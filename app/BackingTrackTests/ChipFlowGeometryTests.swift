import SwiftUI
import Testing
@testable import BackingTrack

/// `ChipFlowGeometry.insertionIndex` turns a drop point into a reading-order position. The cases
/// that matter are the multi-row ones and the edges — this is the half of the reorder that has
/// actual reasoning in it (the array maths is covered by `ChordArrayReorderingTests`).
struct ChipFlowGeometryTests {

    // A two-row layout: three chips on the top row (y 0...40), two on the bottom (y 50...90).
    private let a = UUID(), b = UUID(), c = UUID(), d = UUID(), e = UUID()
    private var order: [UUID] { [a, b, c, d, e] }
    private var frames: [UUID: CGRect] {
        [
            a: CGRect(x: 0, y: 0, width: 60, height: 40),
            b: CGRect(x: 70, y: 0, width: 60, height: 40),
            c: CGRect(x: 140, y: 0, width: 60, height: 40),
            d: CGRect(x: 0, y: 50, width: 60, height: 40),
            e: CGRect(x: 70, y: 50, width: 60, height: 40),
        ]
    }

    private func index(_ x: CGFloat, _ y: CGFloat) -> Int {
        ChipFlowGeometry.insertionIndex(for: CGPoint(x: x, y: y), orderedChipIDs: order, frames: frames)
    }

    @Test func beforeEverything() {
        #expect(index(-10, -10) == 0)
        #expect(index(5, 10) == 0) // left half of the first chip
    }

    @Test func betweenTwoChipsOnTheTopRow() {
        #expect(index(65, 20) == 1)  // past chip a's midpoint, before b
        #expect(index(135, 20) == 2) // past b, before c
    }

    @Test func pastTheEndOfARowLandsAtTheStartOfTheNext() {
        #expect(index(250, 20) == 3) // right of the whole top row
    }

    @Test func onTheBottomRow() {
        #expect(index(10, 70) == 3)  // left of chip d
        #expect(index(65, 70) == 4)  // past d, before e
    }

    @Test func belowEverythingAppends() {
        #expect(index(100, 500) == 5)
    }

    @Test func aChipWithNoKnownFrameStillCountsAsPassed() {
        // b hasn't reported its frame yet; a drop past the end must still resolve to 5, not 4.
        var partial = frames
        partial[b] = nil
        let result = ChipFlowGeometry.insertionIndex(
            for: CGPoint(x: 100, y: 500),
            orderedChipIDs: order,
            frames: partial
        )
        #expect(result == 5)
    }

    @Test func emptyLayout() {
        #expect(ChipFlowGeometry.insertionIndex(for: .zero, orderedChipIDs: [], frames: [:]) == 0)
    }
}
