import Foundation
import Testing
@testable import BackingTrack

@MainActor
struct PlayerViewModelTests {

    // A valid one-bar track document, PascalCase wire format.
    private static let trackJSON = Data("""
    {"LengthInBeats":16,"Drums":[{"MidiNote":36,"BeatPosition":0,"DurationInBeats":0.5,"Velocity":110}],\
    "Bass":[{"MidiNote":38,"BeatPosition":0,"DurationInBeats":1,"Velocity":95}],\
    "ChordVoicings":[{"MidiNotes":[60,64,67],"BeatPosition":0,"DurationInBeats":4,"Velocity":80}]}
    """.utf8)

    private func makeModel(
        input: PlayerInput = .preview(),
        apiClient: any BackingTrackAPIClient,
        historyStore: RecordingJobHistoryStore = RecordingJobHistoryStore(),
        engine: FakePlayerEngine = FakePlayerEngine(),
        nowPlaying: FakeNowPlaying = FakeNowPlaying()
    ) -> (PlayerViewModel, FakePlayerEngine, RecordingJobHistoryStore, FakeNowPlaying) {
        let model = PlayerViewModel(
            input: input,
            apiClient: apiClient,
            historyStore: historyStore,
            makeEngine: { _ in engine },
            makeNowPlaying: { _, _ in nowPlaying }
        )
        return (model, engine, historyStore, nowPlaying)
    }

    // MARK: - Loading

    @Test func cacheMissDownloadsCachesAndBuildsTheEngine() async {
        let api = FakeTrackAPIClient(bytes: Self.trackJSON)
        let (model, engine, store, _) = makeModel(apiClient: api)

        await model.load()

        #expect(api.downloadCalls == 1)
        #expect(store.caches.count == 1)
        #expect(engine.tempoUpdates.last == 120)   // seeded to the input tempo
        guard case .loaded = model.phase else { Issue.record("expected .loaded, got \(model.phase)"); return }
    }

    @Test func aCacheHitSkipsTheDownload() async {
        let api = FakeTrackAPIClient(bytes: Self.trackJSON)
        let store = RecordingJobHistoryStore()
        let cached = JobHistoryEntry.preview()
        cached.preciseContentData = Self.trackJSON
        cached.contentKindValue = .preciseMidiJson
        store.stubbedEntry = cached

        let (model, _, _, _) = makeModel(apiClient: api, historyStore: store)
        await model.load()

        #expect(api.downloadCalls == 0)
        guard case .loaded = model.phase else { Issue.record("expected .loaded"); return }
    }

    @Test func aDownloadTransportErrorFailsAndRetryReDownloads() async {
        let api = FakeTrackAPIClient(returning: .failure(.transport(URLError(.notConnectedToInternet))))
        let (model, _, _, _) = makeModel(apiClient: api)

        await model.load()

        guard case .failed(.download) = model.phase else {
            Issue.record("expected .failed(.download), got \(model.phase)"); return
        }

        await model.retry()
        #expect(api.downloadCalls == 2)
    }

    @Test func corruptBytesFailWithDecode() async {
        let api = FakeTrackAPIClient(bytes: Data("not a track".utf8))
        let (model, _, _, _) = makeModel(apiClient: api)

        await model.load()

        guard case .failed(.decode) = model.phase else {
            Issue.record("expected .failed(.decode), got \(model.phase)"); return
        }
    }

    @Test func aVibeInputFailsFastWithoutBuildingAnEngine() async {
        let api = FakeTrackAPIClient(bytes: Self.trackJSON)
        let (model, engine, _, _) = makeModel(input: .preview(mode: "Vibe"), apiClient: api)

        await model.load()

        guard case .failed(.notPrecise) = model.phase else {
            Issue.record("expected .failed(.notPrecise)"); return
        }
        #expect(api.downloadCalls == 0)
        #expect(engine.playCalls == 0)
    }

    // MARK: - Transport / tempo / mixer forwarding

    @Test func playPauseTogglesThroughTheEngineAndMirrorsState() async {
        let (model, engine, _, _) = makeModel(apiClient: FakeTrackAPIClient(bytes: Self.trackJSON))
        await model.load()

        model.playPause()
        #expect(engine.playCalls == 1)
        #expect(model.playbackState == .playing)

        model.playPause()
        #expect(engine.pauseCalls == 1)
        #expect(model.playbackState == .paused)
    }

    @Test func engineDrivenStateChangesReachTheMirror() async {
        let (model, engine, _, _) = makeModel(apiClient: FakeTrackAPIClient(bytes: Self.trackJSON))
        await model.load()

        engine.simulateStateChange(to: .failed("interrupted"))
        #expect(model.playbackState == .failed("interrupted"))
    }

    @Test func setTempoClampsAndForwards() async {
        let (model, engine, _, _) = makeModel(apiClient: FakeTrackAPIClient(bytes: Self.trackJSON))
        await model.load()

        model.setTempo(999)
        #expect(model.tempoBpm == 240)
        #expect(engine.tempoUpdates.last == 240)

        model.resetTempo()
        #expect(model.tempoBpm == model.originalTempoBpm)
    }

    @Test func stopForwardsToTheEngine() async {
        let (model, engine, _, _) = makeModel(apiClient: FakeTrackAPIClient(bytes: Self.trackJSON))
        await model.load()

        model.playPause()
        model.stop()
        #expect(engine.stopCalls == 1)
        #expect(model.playbackState == .idle)
    }

    @Test func countInForwardsToTheEngine() async {
        let (model, engine, _, _) = makeModel(apiClient: FakeTrackAPIClient(bytes: Self.trackJSON))
        await model.load()

        model.setCountIn(true)
        #expect(model.isCountInEnabled)
        #expect(engine.isCountInEnabled)
    }

    @Test func muteAndVolumeForwardToTheEngine() async {
        let (model, engine, _, _) = makeModel(apiClient: FakeTrackAPIClient(bytes: Self.trackJSON))
        await model.load()

        model.setMuted(true, for: .drums)
        #expect(model.isMuted(.drums))
        #expect(engine.mutes.last?.muted == true)
        #expect(engine.mutes.last?.voice == .drums)

        model.setVolume(0.3, for: .bass)
        #expect(model.volume(.bass) == 0.3)
        #expect(engine.volumes.last?.volume == 0.3)
    }

    @Test func teardownForwardsToTheEngineAndNowPlaying() async {
        let (model, engine, _, nowPlaying) = makeModel(apiClient: FakeTrackAPIClient(bytes: Self.trackJSON))
        await model.load()

        model.teardown()
        #expect(engine.teardownCalls == 1)
        #expect(nowPlaying.teardownCalls == 1)
    }

    // MARK: - Now Playing

    @Test func remoteCommandsDriveTheEngine() async {
        let (model, engine, _, nowPlaying) = makeModel(apiClient: FakeTrackAPIClient(bytes: Self.trackJSON))
        await model.load()

        nowPlaying.onPlay?()
        #expect(engine.playCalls == 1)

        nowPlaying.onPause?()
        #expect(engine.pauseCalls == 1)
    }

    @Test func nowPlayingIsSeededOnLoadAndFollowsTheTempo() async {
        let (model, _, _, nowPlaying) = makeModel(apiClient: FakeTrackAPIClient(bytes: Self.trackJSON))
        await model.load()

        #expect(!nowPlaying.updates.isEmpty)

        model.setTempo(240)
        // 16-beat loop at 240 BPM = 4 seconds.
        #expect(nowPlaying.updates.last?.duration == 4)
    }
}
