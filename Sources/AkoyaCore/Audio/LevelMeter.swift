import Foundation

/// Rolling bar-height history for the menu bar waveform. Each column is a step in 1...8;
/// the newest value is on the right.
public struct LevelMeter {
    public static let columnCount = 22

    public private(set) var columns: [Int]

    public init(columnCount: Int = LevelMeter.columnCount) {
        columns = Array(repeating: 1, count: columnCount)
    }

    /// Appends a step for `rms` as the newest (rightmost) column. The new step never falls
    /// more than two below the previous newest column, giving a smooth release.
    public mutating func push(rms: Float) {
        let raw = Self.step(forRMS: rms)
        let previous = columns[columns.count - 1]
        let step = max(raw, previous - 2)
        columns.removeFirst()
        columns.append(step)
    }

    public mutating func reset() {
        columns = Array(repeating: 1, count: columns.count)
    }

    /// Maps dBFS linearly from -50 (step 1) to -10 (step 8), clamped to 1...8.
    public static func step(forRMS rms: Float) -> Int {
        guard rms > 0, rms.isFinite else { return rms == .infinity ? 8 : 1 }
        let db = 20 * log10(rms)
        let raw = 1 + Int(((db + 50) / 40 * 7).rounded(.down))
        return min(8, max(1, raw))
    }
}
