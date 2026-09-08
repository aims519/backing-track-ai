import Observation
import SwiftUI

@MainActor @Observable
final class PlayerViewModel {
    var tempoBpm: Int {
        didSet {
            let clamped = min(max(tempoBpm, 40), 240)
            if clamped != tempoBpm { tempoBpm = clamped; return }
            engine.updateTempo(newTempo: Double(tempoBpm))
        }
    }
    let originalTempoBpm: Int
    private let playableTrack: PlayableTrack
    private let engine: AudioEngine
    private(set) var playbackState: PlaybackState
    
    init(playableTrack: PlayableTrack) {
        self.playableTrack = playableTrack
        self.engine = AudioEngine(playableTrack: playableTrack)
        self.tempoBpm = playableTrack.originalTempo
        self.originalTempoBpm = playableTrack.originalTempo
        self.playbackState = engine.state
        engine.onStateChange = { [weak self] newState in self?.playbackState = newState }
    }
    
    func playPause() {
        engine.state == .playing ? engine.pause() : engine.play()
    }
    
    func resetTempo() {
        tempoBpm = originalTempoBpm
    }
}
