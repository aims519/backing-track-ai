import SwiftUI

struct HistoryEmptyState: View {
    var body: some View {
        ContentUnavailableView(
            "No tracks yet",
            systemImage: "clock.arrow.circlepath",
            description: Text("Backing tracks you've generated appear here.")
        )
    }
}
