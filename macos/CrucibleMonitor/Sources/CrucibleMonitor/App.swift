import AppKit
import SwiftUI
import MonitorCore
import ServiceManagement

@MainActor final class AppDelegate: NSObject, NSApplicationDelegate {
    var supervisor: Supervisor?
    var approvedTermination = false
    func applicationShouldTerminate(_ sender: NSApplication) -> NSApplication.TerminateReply {
        guard !approvedTermination, let supervisor else { return .terminateNow }
        guard !supervisor.busy else {
            supervisor.message = "Wait for the current operation before quitting."
            return .terminateCancel
        }
        Task {
            var finished = false
            await supervisor.perform { finished = try await supervisor.quit() }
            approvedTermination = finished
            sender.reply(toApplicationShouldTerminate: finished)
        }
        return .terminateLater
    }
    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { false }
}

@main struct CrucibleMonitorApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) private var delegate
    @StateObject private var supervisor: Supervisor
    init() {
        if ProcessInfo.processInfo.arguments.contains("--diagnostics") {
            let status: String
            switch SMAppService.mainApp.status {
            case .enabled: status = "enabled"
            case .requiresApproval: status = "requires-approval"
            case .notRegistered: status = "not-registered"
            case .notFound: status = "not-found"
            @unknown default: status = "unknown"
            }
            let data = try! JSONSerialization.data(withJSONObject: ["launchAtLogin": status, "bundle": Bundle.main.bundlePath], options: [.sortedKeys])
            print(String(decoding: data, as: UTF8.self)); exit(0)
        }
        do {
            let supervisor = try Supervisor()
            _supervisor = StateObject(wrappedValue: supervisor)
        }
        catch {
            // A duplicate launch exits quietly; corrupt configuration remains visible instead of being overwritten.
            if error.localizedDescription.contains("already running") { exit(0) }
            let alert = NSAlert(); alert.messageText = "Crucible Monitor Cannot Start"
            alert.informativeText = error.localizedDescription; alert.runModal(); exit(1)
        }
    }
    var body: some Scene {
        MenuBarExtra {
            MonitorPanel(supervisor: supervisor)
                .onAppear { delegate.supervisor = supervisor }
        } label: {
            Label {
                Text(supervisor.statusLabel)
            } icon: {
                Image(nsImage: MenuBarIcon.image(
                    healthy: supervisor.overallHealthy,
                    stopped: supervisor.overallStopped,
                    brandingPath: supervisor.configuration.runtimes[Component.frontend.rawValue].map { $0 + "/branding.json" }
                ))
                    .renderingMode(.original)
            }
                .task { delegate.supervisor = supervisor; supervisor.begin() }
        }
        .menuBarExtraStyle(.window)
        Settings { SettingsPanel(supervisor: supervisor) }
    }
}

