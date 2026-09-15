import Foundation

/// The body of a `POST /tracks/generate` request. Mirrors the API's `GenerationRequestDto`
/// (`api/src/BackingTrack.Api/Dtos/GenerationRequestDto.cs`).
///
/// Nothing sends this yet — the networking layer is a separate task. It exists now so the form
/// has a concrete value type to build and so that work has a fixed target. `CodingKeys` use the
/// API's PascalCase field names; the server also accepts camelCase, but matching its casing keeps
/// the wire format unambiguous.
struct GenerationRequest: Codable, Hashable, Sendable {
    var mode: String
    var tempo: Int
    var chords: [String]
    var keyRoot: String
    var keyMode: String
    var style: String
    var timeSignatureBeatCount: Int
    var timeSignatureBeatUnit: Int

    enum CodingKeys: String, CodingKey {
        case mode = "Mode"
        case tempo = "Tempo"
        case chords = "Chords"
        case keyRoot = "KeyRoot"
        case keyMode = "KeyMode"
        case style = "Style"
        case timeSignatureBeatCount = "TimeSignatureBeatCount"
        case timeSignatureBeatUnit = "TimeSignatureBeatUnit"
    }
}
