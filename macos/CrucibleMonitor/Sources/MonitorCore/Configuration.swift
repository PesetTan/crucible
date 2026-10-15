import Foundation

public struct MonitorConfiguration: Codable {
    public var projectRoot: String
    public var dotnet: String
    public var frontendPort = 5077
    public var backendPort = 5079
    public var lanAddress: String?
    public var runtimes: [String: String] = [:]
    public var previousRuntimes: [String: String] = [:]
    public init(projectRoot: String, dotnet: String) { self.projectRoot = projectRoot; self.dotnet = dotnet }
    public func port(_ component: Component) -> Int { component == .frontend ? frontendPort : backendPort }
    public func environment(_ component: Component) -> [String: String] {
        var env = ProcessInfo.processInfo.environment
        // A Finder launch has a minimal PATH. Explicit dotnet plus a predictable tool path also supports optimizer Python.
        env["PATH"] = [URL(fileURLWithPath: dotnet).deletingLastPathComponent().path,
                       "/opt/homebrew/bin", "/usr/local/bin", "/usr/bin", "/bin", env["PATH"] ?? ""].joined(separator: ":")
        env["MTG_HOST_MODE"] = component.role; env["MTG_PORT"] = String(port(component))
        env["MTG_PROJECT_ROOT"] = projectRoot
        env["MTG_UI_STATE_ROOT"] = projectRoot + "/src/DeckTester/ui-state"
        env["MTG_BACKEND_URL"] = "http://127.0.0.1:\(backendPort)/"
        env["ASPNETCORE_ENVIRONMENT"] = "Production"
        env.removeValue(forKey: "MTG_LAN_ADDRESS")
        if component == .frontend, let lanAddress, !lanAddress.isEmpty { env["MTG_LAN_ADDRESS"] = lanAddress }
        return env
    }
    public func validate() throws {
        guard FileManager.default.fileExists(atPath: projectRoot + "/src/DeckTester/DeckTester.csproj"),
              FileManager.default.isExecutableFile(atPath: dotnet),
              (1024...65535).contains(frontendPort), (1024...65535).contains(backendPort), frontendPort != backendPort else {
            throw MonitorError.message("Choose the Crucible project folder, a working .NET 10 executable, and two distinct ports above 1023.")
        }
    }
    public func launchExecutable() throws -> String {
        let supplied = URL(fileURLWithPath: dotnet).resolvingSymlinksInPath()
        // Homebrew's bin/dotnet is a shell wrapper that execs libexec/dotnet. Starting
        // the actual binary avoids mistaking that exec transition for PID replacement.
        let homebrew = supplied.deletingLastPathComponent().deletingLastPathComponent().appendingPathComponent("libexec/dotnet")
        for candidate in [supplied, homebrew] {
            guard FileManager.default.isExecutableFile(atPath: candidate.path), let handle = try? FileHandle(forReadingFrom: candidate) else { continue }
            defer { try? handle.close() }
            let prefix = try handle.read(upToCount: 2)
            if prefix != Data([35, 33]) { return candidate.path }
        }
        throw MonitorError.message("Choose the native .NET executable in Connection Settings. The selected shell wrapper has no recognized native runtime.")
    }
}

public struct ConfigurationStore {
    public let directory: URL
    public init(directory: URL) throws {
        self.directory = directory
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
    }
    public func load<T: Decodable>(_ name: String, as type: T.Type) throws -> T? {
        let file = directory.appendingPathComponent(name)
        guard FileManager.default.fileExists(atPath: file.path) else { return nil }
        return try JSONDecoder().decode(type, from: Data(contentsOf: file))
    }
    public func save<T: Encodable>(_ value: T, name: String) throws {
        try JSONEncoder().encode(value).write(to: directory.appendingPathComponent(name), options: .atomic)
    }
}
