import Foundation

/// Talks to the backing-track API's `/tracks` endpoints.
///
/// A protocol so `GenerateTrackViewModel` and its tests can hold a fake instead of hitting the
/// network. `Sendable` because the model is `@MainActor` but `await`s the client from a `Task`,
/// so the instance crosses actor boundaries.
protocol BackingTrackAPIClient: Sendable {
    /// Submits a request and returns the freshly-created job (`POST /tracks/generate`).
    func submitGeneration(_ request: GenerationRequest) async throws -> GenerationJob
    /// Fetches the current state of a job (`GET /tracks/status/{jobId}`). Not used by the submit
    /// flow itself — it's here for the status-polling task that builds on this.
    func jobStatus(id: UUID) async throws -> GenerationJob

    /// Requests cancellation of a job (`POST /tracks/cancel/{jobId}`). The server marks it
    /// `Cancelled` before this call returns (see ADR 0037), so a caller need not poll to confirm.
    func cancelJob(id: UUID) async throws

    /// Raw bytes of `GET /tracks/download/{trackId}`. Not decoded here: for a Precise track the
    /// body is the note-event JSON, and it's the *bytes* that get cached
    /// (`JobHistoryEntry.preciseContentData`) and later re-decoded for offline replay. The
    /// player's `TrackLoader` owns decoding.
    func downloadTrackData(trackID: UUID) async throws -> Data
}

/// The real client. Talks JSON over `URLSession`, authenticating with the `X-API-Key` header the
/// API expects (`api/src/BackingTrack.Api/Auth/ApiKeyFilter.cs`).
struct LiveBackingTrackAPIClient: BackingTrackAPIClient {
    private let baseURL: URL
    private let session: URLSession
    private let apiKeyStore: APIKeyStoring

    /// `session` and `apiKeyStore` are injectable for tests; production uses the shared session
    /// and the real Keychain store.
    init(
        baseURL: URL = APIConfiguration.baseURL,
        session: URLSession = .shared,
        apiKeyStore: APIKeyStoring = KeychainAPIKeyStore()
    ) {
        self.baseURL = baseURL
        self.session = session
        self.apiKeyStore = apiKeyStore
    }

    func submitGeneration(_ request: GenerationRequest) async throws -> GenerationJob {
        var urlRequest = try makeRequest(path: "tracks/generate", method: "POST")
        do {
            urlRequest.httpBody = try JSONEncoder().encode(request)
        } catch {
            // `GenerationRequest` is a plain value type, so this is effectively unreachable — but
            // keeps every throw path out of this client an `APIClientError`.
            throw APIClientError.decoding(String(describing: error))
        }
        // The server is moving `POST /tracks/generate` from 200 to 202 Accepted (the job is queued,
        // not done). Accept both so this build works before and after that server change.
        return try await send(urlRequest, acceptedStatuses: [200, 202])
    }

    func jobStatus(id: UUID) async throws -> GenerationJob {
        let urlRequest = try makeRequest(path: "tracks/status/\(id.uuidString)", method: "GET")
        return try await send(urlRequest)
    }

    func downloadTrackData(trackID: UUID) async throws -> Data {
        let urlRequest = try makeRequest(path: "tracks/download/\(trackID.uuidString)", method: "GET")
        return try await sendForData(urlRequest)
    }

    func cancelJob(id: UUID) async throws {
        let urlRequest = try makeRequest(path: "tracks/cancel/\(id.uuidString)", method: "POST")
        try await sendExpectingNoContent(urlRequest)
    }

    // MARK: - Request building

    private func makeRequest(path: String, method: String) throws -> URLRequest {
        guard let key = apiKeyStore.apiKey(), !key.isEmpty else {
            throw APIClientError.missingAPIKey
        }

        // `appending(path:)` keeps any path already on the base URL (none today, but it means a
        // base URL like `.../api` would still work) and handles the separating slash.
        var urlRequest = URLRequest(url: baseURL.appending(path: path))
        urlRequest.httpMethod = method
        urlRequest.setValue("application/json", forHTTPHeaderField: "Content-Type")
        urlRequest.setValue("application/json", forHTTPHeaderField: "Accept")
        urlRequest.setValue(key, forHTTPHeaderField: "X-API-Key")
        return urlRequest
    }

    // MARK: - Sending & response mapping

    /// Sends a request expecting a `GenerationJob` JSON body on one of `acceptedStatuses`.
    private func send(_ urlRequest: URLRequest, acceptedStatuses: Set<Int> = [200]) async throws -> GenerationJob {
        let (data, http) = try await perform(urlRequest)
        guard acceptedStatuses.contains(http.statusCode) else {
            throw Self.failure(status: http.statusCode, data: data, http: http)
        }
        do {
            return try Self.jobDecoder.decode(GenerationJob.self, from: data)
        } catch {
            throw APIClientError.decoding(String(describing: error))
        }
    }

