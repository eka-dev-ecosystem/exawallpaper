// swift-tools-version: 5.9
import PackageDescription

let package = Package(
    name: "ExaWallpaper",
    platforms: [
        .macOS(.v13)
    ],
    products: [
        .executable(name: "ExaWallpaper", targets: ["ExaWallpaper"])
    ],
    dependencies: [],
    targets: [
        .executableTarget(
            name: "ExaWallpaper",
            dependencies: [],
            path: "Sources"
        )
    ]
)
