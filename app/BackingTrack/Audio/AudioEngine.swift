import AVFAudio
import os
import Foundation

enum PlaybackVoice: String, CaseIterable {
    case drums = "Drums", bass = "Bass", chords = "Chords"
}

@MainActor
final class AudioEngine {
    private let log = Logger(subsystem: "com.andrewsavory.backingtrack", category: "audio_engine")
    
    private let playableTrack : PlayableTrack
    
    private let engine = AVAudioEngine()
    private let bassSampler = AVAudioUnitSampler()
    private let bassReverb = AVAudioUnitReverb()
    private let bassMixer = AVAudioMixerNode()
    private let drumSampler = AVAudioUnitSampler()
    private let drumReverb = AVAudioUnitReverb()
    private let drumMixer = AVAudioMixerNode()
    private let chordSampler = AVAudioUnitSampler()
    private let chordEq = AVAudioUnitEQ(numberOfBands: 1)
    private let chordReverb = AVAudioUnitReverb()
    private let chordMixer = AVAudioMixerNode()
    private var sequencer: AVAudioSequencer?
    private let limiter = AVAudioUnitEffect(audioComponentDescription: AudioComponentDescription(
        componentType: kAudioUnitType_Effect,
        componentSubType: kAudioUnitSubType_PeakLimiter,
        componentManufacturer: kAudioUnitManufacturer_Apple,
        componentFlags: 0,
        componentFlagsMask: 0
    ))

    /// Two bars of woodblock clicks before playback starts, when `isCountInEnabled`. Scheduled by
    /// a `Task` off `play()` rather than baked into the sequence — the sequencer has no time
    /// before beat 0, and a leading bar would offset every position calculation.
    private static let countInBars = 2
    private let clickSampler = AVAudioUnitSampler()
    private let beatsInBar: Int
    var isCountInEnabled = true
    private var countInTask: Task<Void, Never>?

    private var mutedVoices: Set<PlaybackVoice> = []
    private var voiceVolumes: [PlaybackVoice: Float]
    
    private var isSetUp = false
    var onStateChange: ((PlaybackState) -> Void)?
    private(set) var state: PlaybackState = .idle {
        didSet {
            onStateChange?(state)
        }
    }

    /// Set when an interruption paused playback, so `onShouldResume` only resumes audio the
    /// *system* stopped — not audio the user paused (a spurious `.ended` while paused would
    /// otherwise restart it, which reads as "pause didn't work").
    private var pausedForInterruption = false

    /// The play head at the moment of `pause()`, so `play()` can restart the sequencer from
    /// there. `currentPositionInBeats` climbs past the loop and never wraps, so it's reduced
    /// into `[0, lengthInBeats)` on resume — otherwise the sequencer restarts in the dead zone
    /// past the last event and plays silence until the next loop.
    private var pausedAtBeat: Double?

    /// An `AVAudioSequencer` doesn't survive a system audio-session interruption — the engine
    /// restarts fine but the sequencer's scheduling is left corrupt (transport says playing,
    /// audio is garbled or silent, and it stays that way even after a manual stop-and-replay).
    /// Set on any interruption; `play()` swaps in a fresh sequencer before the next start.
    private var sequencerNeedsRebuild = false

    private lazy var sessionController: AudioSessionController = {
        let c = AudioSessionController()

        c.onInterruptionBegan = { [weak self] in
            guard let self else { return }
            // The system has already stopped the engine and left the sequencer corrupt; the
            // next play() rebuilds it.
            self.sequencerNeedsRebuild = true
            switch self.state {
            case .playing:
                self.pausedForInterruption = true
                self.pausedAtBeat = self.sequencer?.currentPositionInBeats
                self.state = .paused
                self.log.notice("interruption began — paused at \(self.pausedAtBeat ?? -1)")
            case .countingIn:
                self.cancelCountIn()
                self.state = .idle
                self.log.notice("interruption began during count-in — cancelled")
            default:
                break
            }
        }
        c.onShouldResume = { [weak self] in
            guard let self, self.pausedForInterruption else { return }
            self.log.notice("interruption ended — resuming")
            self.play()
        }
        c.onRouteGone = { [weak self] in self?.pause() }

        return c
    }()
    init(playableTrack: PlayableTrack){
        self.playableTrack = playableTrack
        self.beatsInBar = playableTrack.beatsInBar
        self.voiceVolumes = [.bass: 1, .drums: 1, .chords: 1]
        setUp()
    }
    
