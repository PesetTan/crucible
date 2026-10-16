import Foundation
import Darwin
import MonitorCore

@MainActor enum LifecycleChecks {
    static func run() async throws {
        let root = URL(fileURLWithPath: "/private/tmp/crucible-lifecycle-" + UUID().uuidString)
        defer { try? FileManager.default.removeItem(at: root) }
        try FileManager.default.createDirectory(at: root.appendingPathComponent("src/DeckTester"), withIntermediateDirectories: true)
        try Data().write(to: root.appendingPathComponent("src/DeckTester/DeckTester.csproj"))
        let executable = root.appendingPathComponent("DeckTester")
        let fixture = root.appendingPathComponent("host.c")
        try """
        #include <signal.h>
        #include <stdio.h>
        #include <unistd.h>
        int main(void) {
            signal(SIGTERM, SIG_IGN);
            if (fork() == 0) { while (1) pause(); }
            puts("ready"); fflush(stdout);
            while (1) pause();
        }
        """.write(to: fixture, atomically: true, encoding: .utf8)
        let compiler = Process(); compiler.executableURL = URL(fileURLWithPath: "/usr/bin/clang")
        compiler.arguments = [fixture.path, "-o", executable.path]
        try compiler.run(); compiler.waitUntilExit()
        guard compiler.terminationStatus == 0 else { throw MonitorError.message("Cannot compile the disposable lifecycle fixture.") }
        var config = MonitorConfiguration(projectRoot: root.path, dotnet: "/usr/bin/false")
        // Unused ports ensure that work status is unknown, rather than querying the user's hosts.
        config.frontendPort = 59471; config.backendPort = 59473
        let supervisor = try Supervisor(stateDirectory: root.appendingPathComponent("state"), initialConfiguration: config)
        XCTAssertTrue(supervisor.overallStopped)
        XCTAssertEqual(supervisor.statusLabel, "Crucible · Stopped")
        supervisor.states[.frontend]?.starting()
        XCTAssertFalse(supervisor.overallStopped)
        XCTAssertEqual(supervisor.statusLabel, "Crucible · Needs Attention")
        supervisor.states[.frontend]?.stopped()
        XCTAssertTrue(supervisor.overallStopped)
        let process = try ProcessDriver.spawn(executable: executable.path,
            arguments: [],
            environment: ["PATH": "/usr/bin:/bin"], directory: root.path, log: root.appendingPathComponent("fake.log"))
        defer { _ = kill(-process.pid, SIGKILL); _ = waitpid(process.pid, nil, 0) }
        for _ in 0..<50 {
            if (try? String(contentsOf: root.appendingPathComponent("fake.log"), encoding: .utf8).contains("ready")) == true { break }
            try await Task.sleep(nanoseconds: 20_000_000)
        }
        guard process.belongsToProject(root.path) else {
            throw MonitorError.message("Fixture identity mismatch: captured=\(process), current=\(String(describing: ProcessIdentity.read(process.pid))), root=\(root.path)")
        }
        supervisor.owned["backend"] = OwnedHost(identity: process, projectRoot: root.path, instanceId: nil, processGroup: process.pid)
        let children = ProcessDriver.descendants(of: supervisor.owned["backend"]!, root: root.path)
        XCTAssertEqual(children.count, 1)
        var approvals: [String] = []
        supervisor.confirmationHandler = { title, detail, _ in approvals.append(title); XCTAssertTrue(detail.contains("unknown")); return false }
        try await supervisor.requestRestart(.backend)
        XCTAssertTrue(process.isCurrent); XCTAssertEqual(approvals.count, 1)
        let quit = try await supervisor.quit()
        XCTAssertFalse(quit); XCTAssertTrue(process.isCurrent)
        supervisor.shutdownTimeout = 0.02
        approvals = []
        supervisor.confirmationHandler = { title, _, _ in approvals.append(title); return !title.hasPrefix("Force Stop") }
        do { try await supervisor.requestStop(.backend); XCTFail("Force-stop cancellation must keep ownership") } catch {}
        XCTAssertEqual(approvals, ["Stop Backend?", "Force Stop Backend?"])
        XCTAssertTrue(process.isCurrent); XCTAssertTrue(supervisor.owned["backend"] != nil)
        supervisor.confirmationHandler = { title, _, _ in approvals.append(title); return true }
        try await supervisor.requestStop(.backend)
        XCTAssertFalse(process.isCurrent); XCTAssertTrue(supervisor.owned["backend"] == nil)
        XCTAssertFalse(children.contains(where: { $0.isCurrent }))
        XCTAssertEqual(supervisor.states[.backend]?.status, .stopped)
    }
}
