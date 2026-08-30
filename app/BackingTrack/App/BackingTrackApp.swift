import SwiftUI

// `@main` marks this as the process entry point — SwiftUI's declarative replacement for the
// old UIKit AppDelegate/SceneDelegate pair. `App` is a protocol; conforming types describe
// *what scenes the app has*, and SwiftUI drives the actual UIApplication lifecycle for us.
@main
struct BackingTrackApp: App {
    var body: some Scene {
        // `WindowGroup` is the standard scene for a single-window content app on iOS. Its
        // closure builds the root view for each new window the system creates (iPadOS/macOS
        // can open several; iOS effectively has one).
        WindowGroup {
            HomeView()
        }
    }
}
