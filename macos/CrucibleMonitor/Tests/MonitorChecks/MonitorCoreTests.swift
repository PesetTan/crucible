import Foundation
import Darwin
import MonitorCore

final class MonitorCoreChecks {
    private func health(ready: Bool = true, work: Int = 0, role: String = "service", root: String = "/tmp/project") -> HostHealth {
        HostHealth(version: 1, application: "crucible", role: role, processId: getpid(), instanceId: "instance",
                   startedAt: "2026-10-15T00:00:00Z", projectRoot: root, ready: ready, database: "available",
                   activeRuns: work, queuedRuns: 0, activeOptimizations: 0, queuedOptimizations: 0, error: nil)
    }
    func testTransientFailuresAndOneNotificationPerEpisode() {
        var state = HealthState()
        state.observe(health())
        XCTAssertFalse(state.observe(nil)); XCTAssertFalse(state.observe(nil)); XCTAssertTrue(state.observe(nil))
        XCTAssertEqual(state.status, .unresponsive)
        XCTAssertFalse(state.observe(nil)); state.observe(health()); XCTAssertEqual(state.status, .healthy)
        XCTAssertFalse(state.observe(nil)); XCTAssertFalse(state.observe(nil)); XCTAssertTrue(state.observe(nil))
    }
    func testStartupAndWakeGraceAndIntentionalStop() {
        let date = Date(timeIntervalSince1970: 100)
        var state = HealthState(); state.starting(now: date)
        for second in [0.0, 5, 10, 59] { XCTAssertFalse(state.observe(nil, now: date.addingTimeInterval(second))) }
        XCTAssertEqual(state.status, .starting)
        XCTAssertTrue(state.observe(nil, now: date.addingTimeInterval(61)))
        state.observe(health()); state.wake(now: date)
        for second in [0.0, 5, 10] { XCTAssertFalse(state.observe(nil, now: date.addingTimeInterval(second))) }
        XCTAssertTrue(state.observe(nil, now: date.addingTimeInterval(16)))
        state.stopped(); state.wake(now: date)
        for _ in 0..<6 { XCTAssertFalse(state.observe(nil)) }
        XCTAssertEqual(state.status, .stopped)
        XCTAssertFalse(state.observe(health())); XCTAssertEqual(state.status, .healthy)
    }
    func testConflictAndHealthIdentityValidation() {
        var state = HealthState()
        XCTAssertTrue(state.observe(nil, conflict: true)); XCTAssertEqual(state.status, .conflict)
        XCTAssertFalse(state.observe(nil, conflict: true))
        XCTAssertTrue(health().matches(.backend, root: "/tmp/project"))
        XCTAssertFalse(health().matches(.frontend, root: "/tmp/project"))
        XCTAssertFalse(health().matches(.backend, root: "/tmp/other"))
    }
    func testUnknownAndQueuedWorkRequireConfirmation() {
        XCTAssertTrue(ShutdownPolicy.needsWorkConfirmation(nil))
        XCTAssertTrue(ShutdownPolicy.needsWorkConfirmation(health(ready: false)))
        XCTAssertTrue(ShutdownPolicy.needsWorkConfirmation(health(work: 1)))
        XCTAssertFalse(ShutdownPolicy.needsWorkConfirmation(health()))
    }
    func testInstanceLockAndServiceLock() throws {
        let directory = temporaryDirectory(); defer { try? FileManager.default.removeItem(at: directory) }
        let lock = try InstanceLock(path: directory.appendingPathComponent("monitor.lock").path)
        try withExtendedLifetime(lock) { XCTAssertThrowsError(try InstanceLock(path: directory.appendingPathComponent("monitor.lock").path)) }
        let data = directory.appendingPathComponent("data"); try FileManager.default.createDirectory(at: data, withIntermediateDirectories: true)
        XCTAssertFalse(ProcessDriver.serviceIsLocked(root: directory.path))
        let fd = Darwin.open(data.appendingPathComponent("service.lock").path, O_CREAT | O_RDWR, S_IRUSR | S_IWUSR)
        defer { close(fd) }; XCTAssertEqual(flock(fd, LOCK_EX | LOCK_NB), 0)
        XCTAssertTrue(ProcessDriver.serviceIsLocked(root: directory.path))
        XCTAssertTrue(FileManager.default.fileExists(atPath: data.appendingPathComponent("service.lock").path))
    }
    func testOccupiedPortCannotBeMistakenForStoppedHost() throws {
        let fd = socket(AF_INET, SOCK_STREAM, 0); defer { close(fd) }
        var address = sockaddr_in(); address.sin_len = UInt8(MemoryLayout.size(ofValue: address))
        address.sin_family = sa_family_t(AF_INET); address.sin_addr.s_addr = inet_addr("127.0.0.1")
        let result = withUnsafePointer(to: &address) { $0.withMemoryRebound(to: sockaddr.self, capacity: 1) { Darwin.bind(fd, $0, socklen_t(MemoryLayout<sockaddr_in>.size)) } }
        XCTAssertEqual(result, 0); XCTAssertEqual(listen(fd, 1), 0)
        var length = socklen_t(MemoryLayout.size(ofValue: address))
        withUnsafeMutablePointer(to: &address) { pointer in
            pointer.withMemoryRebound(to: sockaddr.self, capacity: 1) { _ = getsockname(fd, $0, &length) }
        }
        XCTAssertTrue(ProcessDriver.portIsOccupied(Int(UInt16(bigEndian: address.sin_port))))
    }
    func testSpawnIsolationAndRefusalToSignalUnrelatedProcess() throws {
        let directory = temporaryDirectory(); defer { try? FileManager.default.removeItem(at: directory) }
        let process = try ProcessDriver.spawn(executable: "/bin/sleep", arguments: ["20"], environment: [:], directory: directory.path, log: directory.appendingPathComponent("child.log"))
        defer { kill(process.pid, SIGKILL); _ = waitpid(process.pid, nil, 0) }
        XCTAssertTrue(process.isCurrent); XCTAssertEqual(getpgid(process.pid), process.pid)
        let host = OwnedHost(identity: process, projectRoot: directory.path, instanceId: nil, processGroup: process.pid)
        XCTAssertThrowsError(try ProcessDriver.signal(host, root: directory.path))
        XCTAssertTrue(process.isCurrent)
        let changed = ProcessIdentity(pid: process.pid, seconds: process.seconds + 1, microseconds: process.microseconds, executable: process.executable)
        XCTAssertFalse(changed.isCurrent)
    }
    func testFailedPublishDoesNotChangeSelectedRuntimes() async throws {
        let directory = temporaryDirectory(); defer { try? FileManager.default.removeItem(at: directory) }
        let project = directory.appendingPathComponent("src/DeckTester")
        try FileManager.default.createDirectory(at: project, withIntermediateDirectories: true)
        try Data().write(to: project.appendingPathComponent("DeckTester.csproj"))
        var config = MonitorConfiguration(projectRoot: directory.path, dotnet: "/usr/bin/false")
        config.runtimes = ["frontend": "old-ui", "backend": "old-backend"]
        do { _ = try await RuntimeBuilder.publish(configuration: config, log: directory.appendingPathComponent("build.log")); XCTFail("Expected failed build") }
        catch { XCTAssertEqual(config.runtimes["frontend"], "old-ui"); XCTAssertEqual(config.runtimes["backend"], "old-backend") }
    }
    func testLogRotationIsBounded() throws {
        let directory = temporaryDirectory(); defer { try? FileManager.default.removeItem(at: directory) }
        let file = directory.appendingPathComponent("host.log")
        try Data(repeating: 65, count: 6 * 1024 * 1024).write(to: file)
        ProcessDriver.rotate(file)
        XCTAssertEqual(try Data(contentsOf: file).count, 0)
        XCTAssertEqual(try Data(contentsOf: file.appendingPathExtension("previous")).count, 1_048_576)
    }
    func testHomebrewWrapperResolvesBeforeOwnershipCapture() throws {
        let directory = temporaryDirectory(); defer { try? FileManager.default.removeItem(at: directory) }
        for name in ["bin", "libexec"] { try FileManager.default.createDirectory(at: directory.appendingPathComponent(name), withIntermediateDirectories: true) }
        let wrapper = directory.appendingPathComponent("bin/dotnet")
        try Data("#!/bin/sh\nexit 1\n".utf8).write(to: wrapper)
        try FileManager.default.setAttributes([.posixPermissions: 0o755], ofItemAtPath: wrapper.path)
        let native = directory.appendingPathComponent("libexec/dotnet")
        try FileManager.default.copyItem(at: URL(fileURLWithPath: "/usr/bin/false"), to: native)
        let config = MonitorConfiguration(projectRoot: directory.path, dotnet: wrapper.path)
        XCTAssertEqual(try config.launchExecutable(), native.resolvingSymlinksInPath().path)
    }
    func testNativeAndLegacyRuntimeSelection() throws {
        let directory = temporaryDirectory(); defer { try? FileManager.default.removeItem(at: directory) }
        let config = MonitorConfiguration(projectRoot: directory.path, dotnet: "/usr/bin/false")
        XCTAssertFalse(RuntimeLayout.containsHost(directory.path, component: .backend))
        try Data().write(to: directory.appendingPathComponent("DeckTester.dll"))
        XCTAssertTrue(RuntimeLayout.containsHost(directory.path, component: .frontend))
        XCTAssertTrue(RuntimeLayout.containsHost(directory.path, component: .backend))
        XCTAssertEqual(try RuntimeLayout.launch(directory.path, component: .backend, configuration: config).arguments, [directory.path + "/DeckTester.dll"])
        let native = directory.appendingPathComponent("CrucibleBackend")
        try FileManager.default.copyItem(at: URL(fileURLWithPath: "/usr/bin/true"), to: native)
        let launch = try RuntimeLayout.launch(directory.path, component: .backend, configuration: config)
        XCTAssertEqual(launch.executable, native.path)
        XCTAssertEqual(launch.arguments, [])
        XCTAssertEqual(try RuntimeLayout.launch(directory.path, component: .frontend, configuration: config).arguments, [directory.path + "/DeckTester.dll"])
        try FileManager.default.removeItem(at: directory.appendingPathComponent("DeckTester.dll"))
        XCTAssertFalse(RuntimeLayout.containsHost(directory.path, component: .frontend))
        let ui = directory.appendingPathComponent("CrucibleUi")
        try FileManager.default.copyItem(at: URL(fileURLWithPath: "/usr/bin/true"), to: ui)
        XCTAssertTrue(RuntimeLayout.containsHost(directory.path, component: .frontend))
        let uiLaunch = try RuntimeLayout.launch(directory.path, component: .frontend, configuration: config)
        XCTAssertEqual(uiLaunch.executable, ui.path)
        XCTAssertEqual(uiLaunch.arguments, [])

        XCTAssertTrue(RuntimeLayout.containsHost(directory.path, component: .backend))
    }
    private func temporaryDirectory() -> URL {
        let directory = FileManager.default.temporaryDirectory.appendingPathComponent("crucible-test-" + UUID().uuidString)
        try! FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        return directory
    }
}


