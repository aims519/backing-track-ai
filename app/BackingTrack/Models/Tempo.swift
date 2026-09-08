import Foundation

/// The tempo range the app's UI allows, shared by the generate form's tempo slider and the
/// player's live-tempo slider. The API enforces its own bounds; this is just the control's.
enum Tempo {
    static let range: ClosedRange<Double> = 40...240
}
