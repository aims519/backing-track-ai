import Foundation
import Testing
@testable import BackingTrack

/// `.serialized` because `StubURLProtocol.handler` is shared process-wide — parallel cases would
/// race on it.
@Suite(.serialized)
struct BackingTrackAPIClientTests {

    // `.invalid` is a reserved TLD that can never resolve (RFC 6761) — a guarantee that a test
    // reaching the network by mistake fails loudly rather than hitting something real. Every
    // request is answered by StubURLProtocol before a socket opens, so the host is only ever used
    // to build `URLRequest.url`.
    private let baseURL = URL(string: "https://backingtrack.invalid")!

    private func makeClient(key: String? = "test-key") -> LiveBackingTrackAPIClient {
        LiveBackingTrackAPIClient(
            baseURL: baseURL,
            session: StubURLProtocol.makeSession(),
            apiKeyStore: FakeAPIKeyStore(key: key)
        )
    }

    private let sampleRequest = GenerationRequest(
        mode: "Precise",
        tempo: 120,
        chords: ["C", "Amin", "F", "G7"],
        keyRoot: "C",
        keyMode: "Ionian",
        style: "rock",
        timeSignatureBeatCount: 4,
        timeSignatureBeatUnit: 4
    )

    /// A `GenerationJobDto` body as the API serialises it. The `startTime` shape — an offset plus
    /// six fractional-second digits — is copied verbatim from a real `POST /tracks/generate`
    /// response, which `ISO8601DateFormatter` can't parse without the client's normalisation.
    private func jobJSON(id: String, status: String) -> Data {
        Data("""
        {
          "id": "\(id)",
          "status": "\(status)",
          "startTime": "2026-09-01T21:53:43.328326+00:00",
          "endTime": null,
          "trackId": null,
          "audioLocation": null,
          "mode": "Precise",
          "tempo": 120,
          "chords": ["C", "Amin", "F", "G7"],
          "keyRoot": "C",
          "keyMode": "Ionian",
          "keyModeWarnings": [],
          "timeSignatureBeatCount": 4,
          "timeSignatureBeatUnit": 4
        }
        """.utf8)
    }

    @Test func submitReturnsTheCreatedJob() async throws {
        let id = "3abfc555-3c24-4773-9799-2884b46eb80e"
        StubURLProtocol.handler = { _ in (200, [:], self.jobJSON(id: id, status: "Pending")) }

        let job = try await makeClient().submitGeneration(sampleRequest)

        #expect(job.id == UUID(uuidString: id))
        #expect(job.status == .pending)
        #expect(job.chords == ["C", "Amin", "F", "G7"])
        #expect(job.endTime == nil)
        // 2026-09-01T21:53:43.328326Z — parsed only if fractional-seconds normalisation ran
        // (ISO8601DateFormatter rejects 6 fractional digits). Epoch of the whole-second part:
        let startTime = try #require(job.startTime)
        #expect(abs(startTime.timeIntervalSince1970 - 1_788_299_623) < 1)
    }

    /// .NET's `System.Text.Json` writes a variable-length fractional-seconds part (trailing zeros
    /// trimmed), so the client's date normalisation has to cope with 0–7 digits, not just the
    /// 6-digit case in `jobJSON`.
    @Test(arguments: [
        "2026-09-01T21:53:43+00:00",         // whole second, no fraction
        "2026-09-01T21:53:43.5+00:00",       // 1 digit
        "2026-09-01T21:53:43.25+00:00",      // 2 digits
        "2026-09-01T21:53:43.250+00:00",     // 3 digits
        "2026-09-01T21:53:43.2500000+00:00", // 7 digits
        "2026-09-01T21:53:43.328Z",          // Z instead of +00:00
    ])
    func decodesEveryFractionalSecondsShape(startTime: String) async throws {
        let body = Data("""
        {
          "id": "\(UUID().uuidString)", "status": "Pending", "startTime": "\(startTime)",
          "endTime": null, "trackId": null, "audioLocation": null, "mode": "Precise", "tempo": 120,
          "chords": ["C"], "keyRoot": "C", "keyMode": "Ionian", "keyModeWarnings": [],
          "timeSignatureBeatCount": 4, "timeSignatureBeatUnit": 4
        }
        """.utf8)
        StubURLProtocol.handler = { _ in (200, [:], body) }

        let job = try await makeClient().submitGeneration(sampleRequest)

        // All six represent 21:53:43(.xxx) on 2026-09-01 — within a second of the whole-second epoch.
        let parsed = try #require(job.startTime)
        #expect(abs(parsed.timeIntervalSince1970 - 1_788_299_623) < 1)
    }

    @Test func submitSendsTheKeyAndPascalCaseBody() async throws {
        var seenRequest: URLRequest?
        StubURLProtocol.handler = { request in
            seenRequest = request
            return (200, [:], self.jobJSON(id: UUID().uuidString, status: "Pending"))
        }

        _ = try await makeClient(key: "secret-123").submitGeneration(sampleRequest)

        let request = try #require(seenRequest)
        #expect(request.value(forHTTPHeaderField: "X-API-Key") == "secret-123")
        #expect(request.url?.path == "/tracks/generate")
        // URLProtocol strips the body off the request it hands back (it moves to a stream), so
        // assert on what the encoder produces instead — the wire contract test proper lives in
        // GenerationRequestTests.
        let body = try JSONEncoder().encode(sampleRequest)
        let json = try #require(try JSONSerialization.jsonObject(with: body) as? [String: Any])
        #expect(json["KeyRoot"] as? String == "C")
    }

