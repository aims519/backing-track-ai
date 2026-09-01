import SwiftUI

/// A left-to-right, top-to-bottom wrapping layout for the chord chips. SwiftUI has no built-in
/// wrapping `HStack` — `Layout` (iOS 16+) is the supported way to write one: conforming to it
/// gives a real container that measures and places its subviews itself, rather than faking the
/// wrap with a `GeometryReader` hack.
struct ChipFlowLayout: Layout {
    var spacing: CGFloat = 8

    func sizeThatFits(proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) -> CGSize {
        let maxWidth = proposal.width ?? .infinity
        let rows = rows(for: subviews, maxWidth: maxWidth)
        let height = rows.reduce(CGFloat(0)) { total, row in
            total + row.height + (total > 0 ? spacing : 0)
        }
        let width = maxWidth.isFinite ? maxWidth : (rows.map(\.width).max() ?? 0)
        return CGSize(width: width, height: height)
    }

    func placeSubviews(in bounds: CGRect, proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) {
        var y = bounds.minY
        for row in rows(for: subviews, maxWidth: bounds.width) {
            var x = bounds.minX
            for index in row.range {
                subviews[index].place(at: CGPoint(x: x, y: y), anchor: .topLeading, proposal: .unspecified)
                x += subviews[index].sizeThatFits(.unspecified).width + spacing
            }
            y += row.height + spacing
        }
    }

    // MARK: - Row measurement

    private struct Row {
        var range: Range<Int>
        var width: CGFloat
        var height: CGFloat
    }

    /// Groups subview indices into rows, breaking to a new row whenever the next chip would
    /// overflow `maxWidth`. Shared by both `Layout` methods so placement always matches what was
    /// measured.
    private func rows(for subviews: Subviews, maxWidth: CGFloat) -> [Row] {
        var rows: [Row] = []
        var rowStart = 0
        var rowWidth: CGFloat = 0
        var rowHeight: CGFloat = 0

        for index in subviews.indices {
            let size = subviews[index].sizeThatFits(.unspecified)
            let isFirstInRow = index == rowStart
            let widthWithChip = rowWidth + size.width + (isFirstInRow ? 0 : spacing)

            if widthWithChip > maxWidth, !isFirstInRow {
                rows.append(Row(range: rowStart..<index, width: rowWidth, height: rowHeight))
                rowStart = index
                rowWidth = size.width
                rowHeight = size.height
            } else {
                rowWidth = widthWithChip
                rowHeight = max(rowHeight, size.height)
            }
        }
        if rowStart < subviews.count {
            rows.append(Row(range: rowStart..<subviews.count, width: rowWidth, height: rowHeight))
        }
        return rows
    }
}

/// Geometry helpers for the chord chip flow, kept as free functions so the drop-position math is
/// testable without a running view.
enum ChipFlowGeometry {
    /// The reading-order position a drop at `location` falls at: the number of chips that sit
    /// before it. "Before" means on an earlier row, or on the same row and past the chip's
    /// horizontal midpoint. A chip whose frame isn't known yet still counts as passed — not
    /// knowing where it is isn't a reason to stop short of it.
    ///
    /// - Parameters:
    ///   - orderedChipIDs: the chip ids in display order.
    ///   - frames: each chip's frame in the same coordinate space as `location`.
    static func insertionIndex(
        for location: CGPoint,
        orderedChipIDs: [UUID],
        frames: [UUID: CGRect]
    ) -> Int {
        var index = 0
        for id in orderedChipIDs {
            guard let frame = frames[id] else {
                index += 1
                continue
            }
            let isBefore =
                if location.y > frame.maxY { true }
                else if location.y < frame.minY { false }
                else { location.x > frame.midX }
            if isBefore { index += 1 } else { break }
        }
        return index
    }
}
