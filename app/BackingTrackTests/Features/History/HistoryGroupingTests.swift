import Testing
import Foundation
@testable import BackingTrack

@Suite("History grouping")
struct HistoryGroupingTests {
    @Test
    func sectionsWithEmptyInputReturnsEmptyResults() throws {
        let entries: [JobHistoryEntry] = []
        let results = HistoryGrouping.sections(from: entries, now: .now,calendar: .current)

        #expect(results.isEmpty)
    }
    
    @Test
    func sectionsWithOneEntryPerBucketReturnsThreeResults() throws {
        var cal = Calendar(identifier: .gregorian)
        cal.timeZone = TimeZone(identifier: "UTC")!
        
        let todayDate = cal.date(from: DateComponents(year: 2020, month: 1, day: 3))!
        let yesterdayDate = cal.date(from: DateComponents(year: 2020, month: 1, day: 2))!
        let earlierDate = cal.date(from: DateComponents(year: 2020, month: 1, day: 1))!
        
        let entries: [JobHistoryEntry] = [ JobHistoryEntry.preview(createdAt: todayDate), JobHistoryEntry.preview(createdAt: yesterdayDate), JobHistoryEntry.preview(createdAt: earlierDate)]
        let results = HistoryGrouping.sections(from: entries, now: todayDate,calendar: cal)

        #expect(results.map(\.day) == [.today, .yesterday, .earlier])
    }
    
    @Test
    func sectionsWithEmptyBucketsAreOmitted() throws {
        var cal = Calendar(identifier: .gregorian)
        cal.timeZone = TimeZone(identifier: "UTC")!
        
        let todayDate = cal.date(from: DateComponents(year: 2020, month: 1, day: 3))!
        let earlierDate = cal.date(from: DateComponents(year: 2020, month: 1, day: 1))!
        
        let entries: [JobHistoryEntry] = [ JobHistoryEntry.preview(createdAt: todayDate), JobHistoryEntry.preview(createdAt: earlierDate)]
        let results = HistoryGrouping.sections(from: entries,now: todayDate , calendar: cal)

        #expect(results.map(\.day) == [.today, .earlier])
    }
    
    @Test
    func sectionsRespectsMidnightBoundary() throws {
        var cal = Calendar(identifier: .gregorian)
        cal.timeZone = TimeZone(identifier: "UTC")!
        
        let todayDate = cal.date(from: DateComponents(year: 2020, month: 1, day: 2, hour: 0, minute: 1))!
        let yesterdayDate = cal.date(from: DateComponents(year: 2020, month: 1, day: 1, hour: 23, minute: 59))!
        
        let entries: [JobHistoryEntry] = [ JobHistoryEntry.preview(createdAt: todayDate), JobHistoryEntry.preview(createdAt: yesterdayDate)]
        let results = HistoryGrouping.sections(from: entries, now: todayDate, calendar: cal)

        #expect(results.map(\.day) == [.today, .yesterday])
    }
    
    @Test
    func sectionsEntriesOrderedAsExpectedWithinBuckets() throws {
        var cal = Calendar(identifier: .gregorian)
        cal.timeZone = TimeZone(identifier: "UTC")!
        
        let todayDate1 = cal.date(from: DateComponents(year: 2020, month: 1, day: 1, hour: 1, minute: 0))!
        let todayDate2 = cal.date(from: DateComponents(year: 2020, month: 1, day: 1, hour: 2, minute: 0))!
        let todayDate3 = cal.date(from: DateComponents(year: 2020, month: 1, day: 1, hour: 3, minute: 0))!
        
        let entries: [JobHistoryEntry] = [ JobHistoryEntry.preview(createdAt: todayDate3), JobHistoryEntry.preview(createdAt: todayDate2), JobHistoryEntry.preview(createdAt: todayDate1) ]
        
        let results = HistoryGrouping.sections(from: entries, now: todayDate3, calendar: cal)

        #expect(results.first?.entries.map(\.createdAt) == [todayDate3, todayDate2, todayDate1])
    }
}
