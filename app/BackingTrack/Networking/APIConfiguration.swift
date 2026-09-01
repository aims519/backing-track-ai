import Foundation

/// Where the app finds the backing-track API.
///
/// The base URL isn't hardcoded — it comes from the `APIBaseURL` Info.plist key, which is set
/// from the `API_BASE_URL` build setting (see `app/project.yml` and `Config/Local.xcconfig`).
/// Debug builds default to `http://localhost:5198` (the API's local `dotnet run` address);
/// Release / on-device builds get the Pi's Tailscale URL from the machine-local
/// `Config/Local.xcconfig`, the same per-machine pattern as `DEVELOPMENT_TEAM`.
enum APIConfiguration {
    /// The API's base URL. Resolved once, on first access.
    ///
    /// `preconditionFailure` rather than a fallback: an unset URL is a build-configuration
    /// mistake, and failing loudly at launch (only possible on a Release build with no
    /// `Local.xcconfig` value — Debug always has the committed default) points straight at the
    /// fix, exactly as XcodeGen erroring on a missing `Local.xcconfig` forces signing setup.
    static let baseURL: URL = {
        let raw = (Bundle.main.object(forInfoDictionaryKey: "APIBaseURL") as? String) ?? ""
        guard !raw.isEmpty, let url = URL(string: raw) else {
            preconditionFailure(
                "APIBaseURL is not set. Add API_BASE_URL to app/Config/Local.xcconfig — "
                + "see Config/Local.xcconfig.example."
            )
        }
        return url
    }()
}
