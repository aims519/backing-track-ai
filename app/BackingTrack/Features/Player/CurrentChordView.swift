import SwiftUI

/// The chord being played, large enough to read from a music stand, with the next chord beside it
/// and a row of beat dots underneath. Pure, like `ChordProgressionStrip`, so it previews without
/// an engine.
struct CurrentChordView: View {
    let symbols: [String]
    let currentBar: Int
    /// `nil` when nothing is playing, so no dot is lit — beat 0 would read as "on the one".
    let currentBeat: Int?
    let beatsInBar: Int

    // Scales with Dynamic Type; the text styles top out too small for this.
    @ScaledMetric(relativeTo: .largeTitle) private var chordSize: CGFloat = 72

    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            HStack(alignment: .firstTextBaseline) {
                Text(label(at: currentBar))
                    .font(.system(size: chordSize, weight: .bold, design: .rounded))
                    .lineLimit(1)
                    .minimumScaleFactor(0.4)

                Spacer(minLength: 12)

                // A one-bar loop's "next" is itself, which says nothing.
                if symbols.count > 1 {
                    VStack(alignment: .trailing, spacing: 2) {
                        Text("NEXT")
                            .font(.caption2.weight(.semibold))
                        Text(label(at: currentBar + 1))
                            .font(.title.weight(.semibold))
                            .lineLimit(1)
                    }
                    .foregroundStyle(.secondary)
                }
            }

            beatDots
        }
        .padding(16)
        .accessibilityElement(children: .ignore)
        .accessibilityLabel(accessibilityText)
    }

    private var beatDots: some View {
        // Tighter for long bars (12/8 and up) so they still fit on one line on a small phone.
        HStack(spacing: beatsInBar > 8 ? 4 : 10) {
            ForEach(0..<beatsInBar, id: \.self) { beat in
                Circle()
                    .fill(beat == currentBeat ? Color.accentColor : Color(.tertiarySystemFill))
                    .aspectRatio(1, contentMode: .fit)
                    .frame(maxWidth: 16)
            }
        }
    }

    /// Wraps, so the last bar's "next" is the first — it's a loop.
    private func label(at bar: Int) -> String {
        guard !symbols.isEmpty else { return "–" }
        let symbol = symbols[bar % symbols.count]
        return Chord(apiSymbol: symbol)?.displaySymbol ?? symbol
    }

    private var accessibilityText: String {
        var text = "Current chord \(label(at: currentBar))"
        if symbols.count > 1 { text += ", next \(label(at: currentBar + 1))" }
        if let currentBeat { text += ", beat \(currentBeat + 1) of \(beatsInBar)" }
        return text
    }
}

#if DEBUG
#Preview {
    VStack(spacing: 24) {
        CurrentChordView(symbols: ["C", "Amin7", "F", "G7"], currentBar: 1, currentBeat: 2, beatsInBar: 4)
        CurrentChordView(symbols: ["C", "Amin7", "F", "G7"], currentBar: 3, currentBeat: nil, beatsInBar: 4)
        CurrentChordView(symbols: ["Cmaj7", "F#min7b5"], currentBar: 1, currentBeat: 7, beatsInBar: 12)
    }
    .background(Color(.secondarySystemGroupedBackground))
}
#endif
