// swift-tools-version: 6.0
import Foundation
import PackageDescription

// CommandLineTools-only toolchains keep the Swift Testing macro plugin in a
// non-default directory; point the compiler at it so plain `swift test` works.
// Skipped when the directory is absent or full Xcode is installed (Xcode finds it itself).
let cltTestingPlugins = "/Library/Developer/CommandLineTools/usr/lib/swift/host/plugins/testing"
let needsTestingPluginPath = FileManager.default.fileExists(atPath: cltTestingPlugins)
    && !FileManager.default.fileExists(atPath: "/Applications/Xcode.app")

let package = Package(
    name: "Silk",
    platforms: [.macOS(.v14)],
    products: [
        .library(name: "SilkCore", targets: ["SilkCore"]),
        .executable(name: "Silk", targets: ["Silk"]),
    ],
    dependencies: [
        .package(url: "https://github.com/FluidInference/FluidAudio.git", from: "0.12.4"),
    ],
    targets: [
        .target(
            name: "SilkCore",
            dependencies: [.product(name: "FluidAudio", package: "FluidAudio")]
        ),
        .executableTarget(name: "Silk", dependencies: ["SilkCore"]),
        .testTarget(
            name: "SilkCoreTests",
            dependencies: ["SilkCore"],
            swiftSettings: needsTestingPluginPath
                ? [.unsafeFlags(["-plugin-path", cltTestingPlugins])] : []
        ),
    ]
)
