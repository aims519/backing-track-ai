import Foundation

/// Everything that can go wrong talking to the backing-track API, reduced to the cases the UI
/// actually needs to tell apart. `LocalizedError` so `errorDescription` can be shown straight to
/// the user in an alert.
enum APIClientError: Error, LocalizedError, Equatable {
    /// No API key saved yet — the request was never sent. The user needs to add one in Settings.
    case missingAPIKey
    /// The API rejected the key (HTTP 401).
    case unauthorized
    /// Rate limited (HTTP 429). `retryAfter` is the server's `Retry-After` in seconds, if given.
    case rateLimited(retryAfter: TimeInterval?)
    /// The request body failed server-side validation (HTTP 400). Carries the flattened messages
    /// from the `ValidationProblemDetails.errors` dictionary.
    case validation([String])
    /// Any other non-success HTTP status.
    case server(status: Int)
    /// The request never completed — offline, timeout, DNS, connection refused, …
    case transport(URLError)
    /// A 2xx response whose body didn't decode to the expected shape.
    case decoding(String)

    var errorDescription: String? {
        switch self {
        case .missingAPIKey:
            "No API key set. Add one in Settings before generating a track."
        case .unauthorized:
            "The API key was rejected. Check it in Settings."
        case .rateLimited(let retryAfter):
            if let retryAfter {
                "Too many requests. Try again in about \(Int(retryAfter.rounded())) seconds."
            } else {
                "Too many requests. Wait a moment and try again."
            }
        case .validation(let messages):
            messages.isEmpty
                ? "The request was rejected as invalid."
                : "The request was rejected: \(messages.joined(separator: " "))"
        case .server(let status):
            "The server returned an error (HTTP \(status)). Try again shortly."
        case .transport:
            "Couldn't reach the server. Check your internet connection."
        case .decoding:
            "The server's response couldn't be read."
        }
    }

    // `Equatable` is synthesised for every case except `.transport` — `URLError` is `Equatable`,
    // so this is really just here to document that two transport failures compare by code only,
    // which is all the tests need.
    static func == (lhs: APIClientError, rhs: APIClientError) -> Bool {
        switch (lhs, rhs) {
        case (.missingAPIKey, .missingAPIKey), (.unauthorized, .unauthorized):
            true
        case (.rateLimited(let l), .rateLimited(let r)):
            l == r
        case (.validation(let l), .validation(let r)):
            l == r
        case (.server(let l), .server(let r)):
            l == r
        case (.transport(let l), .transport(let r)):
            l.code == r.code
        case (.decoding, .decoding):
            true
        default:
            false
        }
    }
}
