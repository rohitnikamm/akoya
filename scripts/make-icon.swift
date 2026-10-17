// Renders the Akoya app icon and builds Resources/AppIcon.icns.
// Usage: swift scripts/make-icon.swift   (Command Line Tools only: AppKit + CoreGraphics + iconutil)
//
// Design (spec: "Calls made after review"): a dark continuous-corner square (the `surface`
// colour with the blue V1 top tint and a faint blue bottom glow) carrying the 5-column
// dot-matrix mark (2/4/6/4/2 dots) in light blue with a soft glow.
import AppKit
import CoreGraphics
import Foundation

let root = URL(fileURLWithPath: CommandLine.arguments[0]).deletingLastPathComponent().deletingLastPathComponent()
let iconset = root.appendingPathComponent("build/AppIcon.iconset")
let icns = root.appendingPathComponent("Resources/AppIcon.icns")

func rgb(_ hex: UInt32, _ alpha: CGFloat = 1) -> CGColor {
    CGColor(
        srgbRed: CGFloat((hex >> 16) & 0xFF) / 255, green: CGFloat((hex >> 8) & 0xFF) / 255,
        blue: CGFloat(hex & 0xFF) / 255, alpha: alpha)
}

func gradient(_ stops: [(CGFloat, CGColor)]) -> CGGradient {
    CGGradient(
        colorsSpace: CGColorSpace(name: CGColorSpace.sRGB), colors: stops.map(\.1) as CFArray,
        locations: stops.map(\.0))!
}

/// Superellipse |x|^n + |y|^n = r^n (n = 5 approximates Apple's continuous-corner squircle).
func squirclePath(in rect: CGRect, exponent n: CGFloat = 5) -> CGPath {
    let path = CGMutablePath()
    let steps = 360
    let cx = rect.midX, cy = rect.midY, a = rect.width / 2, b = rect.height / 2
    for i in 0...steps {
        let t = CGFloat(i) / CGFloat(steps) * 2 * .pi
        let c = cos(t), s = sin(t)
        let x = cx + a * (c < 0 ? -1 : 1) * pow(abs(c), 2 / n)
        let y = cy + b * (s < 0 ? -1 : 1) * pow(abs(s), 2 / n)
        i == 0 ? path.move(to: CGPoint(x: x, y: y)) : path.addLine(to: CGPoint(x: x, y: y))
    }
    path.closeSubpath()
    return path
}

