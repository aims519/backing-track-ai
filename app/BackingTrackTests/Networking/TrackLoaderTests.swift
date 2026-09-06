import Foundation
import Testing
@testable import BackingTrack

@MainActor
struct TrackLoaderTests {

    private let trackID = UUID()
    private let jobID = UUID()

    private let validBytes = Data("""
    {"LengthInBeats":8,"Drums":[{"MidiNote":36,"BeatPosition":0,"DurationInBeats":0.5,"Velocity":110}],"Bass":[],"ChordVoicings":[]}
    """.utf8)

    private func cachedRow(data: Data) -> JobHistoryEntry {
        let entry = JobHistoryEntry.preview(jobID: jobID, status: .complete)
        entry.preciseContentData = data
        entry.contentKindValue = .preciseMidiJson
        return entry
    }

    @Test func cacheMissDownloadsDecodesAndCaches() async {
        let api = FakeTrackAPIClient(bytes: validBytes)
        let store = RecordingJobHistoryStore()
        let loader = TrackLoader(apiClient: api, historyStore: store)

        let result = await loader.load(trackID: trackID, jobID: jobID)

        guard case .success(let track) = result else {
            Issue.record("expected success, got \(result)")
            return
        }
        #expect(track.lengthInBeats == 8)
        #expect(api.downloadCalls == 1)
        #expect(api.lastTrackID == trackID)
        #expect(store.caches.count == 1)
        #expect(store.caches.first?.data == validBytes)
        #expect(store.caches.first?.jobID == jobID)
    }

    @Test func cacheHitSkipsTheNetwork() async {
        let api = FakeTrackAPIClient(returning: .failure(.transport(URLError(.notConnectedToInternet))))
        let store = RecordingJobHistoryStore()
        store.stubbedEntry = cachedRow(data: validBytes)
        let loader = TrackLoader(apiClient: api, historyStore: store)

        let result = await loader.load(trackID: trackID, jobID: jobID)

        guard case .success = result else {
            Issue.record("expected success from cache, got \(result)")
            return
        }
        #expect(api.downloadCalls == 0)
        #expect(store.caches.isEmpty)   // nothing re-written
    }

    @Test func aDownloadErrorSurfacesAndWritesNoCache() async {
        let api = FakeTrackAPIClient(returning: .failure(.server(status: 404)))
        let store = RecordingJobHistoryStore()
        let loader = TrackLoader(apiClient: api, historyStore: store)

        let result = await loader.load(trackID: trackID, jobID: jobID)

        #expect(result == .failure(.download(.server(status: 404))))
        #expect(store.caches.isEmpty)
    }

    @Test func corruptDownloadedBytesBecomeADecodeError() async {
        let api = FakeTrackAPIClient(bytes: Data("not a track".utf8))
        let store = RecordingJobHistoryStore()
        let loader = TrackLoader(apiClient: api, historyStore: store)

        let result = await loader.load(trackID: trackID, jobID: jobID)

        guard case .failure(.decode) = result else {
            Issue.record("expected .decode, got \(result)")
            return
        }
        #expect(store.caches.isEmpty)
    }

    @Test func aCorruptCacheRowFallsBackToAFreshDownload() async {
        let api = FakeTrackAPIClient(bytes: validBytes)
        let store = RecordingJobHistoryStore()
        store.stubbedEntry = cachedRow(data: Data("corrupt".utf8))
        let loader = TrackLoader(apiClient: api, historyStore: store)

        let result = await loader.load(trackID: trackID, jobID: jobID)

        guard case .success = result else {
            Issue.record("expected success from re-download, got \(result)")
            return
        }
        #expect(api.downloadCalls == 1)
        #expect(store.caches.count == 1)
    }

    @Test func aStoreWriteFailureNeverFailsTheLoad() async {
        let api = FakeTrackAPIClient(bytes: validBytes)
        let store = RecordingJobHistoryStore()
        store.errorToThrow = RecordingJobHistoryStore.StoreFailure()
        let loader = TrackLoader(apiClient: api, historyStore: store)

        let result = await loader.load(trackID: trackID, jobID: jobID)

        guard case .success = result else {
            Issue.record("expected success despite store failure, got \(result)")
            return
        }
    }
}
