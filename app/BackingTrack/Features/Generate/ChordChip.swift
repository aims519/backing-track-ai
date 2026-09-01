import SwiftUI

/// One chord in the progression, shown as a pickable/draggable chip inside `ChipFlowLayout`.
///
/// Deliberately not a `List` row (see the drag-and-drop notes in `GenerateTrackView`) — choosing
/// and ordering chords is the most hands-on part of building a track, so it's styled like
/// something you pick up and place, not a form field. The drag itself is `.draggable` on the
/// chip; the drop handling lives on the flow container.
struct ChordChip: View {
    let chord: Chord
    let onDelete: () -> Void

    var body: some View {
        HStack(spacing: 6) {
            Text(chord.displaySymbol)
                .font(.headline)
                .fontDesign(.rounded)

            Button(action: onDelete) {
                Image(systemName: "xmark.circle.fill")
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }
            // `.plain` stops the button from taking over the chip's whole tap area — without it
            // the default row-style button styling makes the entire chip highlight as one button.
            .buttonStyle(.plain)
        }
        .padding(.vertical, 8)
        .padding(.horizontal, 12)
        .background {
            RoundedRectangle(cornerRadius: 14, style: .continuous)
                .fill(Color.accentColor.opacity(0.12))
        }
        .overlay {
            RoundedRectangle(cornerRadius: 14, style: .continuous)
                .strokeBorder(Color.accentColor.opacity(0.3))
        }
    }
}

#Preview {
    ChipFlowLayout(spacing: 8) {
        ChordChip(chord: Chord(root: .c, quality: .major)) {}
        ChordChip(chord: Chord(root: .a, quality: .minor)) {}
        ChordChip(chord: Chord(root: .f, quality: .major)) {}
        ChordChip(chord: Chord(root: .g, quality: .dominant7)) {}
        ChordChip(chord: Chord(root: .d, quality: .minor7)) {}
    }
    .padding()
}