func XCTAssertTrue(_ condition: @autoclosure () -> Bool, file: StaticString = #file, line: UInt = #line) { precondition(condition(), "Expected true", file: file, line: line) }
func XCTAssertFalse(_ condition: @autoclosure () -> Bool, file: StaticString = #file, line: UInt = #line) { precondition(!condition(), "Expected false", file: file, line: line) }
func XCTAssertEqual<T: Equatable>(_ lhs: @autoclosure () throws -> T, _ rhs: @autoclosure () throws -> T, file: StaticString = #file, line: UInt = #line) { do { let a = try lhs(); let b = try rhs(); precondition(a == b, "Values differ: \(a) / \(b)", file: file, line: line) } catch { preconditionFailure("Unexpected error: \(error)", file: file, line: line) } }
func XCTAssertThrowsError<T>(_ expression: @autoclosure () throws -> T, file: StaticString = #file, line: UInt = #line) { do { _ = try expression(); preconditionFailure("Expected an error", file: file, line: line) } catch {} }
func XCTFail(_ message: String) { preconditionFailure(message) }

@main struct MonitorChecks {
    @MainActor static func main() async throws {
        let checks = MonitorCoreChecks()
        checks.testTransientFailuresAndOneNotificationPerEpisode()
        checks.testStartupAndWakeGraceAndIntentionalStop()
        checks.testConflictAndHealthIdentityValidation()
        checks.testUnknownAndQueuedWorkRequireConfirmation()
        try checks.testInstanceLockAndServiceLock()
        try checks.testOccupiedPortCannotBeMistakenForStoppedHost()
        try checks.testSpawnIsolationAndRefusalToSignalUnrelatedProcess()
        try await checks.testFailedPublishDoesNotChangeSelectedRuntimes()
        try checks.testLogRotationIsBounded()
        try checks.testHomebrewWrapperResolvesBeforeOwnershipCapture()
        try checks.testNativeAndLegacyRuntimeSelection()
        try await LifecycleChecks.run()
        print("Monitor checks passed: health, wake, ownership, locks, ports, failed builds, logging, shutdown approval and force-stop.")
    }
}
