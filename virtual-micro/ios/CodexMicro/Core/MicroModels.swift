import Foundation

enum ThreadStatus: String, Codable {
    case idle, running, completed, waiting, error
}

enum MicroCommandKind: String, Codable, CaseIterable {
    case open = "desktop.openThread"
    case model = "thread.setModel"
    case effort = "thread.setEffort"
    case fast = "thread.setFast"
    case plan = "thread.setPlan"
    case approve = "approval.accept"
    case decline = "approval.decline"
    case fork = "thread.fork"
    case stop = "turn.stop"
}

struct MicroThread: Codable, Identifiable {
    let id: String
    var title: String
    var status: ThreadStatus
    var modelID: String
    var effort: String
    var fast: Bool
    var plan: Bool
    var turnID: String?
    var approvalID: String?
    var approvalSummary: String?
    var capabilities: Set<MicroCommandKind>
}

struct MicroModel: Codable, Identifiable {
    let id: String
    let name: String
    let efforts: [String]
}

struct MicroQuota: Codable {
    let fiveHour: Double?
    let weekly: Double?
}

struct MicroSnapshot: Codable {
    let hostID: String
    let hostEpoch: String
    let revision: Int64
    var threads: [MicroThread]
    let models: [MicroModel]
    let quota: MicroQuota
}

struct MicroCommand: Codable {
    let requestID: UUID
    let hostID: String
    let hostEpoch: String
    let controlLeaseID: String
    let threadID: String
    let kind: MicroCommandKind
    let expectedRevision: Int64
    var value: String?
    var turnID: String?
    var approvalID: String?
}

struct MicroReceipt: Codable {
    enum Status: String, Codable { case accepted, applied, rejected, notSent, unknown }
    let requestID: UUID
    let status: Status
    var reason: String?
}

enum MicroConnection: Equatable {
    case demo, disconnected, connecting, syncing, ready, suspended, failed(String)

    var label: String {
        switch self {
        case .demo: return "DEMO"
        case .disconnected: return "离线"
        case .connecting: return "连接中"
        case .syncing: return "同步中"
        case .ready: return "已连接"
        case .suspended: return "已暂停"
        case .failed: return "连接失败"
        }
    }

    var allowsCommands: Bool { self == .demo || self == .ready }
}

enum MicroEvent {
    case connection(MicroConnection)
    case lease(String)
    case snapshot(MicroSnapshot)
    case receipt(MicroReceipt)
}

@MainActor
protocol MicroTransport: AnyObject {
    func connect(_ receive: @escaping @MainActor (MicroEvent) -> Void)
    func send(_ command: MicroCommand) async throws
    func query(_ ids: [UUID]) async throws
    func disconnect()
}

enum MicroError: LocalizedError {
    case message(String)
    var errorDescription: String? {
        switch self { case .message(let text): return text }
    }
}
