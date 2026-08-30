import SwiftUI

/// Placeholder landing screen for the scaffolded project. Will become the chord/key/tempo/style
/// input screen described in `docs/backing-track-generator-scope.md` once generation work starts.
struct HomeView: View {
    // Drives the settings sheet's presentation — `.sheet(isPresented:)` below reads this rather
    // than the sheet owning its own dismissal state, so the toolbar button can trigger it too.
    @State private var isShowingSettings = false

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
            .toolbar {
                ToolbarItem(placement: .topBarTrailing) {
                    Button {
                        isShowingSettings = true
                    } label: {
                        Label("Settings", systemImage: "gearshape")
                    }
                }
            }
            // Presents SettingsView modally over this screen. A sheet, rather than a pushed
            // NavigationStack destination, since setting the API key is a self-contained task
            // that isn't part of the app's main navigation flow.
            .sheet(isPresented: $isShowingSettings) {
                SettingsView()
            }
        }
    }
}

#Preview {
    // `#Preview` is a Swift macro (Xcode 15+) replacing the older `PreviewProvider` boilerplate.
    // It powers Xcode's live canvas, rendering this view in the editor without a simulator run.
    HomeView()
}
