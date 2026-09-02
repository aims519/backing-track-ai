import Testing
@testable import BackingTrack

// Exercises the real Simulator Keychain rather than a mock/fake — Keychain access is fast and
// deterministic in the Simulator, and this is the exact code path that runs on-device too, so
// there's little value faking it away.
//
// Swift Testing creates a fresh instance of this struct for every `@Test` method, so `init()`
// runs before each one — used here to guarantee a clean slate even if a previous run crashed
// mid-test and left a stale Keychain item behind.
struct APIKeyStoreTests {
    private let store = KeychainAPIKeyStore()

    init() {
        try? store.deleteKey()
    }

    @Test func returnsNilWhenNoKeyIsSaved() {
        #expect(store.apiKey() == nil)
    }

    @Test func savesAndReturnsTheKey() throws {
        try store.save("test-key-123")
        #expect(store.apiKey() == "test-key-123")
    }

    @Test func overwritesAnExistingKey() throws {
        try store.save("first-key")
        try store.save("second-key")
        #expect(store.apiKey() == "second-key")
    }

    @Test func deleteRemovesTheKey() throws {
        try store.save("test-key-123")
        try store.deleteKey()
        #expect(store.apiKey() == nil)
    }

    @Test func deletingWhenNothingIsSavedDoesNotThrow() throws {
        try store.deleteKey()
    }
}
