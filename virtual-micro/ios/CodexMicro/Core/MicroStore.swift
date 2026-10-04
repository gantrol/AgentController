import Foundation

@MainActor
final class MicroStore {
    private(set) var snapshot: MicroSnapshot?
    private(set) var connection: MicroConnection = .disconnected
    private(set) var selectedID: String?
    private(set) var isDemo = true
    private(set) var pending: [UUID: MicroCommand] = [:]
    private(set) var uncertain: Set<UUID> = []
    private(set) var lastReceipt: MicroReceipt?
    var onChange: (() -> Void)?
    var onError: ((String) -> Void)?
    var page = UserDefaults.standard.integer(forKey: "micro.page") {
        didSet { UserDefaults.standard.set(page, forKey: "micro.page"); onChange?() }
    }

    private var transport: MicroTransport = DemoTransport()
    private var leaseID = ""
    private var generation = UUID()
    private var active = false
    private var demoTransport: MicroTransport?
    private let journalKey = "micro.unresolvedCommands"

    var selected: MicroThread? { snapshot?.threads.first { $0.id == selectedID } }
    var isBusy: Bool { !pending.isEmpty }
    var canControl: Bool { active && connection.allowsCommands && !isBusy && !leaseID.isEmpty }
    var models: [MicroModel] { snapshot?.models ?? [] }

    func select(_ threadID: String) {
        guard snapshot?.threads.contains(where: { $0.id == threadID }) == true else { return }
        selectedID = threadID
        onChange?()
    }

    func supports(_ kind: MicroCommandKind) -> Bool {
        guard canControl, let selected, selected.capabilities.contains(kind) else { return false }
        if kind == .approve || kind == .decline {
            return selected.approvalID != nil && selected.approvalSummary?.isEmpty == false
        }
        return true
    }

    func resume() {
        guard !active else { return }
        active = true
        connect()
    }

    func suspend() {
        guard active else { return }
        active = false
        generation = UUID()
        transport.disconnect()
        leaseID = ""
        uncertain.formUnion(pending.keys)
        connection = .suspended
        onChange?()
    }

    func useDemo() {
        replace(with: demoTransport ?? DemoTransport(), demo: true)
    }

    func useHost(_ credentials: HostCredentials) {
        if isDemo { demoTransport = transport }
        replace(with: WebSocketTransport(credentials: credentials), demo: false)
    }

    private func replace(with next: MicroTransport, demo: Bool) {
        // Never let a different host inherit the previous host's task selection.
        generation = UUID()
        transport.disconnect()
        transport = next
        isDemo = demo
        snapshot = nil
        selectedID = nil
        leaseID = ""
        pending.removeAll()
        uncertain.removeAll()
        lastReceipt = nil
        if active { connect() } else { onChange?() }
    }

    private func connect() {
        let token = UUID()
        generation = token
        leaseID = ""
        connection = .connecting
        onChange?()
        transport.connect { [weak self] event in
            guard let self, self.active, self.generation == token else { return }
            self.receive(event)
        }
    }

    private func receive(_ event: MicroEvent) {
        switch event {
        case .connection(let state):
            connection = state
            if !state.allowsCommands {
                leaseID = ""
                uncertain.formUnion(pending.keys)
            }
            if state == .ready || state == .demo {
                let ids = Array(uncertain)
                if !ids.isEmpty {
                    Task { [weak self] in try? await self?.transport.query(ids) }
                }
            }
        case .lease(let id): leaseID = id
        case .snapshot(let next):
            if let current = snapshot, current.hostID == next.hostID, current.hostEpoch == next.hostEpoch,
               next.revision <= current.revision { return }
            if let current = snapshot, current.hostID != next.hostID || current.hostEpoch != next.hostEpoch {
                selectedID = nil
                uncertain.formUnion(pending.keys)
            }
            if !isDemo, snapshot == nil {
                for command in journal() where command.hostID == next.hostID {
                    pending[command.requestID] = command
                    uncertain.insert(command.requestID)
                }
            }
            snapshot = next
            if !next.threads.contains(where: { $0.id == selectedID }) {
                selectedID = next.threads.first?.id
            }
        case .receipt(let receipt):
            guard pending[receipt.requestID] != nil else { return }
            lastReceipt = receipt
            switch receipt.status {
            case .accepted: break
            case .unknown: uncertain.insert(receipt.requestID)
            case .applied, .rejected, .notSent:
                pending.removeValue(forKey: receipt.requestID)
                uncertain.remove(receipt.requestID)
                if !isDemo { saveJournal(journal().filter { $0.requestID != receipt.requestID }) }
            }
            if receipt.status == .rejected, let reason = receipt.reason { onError?(reason) }
        }
        onChange?()
    }

    func execute(_ kind: MicroCommandKind, value: String? = nil) {
        guard supports(kind), let thread = selected, let snapshot else { return }
        if kind == .stop && thread.turnID == nil { return }
        if (kind == .approve || kind == .decline) && thread.approvalID == nil { return }
        let command = MicroCommand(requestID: UUID(), hostID: snapshot.hostID,
                                   hostEpoch: snapshot.hostEpoch, controlLeaseID: leaseID,
                                   threadID: thread.id, kind: kind, expectedRevision: snapshot.revision,
                                   value: value, turnID: thread.turnID, approvalID: thread.approvalID)
        pending[command.requestID] = command
        if !isDemo { saveJournal(journal() + [command]) }
        lastReceipt = nil
        onChange?()
        let sender = transport
        Task { [weak self] in
            do { try await sender.send(command) }
            catch {
                guard let self, self.pending[command.requestID] != nil else { return }
                self.receive(.receipt(MicroReceipt(requestID: command.requestID, status: .unknown,
                                                   reason: error.localizedDescription)))
            }
        }
        Task { [weak self] in
            try? await Task.sleep(nanoseconds: 15_000_000_000)
            guard let self, self.pending[command.requestID] != nil else { return }
            self.receive(.receipt(MicroReceipt(requestID: command.requestID, status: .unknown,
                                               reason: "等待 Host 确认")))
        }
    }

    // Only IDs and absolute setting values are retained; reconnect queries them without replaying writes.
    private func journal() -> [MicroCommand] {
        guard let data = UserDefaults.standard.data(forKey: journalKey) else { return [] }
        return (try? JSONDecoder().decode([MicroCommand].self, from: data)) ?? []
    }

    private func saveJournal(_ commands: [MicroCommand]) {
        if let data = try? JSONEncoder().encode(commands) { UserDefaults.standard.set(data, forKey: journalKey) }
    }

    func stepEffort(_ steps: Int) {
        guard let thread = selected,
              let model = models.first(where: { $0.id == thread.modelID }),
              let index = model.efforts.firstIndex(of: thread.effort) else { return }
        let next = min(max(index + steps, 0), model.efforts.count - 1)
        if next != index { execute(.effort, value: model.efforts[next]) }
    }

    func toggleModel() {
        guard let selected, models.count > 1,
              let index = models.firstIndex(where: { $0.id == selected.modelID }) else { return }
        execute(.model, value: models[(index + 1) % models.count].id)
    }

    func queryPending() {
        Task { [weak self] in
            guard let self else { return }
            do { try await self.transport.query(Array(self.pending.keys)) }
            catch { self.onError?(error.localizedDescription) }
        }
    }
}