    @Test func missingKeyFailsBeforeSending() async {
        var handlerCalled = false
        StubURLProtocol.handler = { _ in
            handlerCalled = true
            return (200, [:], Data())
        }

        await #expect(throws: APIClientError.missingAPIKey) {
            try await makeClient(key: nil).submitGeneration(sampleRequest)
        }
        #expect(handlerCalled == false)
    }

    @Test func unauthorizedBecomesAClearError() async {
        StubURLProtocol.handler = { _ in (401, [:], Data()) }

        await #expect(throws: APIClientError.unauthorized) {
            try await makeClient().submitGeneration(sampleRequest)
        }
    }

    @Test func validationErrorsAreFlattened() async throws {
        let body = Data("""
        {
          "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
          "title": "One or more validation errors occurred.",
          "status": 400,
          "errors": {
            "Chords": ["Invalid chord H7", "Invalid chord Q9"],
            "Tempo": ["Tempo must be between 40 and 240 BPM, but was 500"]
          }
        }
        """.utf8)
        StubURLProtocol.handler = { _ in (400, [:], body) }

        let error = await #expect(throws: APIClientError.self) {
            try await makeClient().submitGeneration(sampleRequest)
        }
        guard case .validation(let messages) = error else {
            Issue.record("expected .validation, got \(String(describing: error))")
            return
        }
        #expect(Set(messages) == [
            "Invalid chord H7",
            "Invalid chord Q9",
            "Tempo must be between 40 and 240 BPM, but was 500",
        ])
    }

    @Test func rateLimitCarriesRetryAfter() async {
        StubURLProtocol.handler = { _ in (429, ["Retry-After": "30"], Data()) }

        await #expect(throws: APIClientError.rateLimited(retryAfter: 30)) {
            try await makeClient().submitGeneration(sampleRequest)
        }
    }

    @Test func transportFailureIsSurfaced() async {
        StubURLProtocol.handler = { _ in throw URLError(.cannotConnectToHost) }

        let error = await #expect(throws: APIClientError.self) {
            try await makeClient().submitGeneration(sampleRequest)
        }
        guard case .transport(let urlError) = error else {
            Issue.record("expected .transport, got \(String(describing: error))")
            return
        }
        #expect(urlError.code == .cannotConnectToHost)
    }

    @Test func jobStatusHitsTheStatusEndpoint() async throws {
        let id = UUID()
        var seenPath: String?
        StubURLProtocol.handler = { request in
            seenPath = request.url?.path
            return (200, [:], self.jobJSON(id: id.uuidString, status: "InProgress"))
        }

        let job = try await makeClient().jobStatus(id: id)

        #expect(seenPath == "/tracks/status/\(id.uuidString)")
        #expect(job.status == .inProgress)
    }

    // MARK: - downloadTrackData

    @Test func downloadHitsTheDownloadEndpointWithTheKeyAndReturnsTheBodyUnchanged() async throws {
        let id = UUID()
        let body = Data(#"{"LengthInBeats":8,"Drums":[],"Bass":[],"ChordVoicings":[]}"#.utf8)
        var seenRequest: URLRequest?
        StubURLProtocol.handler = { request in
            seenRequest = request
            return (200, [:], body)
        }

        let data = try await makeClient(key: "dl-key").downloadTrackData(trackID: id)

        let request = try #require(seenRequest)
        #expect(request.url?.path == "/tracks/download/\(id.uuidString)")
        #expect(request.httpMethod == "GET")
        #expect(request.value(forHTTPHeaderField: "X-API-Key") == "dl-key")
        #expect(data == body)   // bytes forwarded verbatim, not decoded
    }

    @Test func downloadMissingKeyFailsBeforeSending() async {
        var handlerCalled = false
        StubURLProtocol.handler = { _ in
            handlerCalled = true
            return (200, [:], Data())
        }

        await #expect(throws: APIClientError.missingAPIKey) {
            try await makeClient(key: nil).downloadTrackData(trackID: UUID())
        }
        #expect(handlerCalled == false)
    }

    @Test func downloadNotFoundBecomesAServerError() async {
        StubURLProtocol.handler = { _ in (404, [:], Data()) }

        await #expect(throws: APIClientError.server(status: 404)) {
            try await makeClient().downloadTrackData(trackID: UUID())
        }
    }

    @Test func downloadUnauthorizedBecomesAClearError() async {
        StubURLProtocol.handler = { _ in (401, [:], Data()) }

        await #expect(throws: APIClientError.unauthorized) {
            try await makeClient().downloadTrackData(trackID: UUID())
        }
    }

    @Test func downloadTransportFailureIsSurfaced() async {
        StubURLProtocol.handler = { _ in throw URLError(.notConnectedToInternet) }

        let error = await #expect(throws: APIClientError.self) {
            try await makeClient().downloadTrackData(trackID: UUID())
        }
        guard case .transport(let urlError) = error else {
            Issue.record("expected .transport, got \(String(describing: error))")
            return
        }
        #expect(urlError.code == .notConnectedToInternet)
    }
}
