import SwiftUI

struct HistoryRow: View {
    let entry: JobHistoryEntry
    
    var body: some View {
        HStack {
            VStack(alignment: .leading, spacing: 4) {
                Text(entry.style.isEmpty ? "Untitled" : entry.style).font(.headline)
                Text("\(entry.keyRoot) · \(entry.tempo) BPM · \(entry.chordSymbols.count) chords ").font(.subheadline).foregroundStyle(.secondary)
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
#endif