    private func setUp() {
        do {
            guard !isSetUp else { return }
            
            // Session setup
            try AVAudioSession.sharedInstance().setCategory(.playback, mode: .default, options: [])
            try AVAudioSession.sharedInstance().setActive(true)
            
            // Listen for audio session notifications
            sessionController.startObserving()
            log.notice("audio session observers started")
            
            // Load soundfont. A missing bundle resource is a build-config error, but surface it
            // as a failed state like every other setup error rather than trapping.
            guard let bankURL = Bundle.main.url(forResource: "GeneralUser-GS", withExtension: "sf2") else {
                log.error("GeneralUser-GS.sf2 missing from the bundle")
                state = .failed("The instrument sounds are missing from this build.")
                return
            }

            // Load instruments
            try bassSampler.loadSoundBankInstrument(
                at: bankURL,
                program: UInt8(33),                              // GM program 33 = Electric Bass (finger), 0-indexed
                bankMSB: UInt8(kAUSampler_DefaultMelodicBankMSB), // 0x79 — standard melodic bank
                bankLSB: UInt8(kAUSampler_DefaultBankLSB)         // 0x00
            )
            
            try drumSampler.loadSoundBankInstrument(
                at: bankURL,
                program: UInt8(0),                              // Standard Kit 0
                bankMSB: UInt8(kAUSampler_DefaultPercussionBankMSB), // 0x78 — standard percussion bank
                bankLSB: UInt8(kAUSampler_DefaultBankLSB)         // 0x00
            )
            
            try chordSampler.loadSoundBankInstrument(
                at: bankURL,
                program: UInt8(2),                              // Electric Grand Piano
                bankMSB: UInt8(kAUSampler_DefaultMelodicBankMSB), // 0x79 — standard melodic bank
                bankLSB: UInt8(kAUSampler_DefaultBankLSB)         // 0x00
            )
            
            // EQ config
            let highPass = chordEq.bands[0]
            highPass.filterType = .highPass
            highPass.frequency = 140
            highPass.bypass = false
            
            // Reverb config
            drumReverb.loadFactoryPreset(.mediumRoom)
            drumReverb.wetDryMix = 20
            
            bassReverb.loadFactoryPreset(.mediumRoom)
            bassReverb.wetDryMix = 30
            
            chordReverb.loadFactoryPreset(.mediumRoom)
            chordReverb.wetDryMix = 50
            
            // Connect samplers to engine
            engine.attach(bassSampler)
            engine.attach(bassReverb)
            engine.attach(bassMixer)
            engine.connect(bassSampler, to: bassReverb, format: nil)
            engine.connect(bassReverb, to: bassMixer, format: nil)
            engine.connect(bassMixer, to: engine.mainMixerNode, format: nil)
            
            engine.attach(drumSampler)
            engine.attach(drumReverb)
            engine.attach(drumMixer)
            engine.connect(drumSampler, to: drumReverb, format: nil)
            engine.connect(drumReverb, to: drumMixer, format: nil)
            engine.connect(drumMixer, to: engine.mainMixerNode, format: nil)
            
            engine.attach(chordSampler)
            engine.attach(chordEq)
            engine.attach(chordReverb)
            engine.attach(chordMixer)
            engine.connect(chordSampler, to: chordEq, format: nil)
            engine.connect(chordEq, to: chordReverb, format: nil)
            engine.connect(chordReverb, to: chordMixer, format: nil)
            engine.connect(chordMixer, to: engine.mainMixerNode, format: nil)
            
            // Mixer config
            bassMixer.pan = 0
            drumMixer.pan = 0
            chordMixer.pan = -0.4
            
            // Limiter to prevent clipping
            engine.attach(limiter)
            engine.disconnectNodeOutput(engine.mainMixerNode)
            engine.connect(engine.mainMixerNode, to: limiter, format: nil)
            engine.connect(limiter, to: engine.outputNode, format: nil)
            
            
            // Count-in click — Standard Kit, played by note number (76/77 = wood block). Straight
            // to the main mixer: it isn't one of the mutable voices.
            try clickSampler.loadSoundBankInstrument(
                at: bankURL,
                program: UInt8(0),
                bankMSB: UInt8(kAUSampler_DefaultPercussionBankMSB),
                bankLSB: UInt8(kAUSampler_DefaultBankLSB)
            )
            engine.attach(clickSampler)
            engine.connect(clickSampler, to: engine.mainMixerNode, format: nil)

            try engine.start()

            sequencer = buildSequencer()

            log.debug("Audio engine, samplers, and sequencer initialised")
            isSetUp = true
            state = .idle

        } catch(let error) {
            log.error("Failed to set up audio engine: \(error.localizedDescription)")
            state = .failed("Failed to set up audio engine: \(error.localizedDescription)")
        }
    }

