import Foundation

enum HistoryDays: Int, CaseIterable {
    case today = 0, yesterday, earlier
    
    var title: String {
        switch self {
            case .today:  "Today"
            case .yesterday: "Yesterday"
            case .earlier: "Earlier"
        }
    }
}

struct HistorySection: Identifiable {
    let title: String
    let day: HistoryDays
    let entries: [JobHistoryEntry]
    var id: Int { day.rawValue }
}

enum HistoryGrouping {
    static func sections(from entries: [JobHistoryEntry],
                  now: Date = .now,
                  calendar: Calendar = .current) -> [HistorySection] {
        
        var results: [HistorySection] = []
        
        for day in HistoryDays.allCases {
            let entries = entries.filter { bucketEntry(entry: $0, now: now, calendar: calendar) == day }
            
            guard !entries.isEmpty else { continue }
            results.append(HistorySection(title: day.title, day: day, entries: entries))
        }
        
        return results
    }

    private static func bucketEntry(entry: JobHistoryEntry, now: Date, calendar: Calendar) -> HistoryDays {
        
        let entryDay = calendar.startOfDay(for: entry.createdAt)
        let today = calendar.startOfDay(for: now)
        let daysApart = calendar.dateComponents([.day], from: entryDay, to: today).day ?? 0
        
        switch daysApart {
            case ...0: return .today
            case 1: return .yesterday
            default: return .earlier
        }
    }
}


