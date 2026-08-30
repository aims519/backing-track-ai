import SwiftUI

/// Placeholder landing screen for the scaffolded project. Will become the chord/key/tempo/style
/// input screen described in `docs/backing-track-generator-scope.md` once generation work starts.
struct HomeView: View {
    var body: some View {
        // `NavigationStack` (iOS 16+) is the current navigation container, replacing the older
        // `NavigationView`. It owns an explicit navigation path, which matters once track
        // generation needs to push a results screen programmatically rather than only from taps.
        NavigationStack {
            VStack(spacing: 16) {
                Image(systemName: "guitars")
                    .font(.system(size: 64))
                    // `.tint` follows the app's accent color (set in Assets.xcassets), rather
                    // than a hardcoded color — stays consistent if the accent color changes.
                    .foregroundStyle(.tint)

                Text("Backing Track AI")
                    .font(.title)
                    .bold()

                Text("Scaffold complete — generation UI comes next.")
                    .foregroundStyle(.secondary)
            }
            .padding()
            .navigationTitle("BackingTrack")
        }
    }
}

#Preview {
    // `#Preview` is a Swift macro (Xcode 15+) replacing the older `PreviewProvider` boilerplate.
    // It powers Xcode's live canvas, rendering this view in the editor without a simulator run.
    HomeView()
}
