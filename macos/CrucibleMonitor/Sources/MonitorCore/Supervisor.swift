import AppKit
import SwiftUI
import UserNotifications
import ServiceManagement

@MainActor package final class Supervisor: ObservableObject {
    @Published package var states: [Component: HealthState] = [.frontend: HealthState(), .backend: HealthState()]
    @Published package var owned: [String: OwnedHost] = [:]
    @Published package var busy = false
    @Published package var message: String?
    @Published package var notificationsAllowed = false
    @Published package var launchAtLogin = true
    @Published package var loginStatus = ""
    @Published package var configuration: MonitorConfiguration
    package let store: ConfigurationStore
    private let instanceLock: InstanceLock
    private var loop: Task<Void, Never>?
    private var wakeObserver: NSObjectProtocol?
    private var sleepObserver: NSObjectProtocol?
    private var sleeping = false
    private var sampledEpoch = 0
    private var polled = false
    private var begun = false
    package var confirmationHandler: ((String, String, String) -> Bool)?
    package var shutdownTimeout: TimeInterval = 30

    package init(stateDirectory: URL? = nil, initialConfiguration: MonitorConfiguration? = nil) throws {
        let override = ProcessInfo.processInfo.environment["CRUCIBLE_MONITOR_STATE_DIR"]
        let directory = stateDirectory ?? override.map { URL(fileURLWithPath: $0) } ?? FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0].appendingPathComponent("Crucible Monitor")
        store = try ConfigurationStore(directory: directory)
        instanceLock = try InstanceLock(path: directory.appendingPathComponent("monitor.lock").path)
        let bundledRoot = Bundle.main.url(forResource: "project-root", withExtension: "txt").flatMap { try? String(contentsOf: $0, encoding: .utf8).trimmingCharacters(in: .whitespacesAndNewlines) }
        let root = ProcessInfo.processInfo.environment["CRUCIBLE_MONITOR_PROJECT_ROOT"] ?? bundledRoot ?? FileManager.default.currentDirectoryPath
        let dotnet = ["/opt/homebrew/opt/dotnet/libexec/dotnet", "/opt/homebrew/bin/dotnet", "/usr/local/share/dotnet/dotnet", "/usr/local/bin/dotnet"].first { FileManager.default.isExecutableFile(atPath: $0) } ?? "/opt/homebrew/bin/dotnet"
        configuration = try store.load("configuration.json", as: MonitorConfiguration.self) ?? initialConfiguration ?? MonitorConfiguration(projectRoot: root, dotnet: dotnet)
        if let runtime = Bundle.main.resourceURL?.appendingPathComponent("runtime").path,
           RuntimeLayout.containsHost(runtime, component: .frontend) {
            let seed = Component.allCases.filter { component in
                guard let selected = configuration.runtimes[component.rawValue] else { return true }
                return selected.hasPrefix(Bundle.main.bundlePath + "/") || !RuntimeLayout.containsHost(selected, component: component)
            }
            if !seed.isEmpty, FileManager.default.fileExists(atPath: configuration.projectRoot + "/src/DeckTester/DeckTester.csproj") {
                let builds = URL(fileURLWithPath: configuration.projectRoot).appendingPathComponent("data/monitor/builds")
                try FileManager.default.createDirectory(at: builds, withIntermediateDirectories: true)
                let snapshot = builds.appendingPathComponent("bundled-" + UUID().uuidString.lowercased())
                try FileManager.default.copyItem(atPath: runtime, toPath: snapshot.path)
                for component in seed {
                    let candidate = component == .backend ? snapshot.appendingPathComponent("service").path : snapshot.path
                    configuration.runtimes[component.rawValue] = RuntimeLayout.containsHost(candidate, component: component) ? candidate : snapshot.path
                }
            }
        }
        owned = try store.load("ownership.json", as: [String: OwnedHost].self) ?? [:]
        owned = owned.filter { $0.value.canSignal(root: configuration.projectRoot) }
        launchAtLogin = try store.load("launch-at-login.json", as: Bool.self) ?? true
        try save()
        wakeObserver = NSWorkspace.shared.notificationCenter.addObserver(forName: NSWorkspace.didWakeNotification, object: nil, queue: .main) { [weak self] _ in
            Task { @MainActor in
                guard let self else { return }; self.sleeping = false; self.sampledEpoch += 1
                for c in Component.allCases { self.states[c]?.wake() }
            }
        }
        sleepObserver = NSWorkspace.shared.notificationCenter.addObserver(forName: NSWorkspace.willSleepNotification, object: nil, queue: .main) { [weak self] _ in
            Task { @MainActor in self?.sleeping = true; self?.sampledEpoch += 1 }
        }
    }

    package func begin() {
        guard !begun else { return }; begun = true
        updateLoginItem(enabled: launchAtLogin)
        Task {
            do {
                notificationsAllowed = try await UNUserNotificationCenter.current().requestAuthorization(options: [.alert, .sound])
            } catch { message = "Notifications unavailable. Health warnings remain in this menu." }
        }
        loop = Task {
            await perform { try await self.start(.backend); try await self.start(.frontend) }
            while !Task.isCancelled {
                if !busy && !sleeping { await poll() }
                try? await Task.sleep(nanoseconds: 5_000_000_000)
            }
        }
    }
    package func updateLoginItem(enabled: Bool) {
        do {
            if enabled {
                if SMAppService.mainApp.status != .enabled && SMAppService.mainApp.status != .requiresApproval { try SMAppService.mainApp.register() }
                launchAtLogin = true
                switch SMAppService.mainApp.status {
                case .enabled: loginStatus = "Launch at login is enabled."
                case .requiresApproval: loginStatus = "Allow Crucible Monitor in System Settings → General → Login Items."
                default: loginStatus = "Could not enable launch at login. Open the installed app from Applications and try again."
                }
            } else {
                try SMAppService.mainApp.unregister(); launchAtLogin = false; loginStatus = "Launch at login is disabled."
            }
            try store.save(launchAtLogin, name: "launch-at-login.json")
        } catch { loginStatus = "Could not update launch at login: \(error.localizedDescription)" }
    }
    private func save() throws {
        try store.save(configuration, name: "configuration.json")
        try store.save(owned, name: "ownership.json")
    }
    package func log(_ component: Component) -> URL { store.directory.appendingPathComponent(component.rawValue + ".log") }
    package func openBrowser() {
        NSWorkspace.shared.open(URL(string: "http://127.0.0.1:\(configuration.frontendPort)/")!)
    }
    package func openCrucible() async throws {
        let directories = [Bundle.main.bundleURL.deletingLastPathComponent(),
                           FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent("Applications"),
                           URL(fileURLWithPath: "/Applications")]
        guard let plan = DesktopApplication.launchPlan(configuration: configuration, applicationDirectories: directories) else {
            openBrowser(); return
        }
        try await start(.backend)
        try await start(.frontend)
        // A rolled-back JIT/Blazor host remains available through the browser.
        guard states[.backend]?.health?.runtimeMode == "native-aot",
              states[.frontend]?.health?.runtimeMode == "native-aot" else { openBrowser(); return }
        _ = try await DesktopApplication.open(plan)
    }
    package var buildLog: URL { store.directory.appendingPathComponent("build.log") }
    package var overallHealthy: Bool { Component.allCases.allSatisfy { states[$0]?.status == .healthy } }
    package var overallStopped: Bool { Component.allCases.allSatisfy { states[$0]?.status == .stopped } }
    package var statusLabel: String {
        if busy { return "Crucible · Working" }
        if overallStopped { return "Crucible · Stopped" }
        if overallHealthy { return "Crucible · Healthy" }
        return "Crucible · Needs Attention"
    }
    package func perform(_ operation: @escaping () async throws -> Void) async {
        guard !busy else { return }
        busy = true; message = nil
        sampledEpoch += 1
        defer { busy = false }
        while polled { try? await Task.sleep(nanoseconds: 50_000_000) }
        do { try await operation() } catch { message = error.localizedDescription }
    }
    package func action(_ operation: @escaping () async throws -> Void) { Task { await perform(operation) } }

    private func identity(for health: HostHealth, component: Component) throws -> ProcessIdentity {
        guard health.matches(component, root: configuration.projectRoot),
              let identity = ProcessIdentity.read(health.processId), identity.belongsToProject(configuration.projectRoot) else {
            throw MonitorError.message("Port \(configuration.port(component)) belongs to another process, project, or host mode.")
        }
        return identity
    }
    private func read(_ component: Component) async throws -> HostHealth {
        let health = try await HealthClient.fetch(port: configuration.port(component))
        _ = try identity(for: health, component: component)
        if let host = owned[component.rawValue], host.isAlive,
           (host.identity.pid != health.processId || (host.instanceId != nil && host.instanceId != health.instanceId)) {
            throw MonitorError.message("The listener identity changed. The replacement remains observed until takeover.")
        }
        return health
    }
    private func poll() async {
        guard !polled else { return }; polled = true
        defer { polled = false }
        let epoch = sampledEpoch
        for component in Component.allCases {
            let health: HostHealth?
            var failure: String?
            do { health = try await read(component) } catch { health = nil; failure = error.localizedDescription }
            guard epoch == sampledEpoch, !sleeping else { return }
            let host = owned[component.rawValue]
            if let host, !host.isAlive { ProcessDriver.reap(host.identity.pid); owned.removeValue(forKey: component.rawValue); try? save() }
            let conflict = health == nil && ProcessDriver.portIsOccupied(configuration.port(component)) && host?.identity.isCurrent != true
            if states[component]!.observe(health, error: failure, conflict: conflict) {
                notify(component, detail: conflict ? "Port is occupied by an unverified process. Open the monitor to inspect it." : failure ?? health?.error ?? "Health checks failed. Open the monitor to approve recovery.")
            }
            ProcessDriver.rotate(log(component))
        }
        ProcessDriver.rotate(buildLog)
    }
    private func notify(_ component: Component, detail: String) {
        guard notificationsAllowed else { return }
        let content = UNMutableNotificationContent()
        content.title = "Crucible \(component.rawValue) needs attention"; content.body = detail; content.sound = .default
        UNUserNotificationCenter.current().add(UNNotificationRequest(identifier: "health-" + component.rawValue, content: content, trigger: nil))
    }

    package func start(_ component: Component) async throws {
        try configuration.validate()
        if let health = try? await read(component) {
            states[component]?.observe(health)
            if health.ready { return }
            throw MonitorError.message("\(component.title) is already running but is not ready. Approve a restart to recover it.")
        }
        if let host = owned[component.rawValue], host.isAlive {
            throw MonitorError.message("\(component.title) is still running without a successful health check. Approve a restart instead of launching a duplicate.")
        }
        guard !ProcessDriver.portIsOccupied(configuration.port(component)) else {
            states[component]?.observe(nil, error: "Port is occupied. Existing or older hosts require inspection; they will not be stopped automatically.", conflict: true)
            throw MonitorError.message("Port \(configuration.port(component)) is occupied by an unverified host. No process was stopped.")
        }
        if component == .backend, ProcessDriver.serviceIsLocked(root: configuration.projectRoot) {
            states[component]?.observe(nil, error: "Another backend owns the service lock.", conflict: true)
            throw MonitorError.message("Another backend owns this project's service lock. No replacement was started.")
        }
        if component == .frontend {
            guard let backend = try? await read(.backend), backend.ready else {
                throw MonitorError.message("Start a healthy backend before starting the frontend.")
            }
        }
        guard let runtime = configuration.runtimes[component.rawValue], RuntimeLayout.containsHost(runtime, component: component) else {
            throw MonitorError.message("No published runtime is available. Use Advanced → Build & Restart.")
        }
        let launch = try RuntimeLayout.launch(runtime, component: component, configuration: configuration)
        let process = try ProcessDriver.spawn(executable: launch.executable, arguments: launch.arguments,
                                              environment: configuration.environment(component), directory: runtime, log: log(component))
        owned[component.rawValue] = OwnedHost(identity: process, projectRoot: configuration.projectRoot, instanceId: nil, processGroup: process.pid)
        try save(); states[component]?.starting()
        let deadline = Date().addingTimeInterval(60)
        while Date() < deadline {
            if !process.isCurrent {
                ProcessDriver.reap(process.pid)
                throw MonitorError.message("\(component.title) exited during startup. Open its log for details.")
            }
            if let health = try? await read(component) {
                states[component]?.observe(health)
                if health.ready {
                    owned[component.rawValue] = OwnedHost(identity: process, projectRoot: configuration.projectRoot, instanceId: health.instanceId, processGroup: process.pid)
                    try save(); return
                }
            }
            try await Task.sleep(nanoseconds: 500_000_000)
        }
        throw MonitorError.message("\(component.title) did not become ready within 60 seconds. It remains tracked; inspect its log before recovery.")
    }

    package func confirm(_ title: String, _ detail: String, button: String) -> Bool {
        if let confirmationHandler { return confirmationHandler(title, detail, button) }
        NSApp.activate(ignoringOtherApps: true)
        let alert = NSAlert(); alert.messageText = title; alert.informativeText = detail
        alert.alertStyle = .warning; alert.addButton(withTitle: button); alert.addButton(withTitle: "Cancel")
        return alert.runModal() == .alertFirstButtonReturn
    }
    private func approve(_ components: [Component], verb: String) async -> Bool {
        var detail = "Only processes owned by Crucible Monitor will be stopped."
        if components.contains(.backend), let host = owned[Component.backend.rawValue], host.isAlive {
            let health = try? await read(.backend)
            if ShutdownPolicy.needsWorkConfirmation(health) {
                detail = "The backend has active or queued work, or its work status is unknown. Stopping it may interrupt games or optimization. Completed results are retained; interrupted sessions require explicit resume."
            }
        }
        return confirm("\(verb) \(components.count == 2 ? "Crucible" : components[0].title)?", detail, button: verb)
    }
    package func stop(_ component: Component) async throws {
        guard let host = owned[component.rawValue] else {
            if !ProcessDriver.portIsOccupied(configuration.port(component)) { states[component]?.stopped(); return }
            throw MonitorError.message("This component is observed, not owned. Use Take Ownership before stopping it.")
        }
        if !host.isAlive {
            owned.removeValue(forKey: component.rawValue); try save(); states[component]?.stopped(); return
        }
        // Revalidate both OS identity and application identity when the HTTP endpoint still responds.
        if host.identity.isCurrent, let current = try? await HealthClient.fetch(port: configuration.port(component)),
           (!current.matches(component, root: configuration.projectRoot) || current.processId != host.identity.pid ||
            (host.instanceId != nil && host.instanceId != current.instanceId)) {
            throw MonitorError.message("The listener changed. Refusing to stop it.")
        }
        let workers = (host.workers ?? []) + ProcessDriver.descendants(of: host, root: configuration.projectRoot)
        let tracked = OwnedHost(identity: host.identity, projectRoot: host.projectRoot, instanceId: host.instanceId, processGroup: host.processGroup, workers: workers)
        owned[component.rawValue] = tracked; try save()
        if host.identity.isCurrent { try ProcessDriver.signal(host, root: configuration.projectRoot) }
        let deadline = Date().addingTimeInterval(shutdownTimeout)
        while tracked.isAlive && Date() < deadline { try await Task.sleep(nanoseconds: 200_000_000) }
        if tracked.isAlive {
            guard confirm("Force Stop \(component.title)?", "Graceful shutdown timed out. Force stopping may interrupt work. This is separate from your earlier shutdown approval.", button: "Force Stop") else {
                throw MonitorError.message("Shutdown is still pending. The process remains tracked and no replacement will be started.")
            }
            let remaining = workers + ProcessDriver.descendants(of: host, root: configuration.projectRoot)
            if host.identity.isCurrent { try ProcessDriver.signal(host, root: configuration.projectRoot, force: true) }
            try ProcessDriver.forceStopWorkers(remaining)
            for _ in 0..<50 where tracked.isAlive { try await Task.sleep(nanoseconds: 100_000_000) }
            guard !tracked.isAlive else { throw MonitorError.message("The process or an owned worker has not exited; replacement was cancelled.") }
        }
        ProcessDriver.reap(host.identity.pid)
        owned.removeValue(forKey: component.rawValue); states[component]?.stopped(); try save()
    }
    package func requestStop(_ component: Component) async throws {
        guard await approve([component], verb: "Stop") else { return }
        try await stop(component)
    }
    package func requestRestart(_ component: Component) async throws {
        guard await approve([component], verb: "Restart") else { return }
        try await stop(component); try await start(component)
    }
    package func takeover(_ component: Component) async throws {
        let health = try await HealthClient.fetch(port: configuration.port(component))
        let process = try identity(for: health, component: component)
        guard confirm("Take Ownership of \(component.title)?", "This existing Crucible process will now be stopped when you quit the monitor. Other processes remain untouched.", button: "Take Ownership") else { return }
        // Existing process groups may belong to a terminal. Never signal their groups.
        owned[component.rawValue] = OwnedHost(identity: process, projectRoot: configuration.projectRoot, instanceId: health.instanceId, processGroup: nil)
        try save(); states[component]?.observe(health)
    }
    package func buildAndRestart(_ components: [Component], rollback: Bool = false) async throws {
        for component in components where owned[component.rawValue] == nil && ProcessDriver.portIsOccupied(configuration.port(component)) {
            throw MonitorError.message("Take ownership of the existing \(component.rawValue) before replacing it.")
        }
        if rollback {
            guard components.allSatisfy({ configuration.previousRuntimes[$0.rawValue] != nil }) else {
                throw MonitorError.message("No previous runtime is available for this selection.")
            }
        }
        let runtime = rollback ? nil : try await RuntimeBuilder.publish(configuration: configuration, components: components, log: buildLog)
        guard await approve(components, verb: rollback ? "Roll Back" : "Restart After Build") else { return }
        for component in [Component.frontend, .backend] where components.contains(component) { try await stop(component) }
        for component in components {
            let previous = configuration.runtimes[component.rawValue]
            configuration.runtimes[component.rawValue] = rollback ? configuration.previousRuntimes[component.rawValue] : runtime.map { component == .backend ? $0 + "/service" : $0 }
            configuration.previousRuntimes[component.rawValue] = previous
        }
        try save()
        for component in [Component.backend, .frontend] where components.contains(component) { try await start(component) }
    }
    package func quit() async throws -> Bool {
        let components = Component.allCases.filter { owned[$0.rawValue]?.isAlive == true }
        if components.contains(.backend) {
            let health = try? await read(.backend)
            if ShutdownPolicy.needsWorkConfirmation(health), !(await approve(components, verb: "Quit And Stop")) { return false }
        }
        for component in [Component.frontend, .backend] where components.contains(component) { try await stop(component) }
        loop?.cancel(); return true
    }
    package func configure(projectRoot: String, dotnet: String, frontend: String, backend: String, lan: String) throws {
        guard owned.values.allSatisfy({ !$0.isAlive }) else {
            throw MonitorError.message("Stop owned components before changing connection settings.")
        }
        var updated = configuration
        updated.projectRoot = URL(fileURLWithPath: projectRoot).standardizedFileURL.path
        updated.dotnet = dotnet; updated.frontendPort = Int(frontend) ?? 0; updated.backendPort = Int(backend) ?? 0
        updated.lanAddress = lan.isEmpty ? nil : lan
        try updated.validate()
        if updated.projectRoot != configuration.projectRoot { updated.runtimes = [:]; updated.previousRuntimes = [:] }
        sampledEpoch += 1
        configuration = updated; states = [.frontend: HealthState(), .backend: HealthState()]; try save()
    }
}
