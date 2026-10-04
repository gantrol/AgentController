import Foundation

@MainActor
final class DemoTransport: MicroTransport {
    private var receive: (@MainActor (MicroEvent) -> Void)?
    private var receipts: [UUID: MicroReceipt] = [:]
    private var revision: Int64 = 1
    private let epoch = UUID().uuidString
    private var threads: [MicroThread] = (0..<14).map { index in
        let statuses: [ThreadStatus] = [.running, .completed, .idle, .waiting, .error, .idle,
                                       .running, .completed, .waiting, .idle, .completed, .running,
                                       .idle, .completed]
        let names = ["UIKit 小键盘", "模型切换", "任务面板", "组件设计", "连接适配", "动画调节",
                     "状态同步", "图标资源", "页面切换", "布局整理", "文档更新", "协议设计",
                     "交互细节", "发布准备"]
        let status = statuses[index]
        return MicroThread(id: "demo-\(index)", title: names[index], status: status,
                           modelID: index.isMultiple(of: 2) ? "demo-sol" : "demo-luna",
                           effort: "high", fast: false, plan: false,
                           turnID: status == .running ? "turn-\(index)" : nil,
                           approvalID: status == .waiting ? "approval-\(index)" : nil,
                           approvalSummary: status == .waiting ? "演示命令：git status" : nil,
                           capabilities: Set(MicroCommandKind.allCases))
    }

    func connect(_ receive: @escaping @MainActor (MicroEvent) -> Void) {
        self.receive = receive
        receive(.lease("demo-lease"))
        publish()
        receive(.connection(.demo))
    }

    func send(_ command: MicroCommand) async throws {
        guard let index = threads.firstIndex(where: { $0.id == command.threadID }) else { return }
        if let receipt = receipts[command.requestID] { receive?(.receipt(receipt)); return }
        receive?(.receipt(MicroReceipt(requestID: command.requestID, status: .accepted)))
        switch command.kind {
        case .fast: threads[index].fast = command.value == "true"
        case .plan: threads[index].plan = command.value == "true"
        case .model: threads[index].modelID = command.value ?? threads[index].modelID
        case .effort: threads[index].effort = command.value ?? threads[index].effort
        case .approve, .decline:
            threads[index].approvalID = nil
            threads[index].approvalSummary = nil
            threads[index].status = command.kind == .approve ? .running : .idle
            threads[index].turnID = command.kind == .approve ? UUID().uuidString : nil
        case .stop: threads[index].status = .idle; threads[index].turnID = nil
        case .fork:
            var copy = threads[index]
            copy = MicroThread(id: UUID().uuidString, title: copy.title + " · 分叉", status: .idle,
                               modelID: copy.modelID, effort: copy.effort, fast: copy.fast,
                               plan: copy.plan, capabilities: copy.capabilities)
            threads.insert(copy, at: 0)
        case .open: break
        }
        revision += 1
        let receipt = MicroReceipt(requestID: command.requestID, status: .applied)
        receipts[command.requestID] = receipt
        publish()
        receive?(.receipt(receipt))
    }

    func query(_ ids: [UUID]) async throws {
        for id in ids { receive?(.receipt(receipts[id] ?? MicroReceipt(requestID: id, status: .unknown))) }
    }

    func disconnect() { receive = nil }

    private func publish() {
        receive?(.snapshot(MicroSnapshot(hostID: "demo", hostEpoch: epoch, revision: revision,
            threads: threads,
            models: [MicroModel(id: "demo-sol", name: "SOL", efforts: ["low", "medium", "high", "xhigh"]),
                     MicroModel(id: "demo-luna", name: "LUNA", efforts: ["low", "medium", "high", "xhigh"])],
            quota: MicroQuota(fiveHour: 77, weekly: 58))))
    }
}
