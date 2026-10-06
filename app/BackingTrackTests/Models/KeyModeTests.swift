import Foundation
import Testing
@testable import BackingTrack

struct KeyModeTests {

    /// History stores the API spelling (`"Mixolydian"`), and `HistoryRow` gets back to the enum by
    /// lowercasing it into a raw value. If the two ever diverge, rows silently fall back to the raw
    /// string instead of the short name.
    @Test(arguments: KeyMode.allCases)
    func storedApiValueResolvesBackToTheSameMode(mode: KeyMode) {
        #expect(KeyMode(rawValue: mode.apiValue.lowercased()) == mode)
    }

    @Test func ionianAndAeolianUseTheirFamiliarNames() {
        #expect(KeyMode.ionian.shortName == "Major")
        #expect(KeyMode.aeolian.shortName == "Minor")
    }

    @Test func otherModesKeepTheirModeName() {
        #expect(KeyMode.dorian.shortName == "Dorian")
        #expect(KeyMode.mixolydian.shortName == "Mixolydian")
    }
}
