import Foundation
import Darwin

public struct ProcessIdentity: Codable, Equatable {
    public let pid: Int32
    public let seconds: UInt64
    public let microseconds: UInt64
    public let executable: String
    package init(pid: Int32, seconds: UInt64, microseconds: UInt64, executable: String) {
        self.pid = pid; self.seconds = seconds; self.microseconds = microseconds; self.executable = executable
    }
    public static func read(_ pid: Int32) -> ProcessIdentity? {
        var info = proc_bsdinfo()
        guard proc_pidinfo(pid, PROC_PIDTBSDINFO, 0, &info, Int32(MemoryLayout.size(ofValue: info))) == MemoryLayout.size(ofValue: info),
              info.pbi_status != UInt32(SZOMB) else { return nil }
        var buffer = [CChar](repeating: 0, count: 4 * Int(MAXPATHLEN))
        guard proc_pidpath(pid, &buffer, UInt32(buffer.count)) > 0 else { return nil }
        return ProcessIdentity(pid: pid, seconds: info.pbi_start_tvsec, microseconds: info.pbi_start_tvusec,
                               executable: String(cString: buffer))
    }
    public var isCurrent: Bool { Self.read(pid) == self }
    public func belongsToProject(_ root: String) -> Bool {
        guard isCurrent else { return false }
        let project = URL(fileURLWithPath: root).resolvingSymlinksInPath().path + "/"
        let executableURL = URL(fileURLWithPath: executable).resolvingSymlinksInPath()
        if executableURL.path.hasPrefix(project), ["DeckTester", "CrucibleBackend", "CrucibleUi"].contains(executableURL.lastPathComponent) { return true }
        // KERN_PROCARGS2 contains argc, executable path, padding, argv, then environment.
        var mib: [Int32] = [CTL_KERN, KERN_PROCARGS2, pid]
        var size = 0
        guard sysctl(&mib, 3, nil, &size, nil, 0) == 0, size > 4, size < 4_194_304 else { return false }
        var data = [UInt8](repeating: 0, count: size)
        guard sysctl(&mib, 3, &data, &size, nil, 0) == 0 else { return false }
        let argc = data.withUnsafeBytes { $0.loadUnaligned(as: Int32.self) }
        var position = 4
        while position < size && data[position] != 0 { position += 1 }
        while position < size && data[position] == 0 { position += 1 }
        var arguments: [String] = []
        for _ in 0..<max(0, argc) {
            let start = position
            while position < size && data[position] != 0 { position += 1 }
            arguments.append(String(decoding: data[start..<position], as: UTF8.self)); position += 1
            if position >= size { break }
        }
        return URL(fileURLWithPath: executable).lastPathComponent == "dotnet" && arguments.contains {
            ($0.hasSuffix("/DeckTester.dll") || $0.hasSuffix("/CrucibleBackend.dll") || $0.hasSuffix("/CrucibleUi.dll")) && URL(fileURLWithPath: $0).resolvingSymlinksInPath().path.hasPrefix(project)
        }
    }
}

public struct OwnedHost: Codable {
    public let identity: ProcessIdentity
    public let projectRoot: String
    public let instanceId: String?
    public let processGroup: Int32?
    public let workers: [ProcessIdentity]?
    public var isAlive: Bool { identity.isCurrent || (workers?.contains(where: { $0.isCurrent }) == true) }
    public init(identity: ProcessIdentity, projectRoot: String, instanceId: String?, processGroup: Int32?, workers: [ProcessIdentity]? = nil) {
        self.identity = identity; self.projectRoot = projectRoot; self.instanceId = instanceId; self.processGroup = processGroup
        self.workers = workers
    }
    public func canSignal(root: String) -> Bool {
        projectRoot == root && ((identity.isCurrent && identity.belongsToProject(root)) || workers?.contains(where: { $0.isCurrent }) == true)
    }
}

public final class InstanceLock {
    private var descriptor: Int32 = -1
    public init(path: String) throws {
        descriptor = Darwin.open(path, O_CREAT | O_RDWR, S_IRUSR | S_IWUSR)
        guard descriptor >= 0 else { throw MonitorError.message("Cannot open the monitor instance lock.") }
        guard flock(descriptor, LOCK_EX | LOCK_NB) == 0 else {
            close(descriptor); descriptor = -1
            throw MonitorError.message("Crucible Monitor is already running.")
        }
        _ = fcntl(descriptor, F_SETFD, FD_CLOEXEC)
    }
    deinit { if descriptor >= 0 { flock(descriptor, LOCK_UN); close(descriptor) } }
}

