import Foundation
import Testing
@testable import BackingTrack

/// The hand-written `CodingKeys` on `GenerationRequest` exist solely to match the API's
/// PascalCase field names (`GenerationRequestDto`). This pins that mapping so a property rename
/// can't silently change the wire format.
struct GenerationRequestTests {

    @Test func encodesWithTheApisPascalCaseKeys() throws {
        let request = GenerationRequest(
            mode: "Precise",
            tempo: 120,
            chords: ["C", "Amin", "F", "G7"],
            keyRoot: "C",
            keyMode: "Ionian",
            style: "rock",
            timeSignatureBeatCount: 4,
            timeSignatureBeatUnit: 4
        )

        let data = try JSONEncoder().encode(request)
        let json = try #require(try JSONSerialization.jsonObject(with: data) as? [String: Any])

        #expect(
            Set(json.keys) == [
                "Mode", "Tempo", "Chords", "KeyRoot", "KeyMode", "Style",
                "TimeSignatureBeatCount", "TimeSignatureBeatUnit",
            ]
        )
        #expect(json["Chords"] as? [String] == ["C", "Amin", "F", "G7"])
        #expect(json["KeyRoot"] as? String == "C")
        #expect(json["TimeSignatureBeatUnit"] as? Int == 4)
    }

    @Test func encodesTheDeviceTokenUnderItsPascalCaseKey() throws {
        let request = GenerationRequest(
            mode: "Precise",
            tempo: 120,
            chords: ["C"],
            keyRoot: "C",
            keyMode: "Ionian",
            style: "rock",
            timeSignatureBeatCount: 4,
            timeSignatureBeatUnit: 4,
            apnsDeviceToken: "ab12"
        )

        let data = try JSONEncoder().encode(request)
        let json = try #require(try JSONSerialization.jsonObject(with: data) as? [String: Any])

        // The test above pins the other side: with no token, the key is left out entirely.
        #expect(json["ApnsDeviceToken"] as? String == "ab12")
    }
}
