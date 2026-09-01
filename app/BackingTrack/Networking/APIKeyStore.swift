import Foundation
import Security

/// Persists the shared-secret API key the app sends to the Pi-hosted API's `X-API-Key` header
/// (see `api/README.md`). This is *not* a login credential — there's no user account system
/// (single-user app, see `docs/backing-track-generator-scope.md`'s Non-Goals) — just a static
/// secret that needs to live somewhere more secure than `UserDefaults` (an unencrypted plist)
/// or hardcoded source.
// `Sendable`: the API client holds a store and crosses actor boundaries. Every conformer here is
// a value type over immutable/Sendable state, so this is a safe requirement to add.
protocol APIKeyStoring: Sendable {
    /// The stored key, or `nil` if none has been saved yet.
    func apiKey() -> String?
    /// Saves the key, overwriting any previously stored value.
    func save(_ key: String) throws
    /// Removes the stored key, if any. Not an error if nothing was stored.
    func deleteKey() throws
}

enum APIKeyStoreError: Error {
    case unexpectedStatus(OSStatus)
}

/// Keychain-backed implementation. The Keychain is the standard place for a small secret like
/// this on iOS — encrypted at rest and device-local, unlike `UserDefaults`.
struct KeychainAPIKeyStore: APIKeyStoring {
    // `service` scopes Keychain items to this app; `account` distinguishes this particular
    // secret from anything else the app might store in the Keychain later.
    private let service = "com.andrewsavory.backingtrack"
    private let account = "apiKey"

    private var baseQuery: [String: Any] {
        [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: account,
        ]
    }

    func apiKey() -> String? {
        var query = baseQuery
        query[kSecReturnData as String] = true
        query[kSecMatchLimit as String] = kSecMatchLimitOne

        var result: AnyObject?
        let status = SecItemCopyMatching(query as CFDictionary, &result)

        guard status == errSecSuccess, let data = result as? Data else {
            return nil
        }
        return String(data: data, encoding: .utf8)
    }

    func save(_ key: String) throws {
        guard let data = key.data(using: .utf8) else {
            throw APIKeyStoreError.unexpectedStatus(errSecParam)
        }

        // Keychain items are added or updated, not upserted — a second SecItemAdd for the same
        // service/account fails with errSecDuplicateItem, so check what's there first.
        if apiKey() != nil {
            let status = SecItemUpdate(
                baseQuery as CFDictionary,
                [kSecValueData as String: data] as CFDictionary
            )
            guard status == errSecSuccess else {
                throw APIKeyStoreError.unexpectedStatus(status)
            }
        } else {
            var newItem = baseQuery
            newItem[kSecValueData as String] = data
            // Readable only once the device has been unlocked at least once since boot, and
            // never migrates off this device (e.g. via an encrypted backup restored elsewhere)
            // — the standard choice for a device-local secret like this.
            newItem[kSecAttrAccessible as String] = kSecAttrAccessibleWhenUnlockedThisDeviceOnly
            let status = SecItemAdd(newItem as CFDictionary, nil)
            guard status == errSecSuccess else {
                throw APIKeyStoreError.unexpectedStatus(status)
            }
        }
    }

    func deleteKey() throws {
        let status = SecItemDelete(baseQuery as CFDictionary)
        guard status == errSecSuccess || status == errSecItemNotFound else {
            throw APIKeyStoreError.unexpectedStatus(status)
        }
    }
}
