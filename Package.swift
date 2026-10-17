// swift-tools-version: 6.0
import Foundation
import PackageDescription

// The Swift Testing macro plugin is not on the compiler's default search path when the
// active developer dir is CommandLineTools, so `swift test` fails without help. Add the
// plugin path only when CommandLineTools is the active developer dir (DEVELOPER_DIR, else
// the xcode-select symlink) and the plugin directory exists. Xcode toolchains need nothing.
let cltRoot = "/Library/Developer/CommandLineTools"
let cltTestingPlugins = cltRoot + "/usr/lib/swift/host/plugins/testing"
let activeDeveloperDir: String? = ProcessInfo.processInfo.environment["DEVELOPER_DIR"]
    ?? (try? FileManager.default.destinationOfSymbolicLink(atPath: "/var/db/xcode_select_link"))
let needsTestingPluginPath = activeDeveloperDir?.trimmingCharacters(in: CharacterSet(charactersIn: "/")) == cltRoot.trimmingCharacters(in: CharacterSet(charactersIn: "/"))
    && FileManager.default.fileExists(atPath: cltTestingPlugins)

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
