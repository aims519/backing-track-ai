import SwiftUI

extension MusicStyle {
    var styleIcon: String {
        return switch self {
        case .blues:
            "music.quarternote.3"
        case .rock:
            "guitars.fill"
        case .pop:
            "music.mic"
        case .funk:
            "waveform"
        case .reggae:
            "sun.max.fill"
        case .jazz:
            "pianokeys"
        }
    }
    
    var styleTint: Color {
        return switch self {
        case .blues:
            .blue
        case .rock:
            .purple
        case .pop:
            .pink
        case .funk:
            .brown
        case .reggae:
            .green
        case .jazz:
            .indigo
        }
    }
}

