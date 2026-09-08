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
    
    private var mutedVoices: Set<PlaybackVoice> = []
    private var voiceVolumes: [PlaybackVoice: Float]
    
    private var isSetUp = false
    var onStateChange: ((PlaybackState) -> Void)?
    private(set) var state: PlaybackState = .idle {
        didSet {
            onStateChange?(state)
        }
    }
    
    private lazy var sessionController: AudioSessionController = {
        let c = AudioSessionController()
        
        c.onInterruptionBegan = { [weak self] in self?.pause() }
        c.onShouldResume = { [weak self]  in self?.play() }
        c.onRouteGone = { [weak self] in self?.pause() }
        
        return c
    }()
    private var interruptionListenTask: Task<Void, Never>?
    private var routeChangeListenTask: Task<Void, Never>?
    
    init(playableTrack: PlayableTrack){
        self.playableTrack = playableTrack
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

            if !engine.isRunning {
                try engine.start()   // belt-and-suspenders, see note below
            }
            
            if state == .idle { seq.currentPositionInBeats = 0 }

            try seq.start()
            state = .playing
        } catch(let error) {
            log.error("Failed to play audio: \(error.localizedDescription)")
            state = .failed("Failed to play audio: \(error.localizedDescription)")
        }
    }
    
    /// Stop audio and keep current sequencer position
    func pause() {
        guard state == .playing else { return }
        guard let seq = sequencer else { return }
        
        seq.stop()
        
        state = .paused
    }
    
    /// Stop audio and reset sequencer position to the start
    func stop() {
        guard state == .playing else { return }
        guard let seq = sequencer else { return }
        
        seq.stop()
        seq.currentPositionInBeats = 0
        
        state = .idle
    }
    
    func updateTempo(newTempo: Double) {
        guard let seq = sequencer else { return }
        seq.rate = Float(newTempo / Double(playableTrack.originalTempo))
    }
    
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
