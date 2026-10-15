import Foundation

public enum Component: String, Codable, CaseIterable {
    case frontend, backend
    public var title: String { rawValue.capitalized }
    public var role: String { self == .frontend ? "ui" : "service" }
}

public struct HostHealth: Codable, Equatable {
    public let version: Int
    public let application: String
    public let role: String
    public let processId: Int32
    public let instanceId: String
    public let startedAt: String
    public let projectRoot: String
    public let ready: Bool
    public let database: String
    public let activeRuns: Int
    public let queuedRuns: Int
    public let activeOptimizations: Int
    public let queuedOptimizations: Int
    public let error: String?
    package init(version: Int, application: String, role: String, processId: Int32, instanceId: String,
                 startedAt: String, projectRoot: String, ready: Bool, database: String, activeRuns: Int,
                 queuedRuns: Int, activeOptimizations: Int, queuedOptimizations: Int, error: String?) {
        self.version = version; self.application = application; self.role = role; self.processId = processId
        self.instanceId = instanceId; self.startedAt = startedAt; self.projectRoot = projectRoot; self.ready = ready
        self.database = database; self.activeRuns = activeRuns; self.queuedRuns = queuedRuns
        self.activeOptimizations = activeOptimizations; self.queuedOptimizations = queuedOptimizations; self.error = error
    }
    public var workCount: Int { activeRuns + queuedRuns + activeOptimizations + queuedOptimizations }
    public func matches(_ component: Component, root: String) -> Bool {
        version == 1 && application == "crucible" && role == component.role &&
        URL(fileURLWithPath: projectRoot).standardizedFileURL.path == URL(fileURLWithPath: root).standardizedFileURL.path &&
        processId > 0 && !instanceId.isEmpty
    }
}

public enum HostStatus: String { case stopped, starting, healthy, unresponsive, conflict }

public struct HealthState {
    public private(set) var status: HostStatus = .stopped
    public private(set) var health: HostHealth?
    public private(set) var lastSuccess: Date?
    public private(set) var error: String?
    public private(set) var failures = 0
    public private(set) var intentionalStop = false
    private var startupDeadline: Date?
    private var notified = false
    public init() {}
    public mutating func starting(now: Date = Date()) {
        intentionalStop = false; failures = 0; notified = false
        status = .starting; startupDeadline = now.addingTimeInterval(60); error = nil
    }
    public mutating func stopped() {
        intentionalStop = true; status = .stopped; health = nil; error = nil
        failures = 0; notified = false; startupDeadline = nil
    }
    public mutating func wake(now: Date = Date()) {
        failures = 0; notified = false
        if !intentionalStop { status = .starting; startupDeadline = now.addingTimeInterval(15) }
    }
    /// Returns true once per failure episode. Intentional stops never produce recovery prompts.
    @discardableResult public mutating func observe(_ value: HostHealth?, error message: String? = nil,
                                                    conflict: Bool = false, now: Date = Date()) -> Bool {
        if let value, value.ready, !conflict {
            health = value; status = .healthy; lastSuccess = now; failures = 0
            startupDeadline = nil; notified = false; error = nil; intentionalStop = false
            return false
        }
        health = value; error = message ?? value?.error ?? "Health check failed."
        if intentionalStop && !conflict { status = .stopped; return false }
        if conflict { status = .conflict }
        else {
            failures += 1
            if let deadline = startupDeadline, now < deadline { status = .starting; return false }
            if failures < 3 { return false }
            status = .unresponsive
        }
        if notified { return false }
        notified = true; return true
    }
}

public enum HealthClient {
    public static func fetch(port: Int) async throws -> HostHealth {
        var request = URLRequest(url: URL(string: "http://127.0.0.1:\(port)/api/v1/health")!,
                                 cachePolicy: .reloadIgnoringLocalCacheData, timeoutInterval: 2)
        request.setValue("no-cache", forHTTPHeaderField: "Cache-Control")
        let (data, response) = try await URLSession.shared.data(for: request)
        guard let http = response as? HTTPURLResponse, [200, 503].contains(http.statusCode), data.count < 16_384 else {
            throw MonitorError.message("The port does not expose a compatible Crucible health endpoint.")
        }
        return try JSONDecoder().decode(HostHealth.self, from: data)
    }
}

public enum MonitorError: LocalizedError {
    case message(String)
    public var errorDescription: String? { if case let .message(message) = self { return message }; return nil }
}

public enum ShutdownPolicy {
    public static func needsWorkConfirmation(_ health: HostHealth?) -> Bool { health == nil || health!.workCount > 0 || !health!.ready }
}