    /// Builds the sequencer from the track: a tempo event, one looping `AVMusicTrack` per voice
    /// routed to its sampler. Recreated on interruption recovery — an `AVAudioSequencer` does not
    /// survive an audio-session interruption (the engine restarts fine, the sequencer's
    /// scheduling is left corrupted).
    private func buildSequencer() -> AVAudioSequencer {
        let seq = AVAudioSequencer(audioEngine: engine)

        seq.tempoTrack.addEvent(
            AVExtendedTempoEvent(tempo: Double(playableTrack.originalTempo)),
            at: AVMusicTimeStamp(0)
        )

        for voice in PlaybackVoice.allCases {
            let musicTrack = seq.createAndAppendTrack()
            musicTrack.lengthInBeats = playableTrack.track.lengthInBeats
            musicTrack.destinationAudioUnit = getSampler(for: voice)

            for note in PlayerSequenceBuilder.notes(for: voice, in: playableTrack.track) {
                musicTrack.addEvent(
                    AVMIDINoteEvent(
                        channel: 0,
                        key: UInt32(note.key),
                        velocity: UInt32(note.velocity),
                        duration: note.durationInBeats   // beats, not seconds
                    ),
                    at: AVMusicTimeStamp(note.beat)
                )
            }
        }

        for track in seq.tracks {
            track.isLoopingEnabled = true
            track.numberOfLoops = -1   // AVMusicTrackLoopCount.forever
        }

        seq.prepareToPlay()
        return seq
    }



    func play() {
        pausedForInterruption = false
        switch state {
        case .playing, .countingIn:
            break   // already going
        case .paused:
            resumeFromPause()
        case .idle, .failed:
            startFromTop()
        }
    }

    /// Resume where the pause left off. The play head climbs past the loop and never wraps, so
    /// it has to be reduced back into the event range or the sequencer restarts in the dead zone
    /// past the last event (a rebuilt sequencer starts from 0, so the same wrap applies).
    private func resumeFromPause() {
        do {
            guard let seq = try prepareForPlayback() else { return }
            if let saved = pausedAtBeat {
                let resumePosition = wrappedIntoLoop(saved)
                log.debug("Resuming playback - pos \(resumePosition)")
                seq.currentPositionInBeats = resumePosition
            }
            pausedAtBeat = nil
            if !seq.isPlaying { try seq.start() }
            state = .playing
        } catch { failPlayback(error) }
    }

    /// Start from bar 1 — with a count-in first when enabled.
    private func startFromTop() {
        do {
            guard let seq = try prepareForPlayback() else { return }
            seq.currentPositionInBeats = 0
            if isCountInEnabled {
                startCountIn(rate: seq.rate)
            } else {
                try seq.start()
                state = .playing
            }
        } catch { failPlayback(error) }
    }

    /// Session active, engine running, sequencer fresh — the shared prep for any start. `nil`
    /// only if setup failed (which already set `state = .failed`).
    private func prepareForPlayback() throws -> AVAudioSequencer? {
        setUp()

        // Re-activate every time, not just in setUp(): once audio pauses in the background iOS
        // suspends the app and deactivates the session, so a plain restart plays to nowhere.
        // setActive is cheap when it's already active.
        try AVAudioSession.sharedInstance().setActive(true)
        if !engine.isRunning { try engine.start() }

        // An interruption leaves the sequencer corrupt — swap in a fresh one.
        if sequencerNeedsRebuild {
            log.debug("Rebuilding sequencer after interruption...")
            sequencer?.stop()
            sequencer = nil
            sequencer = buildSequencer()
            sequencerNeedsRebuild = false
        }
        return sequencer
    }

    private func wrappedIntoLoop(_ beat: Double) -> Double {
        let loop = playableTrack.track.lengthInBeats
        return loop > 0 ? beat.truncatingRemainder(dividingBy: loop) : beat
    }

    private func failPlayback(_ error: any Error) {
        log.error("Failed to play audio: \(error.localizedDescription)")
        state = .failed("Failed to play audio: \(error.localizedDescription)")
    }

