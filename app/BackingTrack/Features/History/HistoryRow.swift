import SwiftUI

struct HistoryRow: View {
    let entry: JobHistoryEntry
    
    private var keyModeShortName: String {
        KeyMode(rawValue: entry.keyMode.lowercased())?.shortName ?? entry.keyMode
    }
    
    private var keyRootDisplay: String {
        NoteName(rawValue: entry.keyRoot)?.displayName ?? entry.keyRoot
    }
    
    private var musicStyle: MusicStyle? { decomposeStyle(entry.style).0 }
    
    private var styleSymbol: String {
        return musicStyle?.styleIcon ?? "music.note"
    }
    
    private var styleTint: Color {
        return musicStyle?.styleTint ?? .gray
    }
    
    /// Cap on chords shown per row. A fixed count rather than width-based so a row looks the same at
    /// every Dynamic Type size; `lineLimit(1)` below is only a safety net for the extremes.
    private static let maxVisibleChords = 4

    private var chordsDisplay: String {
        let symbols = entry.chordSymbols.map { Chord(apiSymbol: $0)?.displaySymbol ?? $0 }
        let shown = symbols.prefix(Self.maxVisibleChords).joined(separator: "  ")
        let hidden = symbols.count - Self.maxVisibleChords
        return hidden > 0 ? "\(shown)  +\(hidden)" : shown
    }
    
    var body: some View {
        HStack(spacing: 12) {
            RoundedRectangle(cornerRadius: 10)
                .fill(styleTint.opacity(0.15))
                .frame(width: 50, height: 60)
                .overlay(Image(systemName: styleSymbol).foregroundStyle(styleTint))
            VStack(alignment: .leading, spacing: 4) {
                Text(entry.style.isEmpty ? "Untitled" : entry.style).font(.headline)
                Text(chordsDisplay)
                    .font(.subheadline.monospaced())
                    .lineLimit(1)
                Text("\(keyRootDisplay) \(keyModeShortName) · \(entry.tempo) BPM")
                    .font(.subheadline).foregroundStyle(.secondary)
                Text(entry.createdAt, format: .relative(presentation: .named))
                    .font(.caption).foregroundStyle(.secondary)
                if entry.disposition == .lost {
                    Text(entry.disposition.historyLabel)
                        .font(.caption)
                        .foregroundStyle(.secondary)
                }
            }
            Spacer()
            Image(systemName: entry.disposition.historyIcon).foregroundStyle(entry.disposition.historyTint)
        }
    }
}

#if DEBUG
#Preview("Ready") {
    List {
        HistoryRow(entry: .preview())
    }
}

#Preview("Lost") {
    List {
        HistoryRow(entry: .preview(disposition: .lost))
    }
}


#Preview("In Flight") {
    List {
        HistoryRow(entry: .preview(disposition: .inFlight))
    }
}

#Preview("Long progression") {
    let bluesChords: [String] = ["A7", "A7", "A7", "A7", "D7", "D7", "A7", "A7", "E7", "D7", "A7", "E7"]
    let jazzChords: [String] = ["Cmaj7", "Amin7", "Dmin7", "G7", "Emin7", "F#dim", "Fmaj7", "Bmin7", "E7", "Amin"]
    let flatRootChords: [String] = ["Db", "Gb", "Ab", "Db"]

    // 12-bar blues in A: 12 chords, the realistic worst case from the form.
    let blues = JobHistoryEntry.preview(
        chordSymbols: bluesChords,
        keyRoot: "A",
        keyMode: "Mixolydian",
        style: "Blues - slow shuffle"
    )
    // Mixed qualities and a sharp: widest symbols (maj7, F#dim) to stress the line limit.
    let jazz = JobHistoryEntry.preview(
        chordSymbols: jazzChords,
        keyRoot: "C",
        keyMode: "Ionian",
        style: "Jazz"
    )
    // Not producible by the picker (flat root): exercises the raw-string fallback.
    let flatRoot = JobHistoryEntry.preview(chordSymbols: flatRootChords, style: "Pop")

    return List {
        HistoryRow(entry: blues)
        HistoryRow(entry: jazz)
        HistoryRow(entry: flatRoot)
    }
}
#endif
