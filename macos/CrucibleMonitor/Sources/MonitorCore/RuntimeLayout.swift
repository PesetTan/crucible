import Foundation

public struct HostLaunch: Equatable {
    public let executable: String
    public let arguments: [String]
}

public enum RuntimeLayout {
    public static func containsHost(_ runtime: String, component: Component) -> Bool {
        let files = FileManager.default
        if component == .frontend && files.isExecutableFile(atPath: runtime + "/CrucibleUi") { return true }
        if component == .frontend && files.fileExists(atPath: runtime + "/CrucibleUi.dll") { return true }
        if component == .backend && files.isExecutableFile(atPath: runtime + "/CrucibleBackend") { return true }
        if component == .backend && files.fileExists(atPath: runtime + "/CrucibleBackend.dll") { return true }
        return files.fileExists(atPath: runtime + "/DeckTester.dll")
    }
    public static func launch(_ runtime: String, component: Component, configuration: MonitorConfiguration) throws -> HostLaunch {
        guard containsHost(runtime, component: component) else { throw MonitorError.message("No published \(component.title) runtime is available. Use Advanced → Build & Restart.") }
        if component == .backend && FileManager.default.isExecutableFile(atPath: runtime + "/CrucibleBackend") {
            return HostLaunch(executable: runtime + "/CrucibleBackend", arguments: [])
        }
        if component == .frontend && FileManager.default.isExecutableFile(atPath: runtime + "/CrucibleUi") {
            return HostLaunch(executable: runtime + "/CrucibleUi", arguments: [])
        }
        if component == .frontend && FileManager.default.fileExists(atPath: runtime + "/CrucibleUi.dll") {
            return HostLaunch(executable: try configuration.launchExecutable(), arguments: [runtime + "/CrucibleUi.dll"])
        }
        let assembly = component == .backend && FileManager.default.fileExists(atPath: runtime + "/CrucibleBackend.dll") ? "CrucibleBackend.dll" : "DeckTester.dll"
        return HostLaunch(executable: try configuration.launchExecutable(), arguments: [runtime + "/" + assembly])
    }
}
