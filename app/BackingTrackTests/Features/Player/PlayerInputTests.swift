import Foundation
import Testing
@testable import BackingTrack

@MainActor
struct PlayerInputTests {
    
    @Test func inputFromHistoryEntryCopiesKeyModeCorrectly() throws {
        let historyEntry = JobHistoryEntry.preview(keyMode: "Dorian")
        let input = try #require(PlayerInput.from(entry: historyEntry))
        
        #expect(input.keyMode == "Dorian")
    }
    
    @Test func inputFromGenerationJobCopiesKeyModeCorrectly() throws {
        let job = GenerationJob.preview(status: .complete, keyMode: "Dorian")
        let input = try #require(PlayerInput.from(job: job, style: "Dummy"))
        
        #expect(input.keyMode == "Dorian")
    }
}
