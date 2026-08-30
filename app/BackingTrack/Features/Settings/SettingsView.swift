import SwiftUI

/// Lets the user paste in the API key the app needs to authenticate to the Pi-hosted API.
/// Deliberately *not* a login screen — there's no user account, just a single shared secret
/// (see `Networking/APIKeyStore.swift`).
struct SettingsView: View {
    // `@Environment(\.dismiss)` is the current way to close a sheet/screen from within itself —
    // it replaces the older pattern of a view holding a `Binding<Bool>` back to its presenter
    // just to flip it off again.
    @Environment(\.dismiss) private var dismiss

    // Injectable so tests/previews can swap in a fake rather than touching the real Keychain.
    private let store: APIKeyStoring

    @State private var apiKey = ""
    @State private var statusMessage: String?

    init(store: APIKeyStoring = KeychainAPIKeyStore()) {
        self.store = store
    }

    var body: some View {
        NavigationStack {
            Form {
                Section {
                    // `SecureField` masks input like a password field — appropriate for a
                    // secret even though it isn't literally a password.
                    SecureField("API Key", text: $apiKey)
                        .textInputAutocapitalization(.never)
                        .autocorrectionDisabled()
                } footer: {
                    Text("Stored in the Keychain, on this device only.")
                }

                if let statusMessage {
                    Text(statusMessage)
                        .foregroundStyle(.secondary)
                }
            }
            .navigationTitle("API Key")
            .toolbar {
                ToolbarItem(placement: .confirmationAction) {
                    Button("Save", action: save)
                        .disabled(apiKey.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty)
                }
                ToolbarItem(placement: .cancellationAction) {
                    Button("Close") { dismiss() }
                }
            }
            .onAppear {
                // Pre-fill so reopening this screen shows whether a key is already saved — it's
                // read back from the Keychain, never sent anywhere.
                apiKey = store.apiKey() ?? ""
            }
        }
    }

    private func save() {
        // Trim first — pasting from Notes/Mail often carries a trailing newline, which would
        // otherwise silently break the header value.
        let trimmedKey = apiKey.trimmingCharacters(in: .whitespacesAndNewlines)
        do {
            try store.save(trimmedKey)
            statusMessage = "Saved."
        } catch {
            statusMessage = "Couldn't save: \(error.localizedDescription)"
        }
    }
}

#Preview {
    SettingsView()
}