    /// Sends a request and returns the raw 200 body — for `GET /tracks/download`, where the client
    /// caches and forwards the bytes rather than decoding them. Same status-code → error mapping as
    /// `send`, minus the `.decoding` case (nothing is decoded here).
    private func sendForData(_ urlRequest: URLRequest) async throws -> Data {
        let (data, http) = try await perform(urlRequest)
        guard http.statusCode == 200 else {
            throw Self.failure(status: http.statusCode, data: data, http: http)
        }
        return data
    }

    /// Sends a request expecting `204 No Content` — for `POST /tracks/cancel`, which has nothing
    /// to decode either way.
    private func sendExpectingNoContent(_ urlRequest: URLRequest) async throws {
        let (data, http) = try await perform(urlRequest)
        guard http.statusCode == 204 else {
            throw Self.failure(status: http.statusCode, data: data, http: http)
        }
    }

    /// Runs the request and unwraps the HTTP response, mapping the two transport-level failures
    /// (`URLError`, non-HTTP response) the same way for every caller.
    private func perform(_ urlRequest: URLRequest) async throws -> (Data, HTTPURLResponse) {
        let data: Data
        let response: URLResponse
        do {
            (data, response) = try await session.data(for: urlRequest)
        } catch let urlError as URLError {
            throw APIClientError.transport(urlError)
        }
        guard let http = response as? HTTPURLResponse else {
            throw APIClientError.decoding("Response was not HTTP")
        }
        return (data, http)
    }

    /// Maps a non-200 status to an `APIClientError`. Shared so the job path and the download path
    /// can't drift apart (ADR 0025).
    private static func failure(status: Int, data: Data, http: HTTPURLResponse) -> APIClientError {
        switch status {
        case 400:
            .validation(validationMessages(from: data))
        case 401:
            .unauthorized
        case 429:
            .rateLimited(retryAfter: retryAfter(from: http))
        default:
            .server(status: status)
        }
    }

    /// Pulls the flat list of messages out of an ASP.NET `ValidationProblemDetails` body:
    /// `{ "errors": { "Chords": ["bad chord X", "bad chord Y"], "Tempo": ["..."] } }`.
    private static func validationMessages(from data: Data) -> [String] {
        struct ValidationProblem: Decodable { let errors: [String: [String]] }
        guard let problem = try? JSONDecoder().decode(ValidationProblem.self, from: data) else {
            return []
        }
        return problem.errors.values.flatMap { $0 }
    }

    private static func retryAfter(from response: HTTPURLResponse) -> TimeInterval? {
        guard let raw = response.value(forHTTPHeaderField: "Retry-After") else { return nil }
        return TimeInterval(raw.trimmingCharacters(in: .whitespaces))
    }

    /// The API serialises `DateTimeOffset` as ISO-8601 with an offset. .NET's `System.Text.Json`
    /// writes a *variable-length* fractional-second part with trailing zeros trimmed — so the same
    /// endpoint can return `...43+00:00` (whole second), `...43.5+00:00` (1 digit) or
    /// `...43.328326+00:00` (6). `ISO8601DateFormatter.withFractionalSeconds` accepts *exactly*
    /// three digits and the no-fraction option accepts none, so neither handles the general case.
    /// The fix: normalise the fractional part to exactly three digits (pad or trim), then one
    /// formatter covers everything except the whole-second case, which the fallback handles.
    ///
    /// Formatters are built inside the closure, not captured: `.custom`'s closure is `@Sendable`
    /// and `ISO8601DateFormatter` isn't. A job response carries at most two dates.
    private static let jobDecoder: JSONDecoder = {
        let decoder = JSONDecoder()
        decoder.dateDecodingStrategy = .custom { decoder in
            let raw = try decoder.singleValueContainer().decode(String.self)
            let normalised = Self.normalisingFractionalSeconds(raw)

            let formatter = ISO8601DateFormatter()
            formatter.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
            if let date = formatter.date(from: normalised) { return date }
            formatter.formatOptions = [.withInternetDateTime]
            if let date = formatter.date(from: normalised) { return date }

            throw DecodingError.dataCorrupted(
                .init(codingPath: decoder.codingPath, debugDescription: "Unrecognised date: \(raw)")
            )
        }
        return decoder
    }()

    /// Rewrites a `.f…` fractional-seconds group to exactly three digits — padding `.5` → `.500`
    /// and trimming `.328326` → `.328` — leaving the rest of the timestamp (including the trailing
    /// `Z` or `+hh:mm` offset) untouched. A no-op when there's no fractional part.
    private static func normalisingFractionalSeconds(_ raw: String) -> String {
        guard let dot = raw.firstIndex(of: ".") else { return raw }
        let afterDot = raw.index(after: dot)
        guard let fractionEnd = raw[afterDot...].firstIndex(where: { !$0.isNumber }) else {
            return raw
        }
        let digits = String(raw[afterDot..<fractionEnd])
        let millis = digits.count >= 3
            ? String(digits.prefix(3))
            : digits + String(repeating: "0", count: 3 - digits.count)
        return String(raw[..<afterDot]) + millis + String(raw[fractionEnd...])
    }
}
