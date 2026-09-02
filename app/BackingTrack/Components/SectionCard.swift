import SwiftUI

/// A grouped block — an uppercase caption, a rounded content card, and an optional footnote below
/// — styled to read like an inset-grouped `Form` section.
///
/// Used where a hand-built section is preferred over a real `Form`: `GenerateTrackView` needs
/// drag-and-drop in its chord flow, which is unreliable inside a `List`/`Form` on iOS (the list's
/// scroll gesture competes for the touch); `GenerationProgressView` is a `fullScreenCover` laid
/// out as a plain `ScrollView`. This is the shared shell they use to match `Form`'s look.
struct SectionCard<Content: View>: View {
    private let header: String?
    private let footer: Text?
    private let content: Content

    init(_ header: String? = nil, footer: Text? = nil, @ViewBuilder content: () -> Content) {
        self.header = header
        self.footer = footer
        self.content = content()
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 7) {
            if let header {
                Text(header.uppercased())
                    .font(.footnote)
                    .foregroundStyle(.secondary)
                    .padding(.horizontal, 20)
                    .accessibilityAddTraits(.isHeader)
            }

            content
                .frame(maxWidth: .infinity, alignment: .leading)
                .background(
                    RoundedRectangle(cornerRadius: 12, style: .continuous)
                        .fill(Color(.secondarySystemGroupedBackground))
                )
                .padding(.horizontal, 16)

            if let footer {
                footer
                    .font(.footnote)
                    .foregroundStyle(.secondary)
                    .padding(.horizontal, 20)
            }
        }
    }
}

/// A single label-left / control-right row inside a `SectionCard`, matching a `Form` row's
/// layout and padding. `LabeledContent` supplies the alignment; this just adds the insets.
struct CardRow<Content: View>: View {
    private let label: String
    private let content: Content

    init(_ label: String, @ViewBuilder content: () -> Content) {
        self.label = label
        self.content = content()
    }

    var body: some View {
        LabeledContent(label) { content }
            .padding(.horizontal, 16)
            .padding(.vertical, 11)
    }
}
