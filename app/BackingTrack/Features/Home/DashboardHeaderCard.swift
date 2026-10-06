import SwiftUI

/// The dashboard's brand header: the app icon's amber gradient, the logo, and
/// the New Track button. Fixed colours rather than `AccentColor` so it matches the icon in both
/// appearances (`app/Tools/generate_icon.py` draws the same gradient).
struct DashboardHeaderCard: View {
    let onNewTrack: () -> Void

    private let amberTop = Color(red: 0.949, green: 0.647, blue: 0.290)  // #F2A54A
    private let amberBottom = Color(red: 0.851, green: 0.424, blue: 0.165)  // #D96C2A

    var body: some View {
        HStack(spacing: 16) {
            // LaunchLogo is a white glyph drawn for the amber launch screen, so it sits straight on
            // the gradient. Decorative, so hidden from VoiceOver.
            Image("LaunchLogo")
                .resizable()
                .scaledToFit()
                .frame(width: 48, height: 48)
                .accessibilityHidden(true)

            // White button, amber label: stays the obvious action on an orange background.
            Button(action: onNewTrack) {
                Label("New Track", systemImage: "plus")
                    .frame(maxWidth: .infinity)
                    .foregroundStyle(amberBottom)
            }
            .buttonStyle(.borderedProminent)
            .tint(.white)
            .controlSize(.large)
        }
        .padding(20)
        .frame(maxWidth: .infinity)
        .background(
            LinearGradient(
                colors: [amberTop, amberBottom],
                startPoint: .topLeading,
                endPoint: .bottomTrailing
            ),
            in: RoundedRectangle(cornerRadius: 20, style: .continuous)
        )
        .padding(.horizontal, 16)
    }
}

#if DEBUG
#Preview {
    DashboardHeaderCard(onNewTrack: {})
        .padding(.vertical, 16)
        .background(Color(.systemGroupedBackground))
}
#endif
