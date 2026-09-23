import SwiftUI

/// A horizontally scrolling row of chord cells, one per bar, with the current bar highlighted.
/// Pure — takes the raw symbols and the current bar index — so it previews without an engine.
struct ChordProgressionStrip: View {
    let symbols: [String]
    let currentBar: Int

    var body: some View {
        ScrollViewReader { proxy in
            ScrollView(.horizontal, showsIndicators: false) {
                HStack(spacing: 8) {
                    ForEach(Array(symbols.enumerated()), id: \.offset) { index, symbol in
                        cell(symbol, active: index == currentBar)
                            .id(index)
                    }
                }
                .padding(.horizontal, 16)
                .padding(.vertical, 14)
            }
            .onChange(of: currentBar) { _, bar in
                withAnimation(.easeInOut(duration: 0.2)) {
                    proxy.scrollTo(bar, anchor: .center)
                }
            }
        }
    }

    /// The parsed symbol reads better (`Am7` not `Amin7`); fall back to the raw text for anything
    /// the structured parser can't produce.
    private func label(for symbol: String) -> String {
        Chord(apiSymbol: symbol)?.displaySymbol ?? symbol
    }

    private func cell(_ symbol: String, active: Bool) -> some View {
        Text(label(for: symbol))
            .font(.title3.weight(active ? .semibold : .regular))
            .foregroundStyle(active ? Color.white : Color.primary)
            .padding(.horizontal, 14)
            .padding(.vertical, 8)
            .background(
                RoundedRectangle(cornerRadius: 8, style: .continuous)
                    .fill(active ? Color.accentColor : Color(.tertiarySystemFill))
            )
    }
}

#if DEBUG
#Preview {
    VStack(spacing: 24) {
        ChordProgressionStrip(symbols: ["C", "Amin", "F", "G7"], currentBar: 0)
        ChordProgressionStrip(symbols: ["C", "Amin", "F", "G7"], currentBar: 2)
        ChordProgressionStrip(
            symbols: ["Cmaj7", "Amin7", "Dmin7", "G7", "Emin7", "Amin7", "Dmin7", "G7"],
            currentBar: 4
        )
    }
    .padding()
    .background(Color(.systemGroupedBackground))
}
#endif