/// Draws the icon into a square canvas of `size` pixels (y-up, like AppKit bitmaps).
func drawIcon(_ ctx: CGContext, size: CGFloat) {
    let k = size / 1024
    // macOS icon grid: the shape fills 824 of 1024 px.
    let shape = CGRect(x: 100 * k, y: 100 * k, width: 824 * k, height: 824 * k)
    let path = squirclePath(in: shape)

    // Soft drop shadow under the tile.
    ctx.saveGState()
    ctx.setShadow(offset: CGSize(width: 0, height: -10 * k), blur: 24 * k, color: rgb(0x000000, 0.45))
    ctx.addPath(path)
    ctx.setFillColor(rgb(0x03060F))
    ctx.fillPath()
    ctx.restoreGState()

    ctx.saveGState()
    ctx.addPath(path)
    ctx.clip()
    ctx.setFillColor(rgb(0x03060F))
    ctx.fill(shape)

    // V1 top tint: blue at the top centre, gone by ~60% of the height.
    ctx.drawRadialGradient(
        gradient([(0, rgb(0x285AC8, 0.30)), (1, rgb(0x285AC8, 0))]),
        startCenter: CGPoint(x: shape.midX, y: shape.maxY), startRadius: 0,
        endCenter: CGPoint(x: shape.midX, y: shape.maxY), endRadius: shape.height * 0.62,
        options: [])

    // Faint bottom glow echoing the rim glow: light along the lower edge, rising ~20%.
    ctx.drawLinearGradient(
        gradient([
            (0, rgb(0xC8E4FF, 0.30)), (0.25, rgb(0x5AA0FF, 0.22)),
            (0.6, rgb(0x1E64FF, 0.10)), (1, rgb(0x1446DC, 0)),
        ]),
        start: CGPoint(x: 0, y: shape.minY), end: CGPoint(x: 0, y: shape.minY + shape.height * 0.28),
        options: [])

    // Hairline inner rim.
    ctx.addPath(path)
    ctx.setStrokeColor(rgb(0x8CB4FF, 0.18))
    ctx.setLineWidth(3 * k)
    ctx.strokePath()
    ctx.restoreGState()

    // Dot logo: columns 2/4/6/4/2, dot `d`, column gap 7/6 d, row gap 4/6 d (matches DotLogo).
    let columns = [2, 4, 6, 4, 2]
    let d = 50 * k
    let colGap = d * 7 / 6, rowGap = d * 4 / 6
    let totalWidth = CGFloat(columns.count) * d + CGFloat(columns.count - 1) * colGap
    let originX = shape.midX - totalWidth / 2
    // Optical centre sits slightly above the geometric centre to leave room for the bottom glow.
    let centreY = shape.midY + 8 * k

    func drawDots(color: CGColor) {
        for (index, count) in columns.enumerated() {
            let x = originX + CGFloat(index) * (d + colGap)
            let height = CGFloat(count) * d + CGFloat(count - 1) * rowGap
            for row in 0..<count {
                let y = centreY - height / 2 + CGFloat(row) * (d + rowGap)
                ctx.setFillColor(color)
                ctx.fillEllipse(in: CGRect(x: x, y: y, width: d, height: d))
            }
        }
    }
    // Wide soft halo, then a tight glow, then the dots themselves.
    ctx.saveGState()
    ctx.setShadow(offset: .zero, blur: 46 * k, color: rgb(0x60A5FA, 0.85))
    drawDots(color: rgb(0x93C5FD))
    ctx.restoreGState()
    ctx.saveGState()
    ctx.setShadow(offset: .zero, blur: 14 * k, color: rgb(0x93C5FD, 0.9))
    drawDots(color: rgb(0xBFDBFE))
    ctx.restoreGState()
}

func renderPNG(pixels: Int) -> Data {
    let rep = NSBitmapImageRep(
        bitmapDataPlanes: nil, pixelsWide: pixels, pixelsHigh: pixels, bitsPerSample: 8,
        samplesPerPixel: 4, hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB,
        bytesPerRow: 0, bitsPerPixel: 0)!
    let context = NSGraphicsContext(bitmapImageRep: rep)!
    NSGraphicsContext.saveGraphicsState()
    NSGraphicsContext.current = context
    context.cgContext.setAllowsAntialiasing(true)
    drawIcon(context.cgContext, size: CGFloat(pixels))
    NSGraphicsContext.restoreGraphicsState()
    return rep.representation(using: .png, properties: [:])!
}

let fm = FileManager.default
try? fm.removeItem(at: iconset)
try fm.createDirectory(at: iconset, withIntermediateDirectories: true)
for base in [16, 32, 128, 256, 512] {
    try renderPNG(pixels: base).write(to: iconset.appendingPathComponent("icon_\(base)x\(base).png"))
    try renderPNG(pixels: base * 2).write(to: iconset.appendingPathComponent("icon_\(base)x\(base)@2x.png"))
}

try fm.createDirectory(at: icns.deletingLastPathComponent(), withIntermediateDirectories: true)
let tool = Process()
tool.executableURL = URL(fileURLWithPath: "/usr/bin/iconutil")
tool.arguments = ["-c", "icns", iconset.path, "-o", icns.path]
try tool.run()
tool.waitUntilExit()
guard tool.terminationStatus == 0 else {
    FileHandle.standardError.write(Data("iconutil failed\n".utf8))
    exit(1)
}
print("Wrote \(icns.path)")