private struct MonitorPanel: View {
    @ObservedObject var supervisor: Supervisor
    private var controlsDisabled: Bool { supervisor.busy }
    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            HStack {
                Image(nsImage: MenuBarIcon.image(
                    healthy: supervisor.overallHealthy,
                    stopped: supervisor.overallStopped,
                    brandingPath: supervisor.configuration.runtimes[Component.frontend.rawValue].map { $0 + "/branding.json" }
                )).renderingMode(.original)
                Text("Crucible Monitor").font(.headline)
                Spacer()
                if supervisor.busy { ProgressView().controlSize(.small).accessibilityLabel("Operation in progress") }
            }
            ForEach([Component.frontend, .backend], id: \.rawValue) { component in componentRow(component) }
            if let message = supervisor.message {
                Text(message).font(.callout).foregroundStyle(.orange).fixedSize(horizontal: false, vertical: true)
                    .accessibilityIdentifier("monitor-message")
            }
            if !supervisor.notificationsAllowed {
                Text("Notifications are off. Health warnings appear here.").font(.caption).foregroundStyle(.secondary)
            }
            if SMAppService.mainApp.status == .requiresApproval {
                Button("Allow Launch at Login…") { SMAppService.openSystemSettingsLoginItems() }
            } else if supervisor.loginStatus.hasPrefix("Could not") {
                Text(supervisor.loginStatus).font(.caption).foregroundStyle(.orange)
            }
            Divider()
            HStack {
                Button("Open Crucible") {
                    supervisor.action { try await supervisor.openCrucible() }
                }.keyboardShortcut("o").disabled(controlsDisabled).accessibilityIdentifier("open-crucible")
                Spacer()
                Button("View Logs") { NSWorkspace.shared.open(supervisor.store.directory) }
            }
            HStack {
                Menu("Advanced") {
                    Button("Open in Browser") { supervisor.openBrowser() }
                    Divider()
                    Menu("Build & Restart") { buildItems(rollback: false) }
                    Menu("Roll Back") { buildItems(rollback: true) }
                    Divider()
                    if #available(macOS 14, *) {
                        SettingsLink { Text("Connection Settings…") }
                    } else {
                        Button("Connection Settings…") { NSApp.sendAction(Selector(("showSettingsWindow:")), to: nil, from: nil) }
                    }
                }.disabled(controlsDisabled)
                Spacer()
                Button("Quit") { NSApp.terminate(nil) }.keyboardShortcut("q").disabled(controlsDisabled)
                    .accessibilityIdentifier("quit-monitor")
            }
        }.padding(16).frame(width: 370)
    }
    @ViewBuilder private func componentRow(_ component: Component) -> some View {
        let state = supervisor.states[component] ?? HealthState()
        let owned = supervisor.owned[component.rawValue] != nil
        VStack(alignment: .leading, spacing: 5) {
            HStack {
                Image(systemName: state.status == .healthy ? "checkmark.circle.fill" : state.status == .stopped ? "stop.circle" : "exclamationmark.circle.fill")
                    .foregroundStyle(state.status == .healthy ? Color.green : state.status == .stopped ? Color.secondary : Color.orange)
                Text(component.title).fontWeight(.semibold)
                Spacer()
                Text(state.status.rawValue.capitalized).foregroundStyle(.secondary)
                    .accessibilityIdentifier(component.rawValue + "-status")
            }
            if let health = state.health, component == .backend {
                Text("\(health.activeRuns) active · \(health.queuedRuns) queued runs · \(health.activeOptimizations + health.queuedOptimizations) optimizer jobs")
                    .font(.caption).foregroundStyle(.secondary)
            }
            if let date = state.lastSuccess {
                Text("Last healthy \(date.formatted(date: .omitted, time: .standard)) · \(owned ? "Managed" : "Observed")")
                    .font(.caption).foregroundStyle(.secondary)
            }
            if let error = state.error, state.status != .healthy && state.status != .stopped {
                Text(error).font(.caption).foregroundStyle(.orange).lineLimit(3).help(error)
            }
            HStack(spacing: 10) {
                Button("Start") { supervisor.action { try await supervisor.start(component) } }
                    .disabled(controlsDisabled || state.status == .healthy || state.status == .starting)
                Button("Stop") { supervisor.action { try await supervisor.requestStop(component) } }
                    .disabled(controlsDisabled || !owned)
                Button("Restart") { supervisor.action { try await supervisor.requestRestart(component) } }
                    .disabled(controlsDisabled || !owned)
                if !owned, state.health != nil {
                    Button("Take Ownership") { supervisor.action { try await supervisor.takeover(component) } }.disabled(controlsDisabled)
                }
            }.controlSize(.small)
        }
    }
    @ViewBuilder private func buildItems(rollback: Bool) -> some View {
        ForEach([Component.frontend, .backend], id: \.rawValue) { component in
            Button(component.title) { supervisor.action { try await supervisor.buildAndRestart([component], rollback: rollback) } }
        }
        Button("Both Components") { supervisor.action { try await supervisor.buildAndRestart([.backend, .frontend], rollback: rollback) } }
    }
}

@MainActor private final class SettingsDraft: ObservableObject {
    @Published var root = ""
    @Published var dotnet = ""
    @Published var frontend = "5077"
    @Published var backend = "5079"
    @Published var lan = ""
    @Published var result: String?
}

private struct SettingsPanel: View {
    @ObservedObject var supervisor: Supervisor
    @StateObject private var draft = SettingsDraft()
    var body: some View {
        Form {
            Toggle("Launch at Mac Login", isOn: Binding(get: { supervisor.launchAtLogin }, set: { supervisor.updateLoginItem(enabled: $0) }))
            Text(supervisor.loginStatus).font(.caption).foregroundStyle(.secondary)
            if SMAppService.mainApp.status == .requiresApproval {
                Button("Open Login Items Settings") { SMAppService.openSystemSettingsLoginItems() }
            }
            TextField("Project Folder", text: $draft.root)
            Button("Choose Project Folder…") {
                let picker = NSOpenPanel(); picker.canChooseDirectories = true; picker.canChooseFiles = false
                if picker.runModal() == .OK, let url = picker.url { draft.root = url.path }
            }
            TextField(".NET Executable", text: $draft.dotnet)
            TextField("Frontend Port", text: $draft.frontend)
            TextField("Backend Port", text: $draft.backend)
            TextField("Optional LAN IPv4", text: $draft.lan)
            Text("Stop managed components before changing settings. The backend always stays on loopback.").font(.caption).foregroundStyle(.secondary)
            if let result = draft.result { Text(result).font(.callout) }
            Button("Save Settings") {
                do {
                    try supervisor.configure(projectRoot: draft.root, dotnet: draft.dotnet, frontend: draft.frontend, backend: draft.backend, lan: draft.lan)
                    draft.result = "Saved. Use Start to launch Crucible."
                } catch { draft.result = error.localizedDescription }
            }.disabled(supervisor.busy)
        }.padding(24).frame(width: 550).onAppear {
            draft.root = supervisor.configuration.projectRoot; draft.dotnet = supervisor.configuration.dotnet
            draft.frontend = String(supervisor.configuration.frontendPort); draft.backend = String(supervisor.configuration.backendPort)
            draft.lan = supervisor.configuration.lanAddress ?? ""
        }
    }
}
