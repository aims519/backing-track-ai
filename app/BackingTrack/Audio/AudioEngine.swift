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
    private let bassMixer = AVAudioMixerNode()
    private let drumSampler = AVAudioUnitSampler()
    private let drumMixer = AVAudioMixerNode()
    private let chordSampler = AVAudioUnitSampler()
    private let chordMixer = AVAudioMixerNode()
    private var sequencer: AVAudioSequencer?

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

    private lazy var sessionController: AudioSessionController = {
        let c = AudioSessionController()

        c.onInterruptionBegan = { [weak self] in
            guard let self, self.state == .playing else { return }
            self.pausedForInterruption = true
            self.log.debug("interruption began — pausing")
            self.pause()
        }
        c.onShouldResume = { [weak self] in
            guard let self, self.pausedForInterruption else { return }
            self.pausedForInterruption = false
            self.log.debug("interruption ended — resuming")
            self.play()
        }
        c.onRouteGone = { [weak self] in self?.pause() }

        return c
    }()
    private var interruptionListenTask: Task<Void, Never>?
    private var routeChangeListenTask: Task<Void, Never>?
    
    init(playableTrack: PlayableTrack){
        self.playableTrack = playableTrack
        self.beatsInBar = max(1, Int(playableTrack.beatsPerBar.rounded()))
        self.voiceVolumes = [.bass: 1, .drums: 1, .chords: 1]
        setUp()
    }
    
    private func setUp() {
        do {
            guard !isSetUp else {
                log.debug("Set up skipped")
                return
            }
            
            // Session setup
            try AVAudioSession.sharedInstance().setCategory(.playback, mode: .default, options: [])
            try AVAudioSession.sharedInstance().setActive(true)
            
            // Listen for audio session notifications
            interruptionListenTask = Task { await sessionController.observeInterruptions() }
            routeChangeListenTask = Task { await sessionController.observeRouteChanges() }
            
            // Load sountfont
            let bankURL = Bundle.main.url(forResource: "GeneralUser-GS", withExtension: "sf2")!

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
            
            // Connect samplers to engine
            engine.attach(bassSampler)
            engine.attach(bassMixer)
            engine.connect(bassSampler, to: bassMixer, format: nil)
            engine.connect(bassMixer, to: engine.mainMixerNode, format: nil)
            
            engine.attach(drumSampler)
            engine.attach(drumMixer)
            engine.connect(drumSampler, to: drumMixer, format: nil)
            engine.connect(drumMixer, to: engine.mainMixerNode, format: nil)
            
            engine.attach(chordSampler)
            engine.attach(chordMixer)
            engine.connect(chordSampler, to: chordMixer, format: nil)
            engine.connect(chordMixer, to: engine.mainMixerNode, format: nil)
            
            
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
            
            let seq = AVAudioSequencer(audioEngine: engine)
            
            // Tempo
            let tempoEvent = AVExtendedTempoEvent(tempo: Double(playableTrack.originalTempo)) // BPM
            seq.tempoTrack.addEvent(tempoEvent, at: AVMusicTimeStamp(0))
            
            // One AVMusicTrack per voice, built from the pure translation in PlayerSequenceBuilder.
            // Track order doesn't matter — each routes to its own sampler via destinationAudioUnit.
            for voice in PlaybackVoice.allCases {
                let musicTrack = seq.createAndAppendTrack()
                musicTrack.lengthInBeats = playableTrack.track.lengthInBeats
                musicTrack.destinationAudioUnit = getSampler(for: voice)

                for note in PlayerSequenceBuilder.notes(for: voice, in: playableTrack.track) {
                    let midiEvent = AVMIDINoteEvent(
                        channel: 0,
                        key: UInt32(note.key),
                        velocity: UInt32(note.velocity),
                        duration: note.durationInBeats   // beats, not seconds
                    )
                    musicTrack.addEvent(midiEvent, at: AVMusicTimeStamp(note.beat))
                }
            }

            // Looping
            for track in seq.tracks {
                track.isLoopingEnabled = true
                track.numberOfLoops = -1   // AVMusicTrackLoopCount.forever — loop indefinitely
            }
            
            seq.prepareToPlay()

            sequencer = seq

            log.debug("Audio engine, samplers, and sequencer initialised")
            isSetUp = true
            state = .idle

        } catch(let error) {
            log.error("Failed to set up audio engine: \(error.localizedDescription)")
            state = .failed("Failed to set up audio engine: \(error.localizedDescription)")
        }
    }
    
    
    
    func play() {
        do {
            setUp()
            guard let seq = sequencer else { return }

            // Re-activate every time, not just in setUp(): once audio pauses in the background
            // iOS suspends the app and deactivates the session, and a plain engine/sequencer
            // restart then plays to nowhere (silent until the view is reloaded). setActive is
            // cheap when the session is already active.
            try AVAudioSession.sharedInstance().setActive(true)
            if !engine.isRunning {
                try engine.start()
            }
            pausedForInterruption = false

            switch state {
            case .playing, .countingIn:
                return   // already going

            case .paused:
                // Resume. pause() froze the engine (and so the sequencer's clock). The play head
                // climbs past the loop and never wraps, so reduce it back into the event range or
                // the sequencer restarts in the dead zone past the last event.
                if let saved = pausedAtBeat {
                    let loop = playableTrack.track.lengthInBeats
                    seq.currentPositionInBeats = loop > 0 ? saved.truncatingRemainder(dividingBy: loop) : saved
                }
                pausedAtBeat = nil
                if !seq.isPlaying { try seq.start() }
                state = .playing

            case .idle, .failed:
                seq.currentPositionInBeats = 0
                if isCountInEnabled {
                    startCountIn(rate: seq.rate)
                } else {
                    try seq.start()
                    state = .playing
                }
            }
            log.debug("play — state \(String(describing: self.state)), pos \(seq.currentPositionInBeats)")
        } catch(let error) {
            log.error("Failed to play audio: \(error.localizedDescription)")
            state = .failed("Failed to play audio: \(error.localizedDescription)")
        }
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
        interruptionListenTask?.cancel()
        routeChangeListenTask?.cancel()
        sequencer?.stop()
        engine.stop()
        try? AVAudioSession.sharedInstance().setActive(
            false,
            options: .notifyOthersOnDeactivation
        )
        state = .idle
    }
}
