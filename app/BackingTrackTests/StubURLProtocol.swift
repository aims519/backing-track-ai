import Foundation
@testable import BackingTrack

/// A `URLProtocol` that answers every request from an in-test closure instead of the network.
///
/// `URLProtocol` is `URLSession`'s request-interception point: register a subclass on a
/// `URLSessionConfiguration` and `URLSession` routes matching requests through it. This is the
/// standard way to test networking code without a live server or a third-party mock library.
///
/// `handler` is a `static` because `URLProtocol` instances are created by `URLSession` itself —
/// the test never holds one to configure. A `@TaskLocal` would be tidier but Swift Testing runs
/// tests in parallel by default; `.serialized` on the suite keeps the shared handler safe.
final class StubURLProtocol: URLProtocol {
    /// Given the outgoing request, return the status code, headers and body to respond with —
    /// or throw to simulate a transport failure.
    nonisolated(unsafe) static var handler: ((URLRequest) throws -> (Int, [String: String], Data))?

    /// A `URLSession` wired to use this protocol and nothing else.
    static func makeSession() -> URLSession {
        let config = URLSessionConfiguration.ephemeral
        config.protocolClasses = [StubURLProtocol.self]
        return URLSession(configuration: config)
    }

    override class func canInit(with request: URLRequest) -> Bool { true }
    override class func canonicalRequest(for request: URLRequest) -> URLRequest { request }

    override func startLoading() {
        guard let handler = Self.handler else {
            client?.urlProtocol(self, didFailWithError: URLError(.unknown))
            return
        }
        do {
            let (status, headers, body) = try handler(request)
            let response = HTTPURLResponse(
                url: request.url!,
                statusCode: status,
                httpVersion: "HTTP/1.1",
                headerFields: headers
            )!
            client?.urlProtocol(self, didReceive: response, cacheStoragePolicy: .notAllowed)
            client?.urlProtocol(self, didLoad: body)
            client?.urlProtocolDidFinishLoading(self)
        } catch {
            client?.urlProtocol(self, didFailWithError: error)
        }
    }

    override func stopLoading() {}
}

/// In-memory `APIKeyStoring` for tests — no Keychain.
struct FakeAPIKeyStore: APIKeyStoring {
    var key: String?
    func apiKey() -> String? { key }
    func save(_ key: String) throws {}
    func deleteKey() throws {}
}