public enum ProcessDriver {
    public static func serviceIsLocked(root: String) -> Bool {
        let path = root + "/data/service.lock"
        guard FileManager.default.fileExists(atPath: path) else { return false }
        let descriptor = Darwin.open(path, O_RDONLY)
        guard descriptor >= 0 else { return true }
        defer { close(descriptor) }
        guard flock(descriptor, LOCK_EX | LOCK_NB) == 0 else { return true }
        flock(descriptor, LOCK_UN); return false
    }
    public static func portIsOccupied(_ port: Int) -> Bool {
        let socketFD = socket(AF_INET, SOCK_STREAM, 0)
        guard socketFD >= 0 else { return true }
        defer { close(socketFD) }
        var reuse: Int32 = 1
        setsockopt(socketFD, SOL_SOCKET, SO_REUSEADDR, &reuse, socklen_t(MemoryLayout<Int32>.size))
        var address = sockaddr_in()
        address.sin_len = UInt8(MemoryLayout<sockaddr_in>.size); address.sin_family = sa_family_t(AF_INET)
        address.sin_port = UInt16(port).bigEndian; address.sin_addr.s_addr = inet_addr("127.0.0.1")
        return withUnsafePointer(to: &address) { pointer in
            pointer.withMemoryRebound(to: sockaddr.self, capacity: 1) { Darwin.bind(socketFD, $0, socklen_t(MemoryLayout<sockaddr_in>.size)) != 0 }
        }
    }
    /// Spawn into a dedicated process group before exec; never use a shell or broad process-name kills.
    public static func spawn(executable: String, arguments: [String], environment: [String: String],
                             directory: String, log: URL) throws -> ProcessIdentity {
        rotate(log)
        var actions: posix_spawn_file_actions_t?
        var attributes: posix_spawnattr_t?
        posix_spawn_file_actions_init(&actions); posix_spawnattr_init(&attributes)
        defer { posix_spawn_file_actions_destroy(&actions); posix_spawnattr_destroy(&attributes) }
        posix_spawn_file_actions_addchdir_np(&actions, directory)
        posix_spawn_file_actions_addopen(&actions, STDIN_FILENO, "/dev/null", O_RDONLY, 0)
        posix_spawn_file_actions_addopen(&actions, STDOUT_FILENO, log.path, O_WRONLY | O_CREAT | O_APPEND, S_IRUSR | S_IWUSR)
        posix_spawn_file_actions_adddup2(&actions, STDOUT_FILENO, STDERR_FILENO)
        posix_spawnattr_setflags(&attributes, Int16(POSIX_SPAWN_SETPGROUP))
        posix_spawnattr_setpgroup(&attributes, 0)
        let argv = ([executable] + arguments).map { strdup($0) } + [nil]
        let env = environment.sorted(by: { $0.key < $1.key }).map { strdup("\($0.key)=\($0.value)") } + [nil]
        defer { argv.forEach { free($0) }; env.forEach { free($0) } }
        var pid: pid_t = 0
        let result = argv.withUnsafeBufferPointer { a in env.withUnsafeBufferPointer { e in
            posix_spawn(&pid, executable, &actions, &attributes, a.baseAddress!, e.baseAddress!)
        } }
        guard result == 0 else { throw MonitorError.message("Launch failed: \(String(cString: strerror(result))). See \(log.lastPathComponent).") }
        guard let identity = ProcessIdentity.read(pid) else {
            _ = waitpid(pid, nil, WNOHANG)
            throw MonitorError.message("The process exited during startup. See \(log.lastPathComponent).")
        }
        return identity
    }
    public static func signal(_ host: OwnedHost, root: String, force: Bool = false) throws {
        guard host.projectRoot == root, host.identity.isCurrent, host.identity.belongsToProject(root) else { throw MonitorError.message("Process identity changed; refusing to signal it.") }
        guard kill(host.identity.pid, force ? SIGKILL : SIGTERM) == 0 || errno == ESRCH else {
            throw MonitorError.message("Cannot stop the process: \(String(cString: strerror(errno))).")
        }
    }
    public static func descendants(of host: OwnedHost, root: String) -> [ProcessIdentity] {
        guard host.projectRoot == root, host.identity.isCurrent, host.identity.belongsToProject(root) else { return [] }
        var result: [ProcessIdentity] = []; var pending = [host.identity.pid]; var seen: Set<Int32> = []
        while let parent = pending.popLast(), seen.count < 4096 {
            guard seen.insert(parent).inserted else { continue }
            var children = [Int32](repeating: 0, count: 4096)
            let count = proc_listchildpids(parent, &children, Int32(children.count * MemoryLayout<Int32>.size))
            for pid in children.prefix(max(0, Int(count))) where pid > 0 {
                if let identity = ProcessIdentity.read(pid) { result.append(identity); pending.append(pid) }
            }
        }
        return result
    }
    public static func forceStopWorkers(_ workers: [ProcessIdentity]) throws {
        for worker in workers.reversed() where worker.isCurrent {
            guard kill(worker.pid, SIGKILL) == 0 || errno == ESRCH else {
                throw MonitorError.message("An owned worker could not be stopped.")
            }
        }
    }
    public static func reap(_ pid: Int32) { _ = waitpid(pid, nil, WNOHANG) }
    public static func rotate(_ file: URL) {
        guard let attributes = try? FileManager.default.attributesOfItem(atPath: file.path),
              let size = attributes[.size] as? NSNumber, size.intValue > 5 * 1024 * 1024,
              let handle = try? FileHandle(forUpdating: file) else { return }
        defer { try? handle.close() }
        do {
            try handle.seek(toOffset: UInt64(max(0, size.intValue - 1_048_576)))
            let tail = try handle.readToEnd() ?? Data()
            try tail.write(to: file.appendingPathExtension("previous"), options: .atomic)
            try handle.truncate(atOffset: 0)
        } catch { /* Logging must not prevent lifecycle actions. */ }
    }
}
