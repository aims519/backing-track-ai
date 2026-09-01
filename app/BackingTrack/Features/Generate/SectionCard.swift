import SwiftUI

/// One grouped block on `GenerateTrackView` — an uppercase caption, a rounded content card, and
/// an optional footnote below — styled to read like an inset-grouped `Form` section.
///
/// The screen can't actually use `Form`: the chord chip flow needs `onDrag`/`onDrop`, which do
/// nothing inside a `List`/`Form` on iOS. So every section is hand-built to match, and this is
/// the shared shell they all use.
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
