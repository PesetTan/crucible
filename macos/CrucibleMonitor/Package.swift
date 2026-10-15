// swift-tools-version: 6.0
import PackageDescription

let package = Package(
    name: "CrucibleMonitor",
    platforms: [.macOS(.v13)],
    products: [.executable(name: "CrucibleMonitor", targets: ["CrucibleMonitor"])],
    targets: [
        .target(name: "MonitorCore"),
        .executableTarget(name: "CrucibleMonitor", dependencies: ["MonitorCore"]),
        .executableTarget(name: "MonitorChecks", dependencies: ["MonitorCore"], path: "Tests/MonitorChecks")
    ],
    swiftLanguageModes: [.v5]
)
