import Foundation

private struct ClientMessage: Encodable {
    var type: String
    var protocolVersion: Int? = nil
    var clientID: String? = nil
    var hostEpoch: String? = nil
    var controlLeaseID: String? = nil
    var command: MicroCommand? = nil
    var requestIDs: [UUID]? = nil
}

private struct ServerMessage: Decodable {
    let type: String
    var protocolVersion: Int?
    var hostID: String?
    var hostEpoch: String?
    var controlLeaseID: String?
    var ttlSeconds: Double?
    var snapshot: MicroSnapshot?
    var receipt: MicroReceipt?
    var reason: String?
}

@MainActor
final class WebSocketTransport: MicroTransport {
    private let credentials: HostCredentials
    private var session: URLSession?
    private var socket: URLSessionWebSocketTask?
    private var reader: Task<Void, Never>?
    private var heartbeat: Task<Void, Never>?
    private var handshake: Task<Void, Never>?
    private var receive: (@MainActor (MicroEvent) -> Void)?
    private var generation = UUID()
    private var hostID: String?
    private var epoch: String?
    private var lease: String?
    private var leaseExpiry: TimeInterval = 0
    private var renewAt: TimeInterval = 0
    private var synchronized = false

    init(credentials: HostCredentials) { self.credentials = credentials }

    func connect(_ receive: @escaping @MainActor (MicroEvent) -> Void) {
        disconnect()
        self.receive = receive
        receive(.connection(.connecting))
        do {
            let url = try credentials.validatedURL()
            let configuration = URLSessionConfiguration.ephemeral
            configuration.waitsForConnectivity = true
            configuration.timeoutIntervalForRequest = 12
            configuration.httpCookieStorage = nil
            let delegate = HostTrustDelegate(host: url.host!, pin: credentials.normalizedPin)
            let session = URLSession(configuration: configuration, delegate: delegate, delegateQueue: nil)
            self.session = session
            var request = URLRequest(url: url)
            request.setValue("Bearer \(credentials.token)", forHTTPHeaderField: "Authorization")
            let socket = session.webSocketTask(with: request)
            socket.maximumMessageSize = 1_048_576
            self.socket = socket
            let token = generation
            socket.resume()
            reader = Task { [weak self] in
                guard let self else { return }
                do {
                    let defaults = UserDefaults.standard
                    let clientID = defaults.string(forKey: "micro.clientID") ?? UUID().uuidString
                    defaults.set(clientID, forKey: "micro.clientID")
                    try await self.write(ClientMessage(type: "hello", protocolVersion: 1, clientID: clientID))
                    while !Task.isCancelled && self.generation == token {
                        let message = try await socket.receive()
                        guard self.generation == token else { return }
                        let data: Data
                        switch message {
                        case .data(let bytes): data = bytes
                        case .string(let string): data = Data(string.utf8)
                        @unknown default: throw MicroError.message("不支持的 Host 消息")
                        }
                        try await self.handle(JSONDecoder().decode(ServerMessage.self, from: data))
                    }
                } catch {
                    if self.generation == token && !Task.isCancelled { self.fail(error.localizedDescription) }
                }
            }
            handshake = Task { [weak self] in
                try? await Task.sleep(nanoseconds: 12_000_000_000)
                guard !Task.isCancelled, let self, self.generation == token, !self.synchronized else { return }
                self.fail("Host 同步超时")
            }
        } catch { fail(error.localizedDescription) }
    }

    func send(_ command: MicroCommand) async throws {
        guard synchronized, command.hostEpoch == epoch, command.controlLeaseID == lease,
              command.hostID == hostID, ProcessInfo.processInfo.systemUptime < leaseExpiry else {
            receive?(.receipt(MicroReceipt(requestID: command.requestID, status: .notSent,
                                           reason: "连接状态已变化")))
            return
        }
        try await write(ClientMessage(type: "command.execute", command: command))
    }

    func query(_ ids: [UUID]) async throws {
        guard synchronized, !ids.isEmpty else { return }
        try await write(ClientMessage(type: "command.status", hostEpoch: epoch, requestIDs: ids))
    }

