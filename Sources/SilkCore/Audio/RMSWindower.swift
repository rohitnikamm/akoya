import Foundation

/// Groups a stream of mono samples into fixed windows and reports one RMS value per
/// completed window. Leftover samples carry over to the next `append`.
public struct RMSWindower {
    private let windowSize: Int
    private var pending: [Float] = []

    public init(sampleRate: Double = 16_000, window: TimeInterval = 0.04) {
        windowSize = max(1, Int((sampleRate * window).rounded()))
        pending.reserveCapacity(windowSize * 2)
    }

    public mutating func append(_ samples: [Float]) -> [Float] {
        pending.append(contentsOf: samples)
        var results: [Float] = []
        var start = 0
        while pending.count - start >= windowSize {
            var sum: Float = 0
            for i in start..<(start + windowSize) {
                sum += pending[i] * pending[i]
            }
            results.append((sum / Float(windowSize)).squareRoot())
            start += windowSize
        }
        if start > 0 { pending.removeFirst(start) }
        return results
    }
}