    /// Two bars of woodblock clicks, then start the sequencer. Timed off an absolute clock so the
    /// per-beat jitter doesn't accumulate. Cancelled by `pause()` / `stop()` / `teardown()`.
    private func startCountIn(rate: Float) {
        let secondsPerBeat = 60.0 / (Double(rate) * Double(playableTrack.originalTempo))
        let totalBeats = beatsInBar * Self.countInBars
        state = .countingIn

        countInTask = Task { [weak self] in
            let clock = ContinuousClock()
            let start = clock.now

            for beat in 0..<totalBeats {
                try? await Task.sleep(until: start.advanced(by: .seconds(Double(beat) * secondsPerBeat)), clock: clock)
                guard let self, !Task.isCancelled else { return }
                // Accent (76) on each bar's downbeat, plain (77) elsewhere.
                let isDownbeat = beat % self.beatsInBar == 0
                self.clickSampler.startNote(isDownbeat ? 76 : 77, withVelocity: 100, onChannel: 0)
            }

            try? await Task.sleep(until: start.advanced(by: .seconds(Double(totalBeats) * secondsPerBeat)), clock: clock)
            guard let self, !Task.isCancelled else { return }
            self.countInTask = nil
            if let seq = self.sequencer, !seq.isPlaying { try? seq.start() }
            self.state = .playing
        }
    }

    private func cancelCountIn() {
        countInTask?.cancel()
        countInTask = nil
        clickSampler.sendController(123, withValue: 0, onChannel: 0)   // All Notes Off
    }

    /// Pause. Records the play head, stops the sequencer, silences held notes and pauses the
    /// engine (so nothing rings and iOS reads the output as stopped). `play()` restarts the
    /// sequencer from the recorded — and loop-wrapped — position.
    func pause() {
        // Pressed during the count-in: cancel it, the music never started.
        if state == .countingIn {
            cancelCountIn()
            state = .idle
            return
        }
        guard state == .playing else { return }
        guard let seq = sequencer else { return }

        pausedAtBeat = seq.currentPositionInBeats
        seq.stop()
        for voice in PlaybackVoice.allCases {
            getSampler(for: voice).sendController(123, withValue: 0, onChannel: 0)   // All Notes Off
        }
        engine.pause()
        state = .paused
        log.debug("pause — pos \(self.pausedAtBeat ?? -1), engine \(self.engine.isRunning)")
    }

    /// Stop — resets the sequencer to the top and stops the engine.
    func stop() {
        guard state != .idle else { return }
        cancelCountIn()
        // Otherwise a later interruption-ended would auto-resume what the user just stopped.
        pausedForInterruption = false
        guard let seq = sequencer else { state = .idle; return }

        seq.stop()
        seq.currentPositionInBeats = 0
        engine.pause()

        state = .idle
    }
    
    func updateTempo(newTempo: Double) {
        guard let seq = sequencer else { return }
        seq.rate = Float(newTempo / Double(playableTrack.originalTempo))
    }

    /// Play head in beats. Keeps climbing past the loop point — never wraps — so callers that
    /// want a position within the loop reduce it themselves (see plan Risk 1).
    var currentBeat: Double { sequencer?.currentPositionInBeats ?? 0 }
    
    private func getSampler(for voice: PlaybackVoice) -> AVAudioUnitSampler {
        switch voice {
            case .bass: bassSampler
            case .drums: drumSampler
            case .chords: chordSampler
        }
    }

    private func getMixer(for voice: PlaybackVoice) -> AVAudioMixerNode {
        switch voice {
            case .bass: bassMixer
            case .drums: drumMixer
            case .chords: chordMixer
        }
    }
    
    private func applyVoiceVolume(_ voice: PlaybackVoice) {
        getMixer(for: voice).volume = (mutedVoices.contains(voice) ? 0 : voiceVolumes[voice] ?? 1)
    }
    
    func setVoiceVolume(_ volume: Float, for voice: PlaybackVoice) {
        voiceVolumes[voice] = volume
        applyVoiceVolume(voice)
    }
    
    func setMuted(_ muted: Bool, for voice: PlaybackVoice) {
        if muted { mutedVoices.insert(voice) } else { mutedVoices.remove(voice) }
        applyVoiceVolume(voice)
    }
    
    func isMuted(for voice: PlaybackVoice) -> Bool { mutedVoices.contains(voice) }
    
    func volume(for voice: PlaybackVoice) -> Float { voiceVolumes[voice] ?? 1 }
    
    func teardown() {
        cancelCountIn()
        sessionController.stopObserving()
        sequencer?.stop()
        engine.stop()
        try? AVAudioSession.sharedInstance().setActive(
            false,
            options: .notifyOthersOnDeactivation
        )
        state = .idle
    }
}