    private func handle(_ message: ServerMessage) async throws {
        switch message.type {
        case "welcome":
            guard message.protocolVersion == 1, let host = message.hostID, !host.isEmpty,
                  let epoch = message.hostEpoch, !epoch.isEmpty, self.epoch == nil else {
                throw MicroError.message("Host 协议不兼容")
            }
            hostID = host; self.epoch = epoch
            receive?(.connection(.syncing))
            try await write(ClientMessage(type: "session.acquire", hostEpoch: epoch))
        case "session.acquired", "session.renewed":
            guard epoch != nil, message.hostEpoch == epoch, let lease = message.controlLeaseID,
                  !lease.isEmpty, let ttl = message.ttlSeconds, ttl.isFinite, (5...300).contains(ttl) else {
                throw MicroError.message("无效控制租约")
            }
            let changed = self.lease != lease
            self.lease = lease
            leaseExpiry = ProcessInfo.processInfo.systemUptime + ttl
            renewAt = ProcessInfo.processInfo.systemUptime + ttl / 2
            receive?(.lease(lease))
            if changed || !synchronized {
                synchronized = false
                receive?(.connection(.syncing))
                try await write(ClientMessage(type: "state.subscribe", hostEpoch: epoch))
            }
            startHeartbeat()
        case "state.snapshot":
            guard let snapshot = message.snapshot, snapshot.hostID == hostID,
                  snapshot.hostEpoch == epoch, let lease,
                  ProcessInfo.processInfo.systemUptime < leaseExpiry,
                  snapshot.revision >= 0,
                  snapshot.threads.count <= 256, snapshot.models.count <= 256,
                  Set(snapshot.threads.map(\.id)).count == snapshot.threads.count,
                  Set(snapshot.models.map(\.id)).count == snapshot.models.count,
                  snapshot.threads.allSatisfy({ !$0.id.isEmpty }),
                  snapshot.models.allSatisfy({ !$0.id.isEmpty && !$0.efforts.isEmpty }) else {
                throw MicroError.message("无效状态快照")
            }
            receive?(.lease(lease))
            receive?(.snapshot(snapshot))
            synchronized = true
            handshake?.cancel()
            receive?(.connection(.ready))
        case "state.delta":
            // v0.1 deliberately resynchronizes complete snapshots instead of guessing patch semantics.
            guard message.hostEpoch == epoch else { throw MicroError.message("Host 会话已变化") }
            if synchronized {
                synchronized = false
                receive?(.connection(.syncing))
                try await write(ClientMessage(type: "state.subscribe", hostEpoch: epoch))
            }
        case "command.receipt":
            guard epoch != nil, message.hostEpoch == epoch, let receipt = message.receipt else {
                throw MicroError.message("无效操作回执")
            }
            receive?(.receipt(receipt))
        case "error": throw MicroError.message(message.reason ?? "Host 拒绝连接")
        default: throw MicroError.message("Host 消息版本不兼容")
        }
    }

    private func startHeartbeat() {
        guard heartbeat == nil else { return }
        let token = generation
        heartbeat = Task { [weak self] in
            while !Task.isCancelled {
                try? await Task.sleep(nanoseconds: 1_000_000_000)
                guard !Task.isCancelled, let self, self.generation == token else { return }
                let now = ProcessInfo.processInfo.systemUptime
                if now >= self.leaseExpiry { self.fail("控制租约已过期"); return }
                if now >= self.renewAt {
                    self.renewAt = now + 3
                    do {
                        try await self.write(ClientMessage(type: "session.renew", hostEpoch: self.epoch,
                                                           controlLeaseID: self.lease))
                    } catch { self.fail(error.localizedDescription); return }
                }
            }
        }
    }

    private func write(_ message: ClientMessage) async throws {
        guard let socket else { throw MicroError.message("Host 未连接") }
        let data = try JSONEncoder().encode(message)
        try await socket.send(.string(String(decoding: data, as: UTF8.self)))
    }

    private func fail(_ reason: String) {
        let callback = receive
        disconnect()
        callback?(.connection(.failed(reason)))
    }

    func disconnect() {
        generation = UUID()
        reader?.cancel(); reader = nil
        heartbeat?.cancel(); heartbeat = nil
        handshake?.cancel(); handshake = nil
        socket?.cancel(with: .goingAway, reason: nil); socket = nil
        session?.invalidateAndCancel(); session = nil
        receive = nil; hostID = nil; epoch = nil; lease = nil
        synchronized = false
    }
}
