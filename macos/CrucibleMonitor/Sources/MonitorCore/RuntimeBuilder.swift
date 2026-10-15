import Foundation

public enum RuntimeBuilder {
    public static func publish(configuration: MonitorConfiguration, components: [Component] = Component.allCases, log: URL) async throws -> String {
        try configuration.validate()
        let output = URL(fileURLWithPath: configuration.projectRoot)
            .appendingPathComponent("data/monitor/builds/" + UUID().uuidString.lowercased())
        try FileManager.default.createDirectory(at: output, withIntermediateDirectories: true)
        ProcessDriver.rotate(log)
        if !FileManager.default.fileExists(atPath: log.path) { FileManager.default.createFile(atPath: log.path, contents: nil) }
        let handle = try FileHandle(forWritingTo: log)
        try handle.seekToEnd()
        defer { try? handle.close() }
        for component in [Component.frontend, .backend] where components.contains(component) {
        let process = Process()
        process.executableURL = URL(fileURLWithPath: configuration.dotnet)
        process.currentDirectoryURL = URL(fileURLWithPath: configuration.projectRoot)
        process.environment = configuration.environment(.backend)
        let native = component == .backend
        let destination = native ? output.appendingPathComponent("service") : output
        process.arguments = ["publish", configuration.projectRoot + (native ? "/src/Crucible.Service/Crucible.Service.csproj" : "/src/DeckTester/DeckTester.csproj"),
                             "--configuration", "Release", "--output", destination.path, "--nologo", "--disable-build-servers"]
        if native {
            #if arch(arm64)
            process.arguments! += ["--runtime", "osx-arm64"]
            #else
            process.arguments! += ["--runtime", "osx-x64"]
            #endif
        } else { process.arguments! += ["-p:UseAppHost=false"] }
        process.standardOutput = handle; process.standardError = handle; process.standardInput = FileHandle.nullDevice
        let result: Int32 = try await withCheckedThrowingContinuation { continuation in
            process.terminationHandler = { continuation.resume(returning: $0.terminationStatus) }
            do { try process.run() } catch { continuation.resume(throwing: error) }
        }
        guard result == 0, RuntimeLayout.containsHost(destination.path, component: component) else {
            throw MonitorError.message("Build failed. Running components were left untouched. Open build.log for details.")
        }
        if native {
            let check = Process()
            check.executableURL = destination.appendingPathComponent("CrucibleBackend")
            check.arguments = ["--check-runtime"]
            check.environment = configuration.environment(.backend)
            check.standardInput = FileHandle.nullDevice; check.standardOutput = handle; check.standardError = handle
            let status: Int32 = try await withCheckedThrowingContinuation { continuation in
                check.terminationHandler = { continuation.resume(returning: $0.terminationStatus) }
                do { try check.run() } catch { continuation.resume(throwing: error) }
            }
            guard status == 0 else { throw MonitorError.message("Native runtime self-check failed. Running components were left untouched. Open build.log for details.") }
        }
        }
        return output.path
    }
}
