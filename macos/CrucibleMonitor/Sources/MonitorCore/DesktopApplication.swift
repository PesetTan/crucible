import Foundation
import AppKit

public struct DesktopLaunchPlan {
    public let application: URL
    public let arguments: [String]
    public let environment: [String: String]
}

public enum DesktopApplication {
    @MainActor public static func open(_ plan: DesktopLaunchPlan) async throws -> Int32 {
        let options = NSWorkspace.OpenConfiguration()
        options.arguments = plan.arguments
        options.environment = plan.environment
        return try await NSWorkspace.shared.openApplication(at: plan.application, configuration: options).processIdentifier
    }
    public static func launchPlan(configuration: MonitorConfiguration, applicationDirectories: [URL]) -> DesktopLaunchPlan? {
        for directory in applicationDirectories {
            let application = directory.appendingPathComponent("Crucible.app")
            guard Bundle(url: application)?.bundleIdentifier == "local.crucible.desktop",
                  FileManager.default.isExecutableFile(atPath: application.appendingPathComponent("Contents/MacOS/Crucible").path) else { continue }
            var environment = configuration.environment(.frontend)
            environment["CRUCIBLE_UI_PORT"] = String(configuration.frontendPort)
            environment["CRUCIBLE_BACKEND_PORT"] = String(configuration.backendPort)
            // The Monitor starts and owns hosts. Its window must only adopt those hosts.
            return DesktopLaunchPlan(application: application, arguments: ["--existing-hosts-only"], environment: environment)
        }
        return nil
    }
}
