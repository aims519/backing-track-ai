import Foundation
@testable import BackingTrack

/// A `BackingTrackAPIClient` whose `downloadTrackData` answers from a fixed result and counts
/// calls. Used by `TrackLoaderTests` (and, later, the player view-model tests). `submitGeneration`
/// / `jobStatus` trap — nothing that downloads also polls.
final class FakeTrackAPIClient: BackingTrackAPIClient, @unchecked Sendable {
    private let result: Result<Data, APIClientError>
    private(set) var downloadCalls = 0
    private(set) var lastTrackID: UUID?

    init(returning result: Result<Data, APIClientError>) {
        self.result = result
    }

    convenience init(bytes: Data) {
        self.init(returning: .success(bytes))
    }

    func submitGeneration(_ request: GenerationRequest) async throws -> GenerationJob {
        fatalError("FakeTrackAPIClient never submits")
    }

    func jobStatus(id: UUID) async throws -> GenerationJob {
        fatalError("FakeTrackAPIClient never polls")
    }

    func cancelJob(id: UUID) async throws {
        fatalError("FakeTrackAPIClient never cancels")
    }

    func downloadTrackData(trackID: UUID) async throws -> Data {
        downloadCalls += 1
        lastTrackID = trackID
        return try result.get()
    }
}
