// Assembles PNG frames into a looping animated GIF using ImageIO.
// Usage: swift scripts/make-gif.swift <frames-dir> <out.gif> [delay-seconds]
// Frames are taken in filename order (f000.png, f001.png, ...).
import Foundation
import ImageIO
import UniformTypeIdentifiers

let args = CommandLine.arguments
guard args.count >= 3 else {
    print("usage: make-gif.swift <frames-dir> <out.gif> [delay-seconds]")
    exit(1)
}
let dir = URL(fileURLWithPath: args[1])
let out = URL(fileURLWithPath: args[2])
let delay = args.count > 3 ? Double(args[3]) ?? 0.08 : 0.08

let frames = try FileManager.default.contentsOfDirectory(at: dir, includingPropertiesForKeys: nil)
    .filter { $0.pathExtension == "png" }
    .sorted { $0.lastPathComponent < $1.lastPathComponent }
guard !frames.isEmpty,
      let dest = CGImageDestinationCreateWithURL(out as CFURL, UTType.gif.identifier as CFString, frames.count, nil)
else {
    print("no frames, or cannot create \(out.path)")
    exit(1)
}
CGImageDestinationSetProperties(dest, [kCGImagePropertyGIFDictionary: [kCGImagePropertyGIFLoopCount: 0]] as CFDictionary)
let frameProps = [kCGImagePropertyGIFDictionary: [kCGImagePropertyGIFDelayTime: delay]] as CFDictionary
for url in frames {
    guard let src = CGImageSourceCreateWithURL(url as CFURL, nil),
          let image = CGImageSourceCreateImageAtIndex(src, 0, nil) else { continue }
    CGImageDestinationAddImage(dest, image, frameProps)
}
guard CGImageDestinationFinalize(dest) else {
    print("failed to write \(out.path)")
    exit(1)
}
print("wrote \(frames.count) frames to \(out.path)")
