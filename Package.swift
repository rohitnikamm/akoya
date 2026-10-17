// swift-tools-version: 6.0
import PackageDescription

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
        // CommandLineTools-only toolchains keep the Swift Testing macro plugin in a
        // non-default directory; point the compiler at it so plain `swift test` works.
        .testTarget(
            name: "SilkCoreTests",
            dependencies: ["SilkCore"],
            swiftSettings: [.unsafeFlags([
                "-plugin-path",
                "/Library/Developer/CommandLineTools/usr/lib/swift/host/plugins/testing",
            ])]
        ),
    ]
)
