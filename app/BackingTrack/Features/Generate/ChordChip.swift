import SwiftUI

/// One chord in the progression, shown as a bold, card-like box inside `ChipFlowLayout`.
///
/// Deliberately not a `List` row (see the drag-and-drop notes in `GenerateTrackView`) — choosing
/// and ordering chords is the most hands-on part of building a track, so each chord is a chunky
/// tile you pick up and place. The drag is `.draggable` on the tile; the drop handling lives on
/// the flow container.
struct ChordChip: View {
    let chord: Chord
    let onDelete: () -> Void

    private let cornerRadius: CGFloat = 16

    var body: some View {
        VStack(spacing: 2) {
            Text(chord.displaySymbol)
                .font(.system(.title2, design: .rounded, weight: .bold))
            Text(chord.quality.shortLabel)
                .font(.caption2)
                .textCase(.lowercase)
                .foregroundStyle(.secondary)
        }
        .padding(.horizontal, 18)
        .padding(.vertical, 12)
        .frame(minWidth: 84)
        .background {
            RoundedRectangle(cornerRadius: cornerRadius, style: .continuous)
                .fill(Color.accentColor.opacity(0.16))
        }
        .overlay {
            RoundedRectangle(cornerRadius: cornerRadius, style: .continuous)
                .strokeBorder(Color.accentColor.opacity(0.4), lineWidth: 1)
        }
        .overlay(alignment: .topTrailing) {
            Button(action: onDelete) {
                Image(systemName: "xmark.circle.fill")
                    .font(.subheadline)
                    .foregroundStyle(.secondary, Color(.systemBackground))
                    .padding(5)
                    // Apple's 44pt minimum tap target. The glyph stays small and tucked in the
                    // corner; the frame is what makes a slightly-off tap still land here instead
                    // of starting a drag of the whole chip.
                    .frame(width: 44, height: 44, alignment: .topTrailing)
                    .contentShape(.rect)
            }
            // `.plain` stops the button from claiming the whole tile's tap/drag area.
            .buttonStyle(.plain)
            .accessibilityLabel("Remove \(chord.spokenName)")
        }
        // One VoiceOver element per chip, read as "A Minor 7th" instead of two stops (root, then
        // quality). Children are ignored, so the ✕ button isn't reachable on its own — Remove is
        // offered as an accessibility action instead (see `GenerateTrackView.chordChipFlow`),
        // alongside Duplicate and the reorder actions.
        .accessibilityElement(children: .ignore)
        .accessibilityLabel(chord.spokenName)
    }
}

#if DEBUG
#Preview {
    ChipFlowLayout(spacing: 10) {
        ChordChip(chord: Chord(root: .c, quality: .major)) {}
        ChordChip(chord: Chord(root: .a, quality: .minor)) {}
        ChordChip(chord: Chord(root: .f, quality: .major)) {}
        ChordChip(chord: Chord(root: .g, quality: .dominant7)) {}
        ChordChip(chord: Chord(root: .d, quality: .minor7)) {}
        ChordChip(chord: Chord(root: .e, quality: .sus4)) {}
    }
    .padding()
}
#endif
