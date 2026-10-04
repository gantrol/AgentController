import UIKit

final class MicroViewController: UIViewController {
    private let store: MicroStore
    private let device = MicroDeviceView(frame: CGRect(origin: .zero, size: MicroTheme.designSize))
    private let scroll = UIScrollView()
    private let connectionButton = UIButton(type: .system)
    private let settingsButton = UIButton(type: .system)
    private let selectionLabel = UILabel()
    private var lastFeedback: UUID?

    init(store: MicroStore) { self.store = store; super.init(nibName: nil, bundle: nil) }
    required init?(coder: NSCoder) { fatalError("init(coder:) is unavailable") }
    override var preferredStatusBarStyle: UIStatusBarStyle { .darkContent }

    override func viewDidLoad() {
        super.viewDidLoad()
        view.backgroundColor = MicroTheme.backdrop
        scroll.showsVerticalScrollIndicator = false
        scroll.alwaysBounceVertical = false
        scroll.contentInsetAdjustmentBehavior = .never
        for case let gesture as UIPanGestureRecognizer in device.encoder.gestureRecognizers ?? [] {
            scroll.panGestureRecognizer.require(toFail: gesture)
        }
        scroll.addSubview(device)
        for child in [connectionButton, settingsButton, scroll, selectionLabel] {
            child.translatesAutoresizingMaskIntoConstraints = false
            view.addSubview(child)
        }
        let safe = view.safeAreaLayoutGuide
        NSLayoutConstraint.activate([
            connectionButton.leadingAnchor.constraint(equalTo: safe.leadingAnchor, constant: 22),
            connectionButton.topAnchor.constraint(equalTo: safe.topAnchor, constant: 8),
            connectionButton.heightAnchor.constraint(equalToConstant: 44),
            settingsButton.trailingAnchor.constraint(equalTo: safe.trailingAnchor, constant: -18),
            settingsButton.topAnchor.constraint(equalTo: connectionButton.topAnchor),
            settingsButton.widthAnchor.constraint(equalToConstant: 44),
            settingsButton.heightAnchor.constraint(equalToConstant: 44),
            scroll.leadingAnchor.constraint(equalTo: safe.leadingAnchor),
            scroll.trailingAnchor.constraint(equalTo: safe.trailingAnchor),
            scroll.topAnchor.constraint(equalTo: connectionButton.bottomAnchor, constant: 8),
            scroll.bottomAnchor.constraint(equalTo: selectionLabel.topAnchor, constant: -8),
            selectionLabel.leadingAnchor.constraint(equalTo: safe.leadingAnchor, constant: 24),
            selectionLabel.trailingAnchor.constraint(equalTo: safe.trailingAnchor, constant: -24),
            selectionLabel.bottomAnchor.constraint(equalTo: safe.bottomAnchor, constant: -16),
            selectionLabel.heightAnchor.constraint(greaterThanOrEqualToConstant: 28)
        ])
        connectionButton.addTarget(self, action: #selector(showConnection), for: .touchUpInside)
        settingsButton.setImage(UIImage(systemName: "slider.horizontal.3"), for: .normal)
        settingsButton.tintColor = MicroTheme.muted
        settingsButton.accessibilityLabel = "连接与设置"
        settingsButton.addTarget(self, action: #selector(showConnection), for: .touchUpInside)
        selectionLabel.font = .preferredFont(forTextStyle: .caption1)
        selectionLabel.adjustsFontForContentSizeCategory = true
        selectionLabel.textColor = MicroTheme.muted
        selectionLabel.textAlignment = .center
        selectionLabel.numberOfLines = 2
        device.onPage = { [weak store] in store?.page = $0 }
        device.onThread = { [weak store] in store?.select($0) }
        device.onCommand = { [weak self] in self?.perform($0) }
        device.encoder.onStep = { [weak store] in store?.stepEffort($0) }
        device.encoder.onTap = { [weak store] in store?.toggleModel() }
        device.encoder.onLongPress = { [weak self] in self?.showModels() }
        device.quota.onTap = { [weak store] in store?.toggleModel() }
        device.quota.onLongPress = { [weak self] in self?.showModels() }
        device.joystick.onUp = { [weak self] in self?.perform(.plan) }
        device.menuForThread = { [weak self] in self?.threadMenu($0) }
        store.onChange = { [weak self] in self?.render() }
        store.onError = { [weak self] in self?.showError($0) }
        render()
    }

    override func viewDidLayoutSubviews() {
        super.viewDidLayoutSubviews()
        let size = scroll.bounds.size
        guard size.width > 0, size.height > 0 else { return }
        // Keep the Windows device proportions; landscape may scroll instead of shrinking its keys.
        let scale = min((size.width - 16) / 590, min(1.20, max(0.62, size.height / 610)))
        let height = max(size.height, 610 * scale)
        device.bounds = CGRect(origin: .zero, size: MicroTheme.designSize)
        device.transform = CGAffineTransform(scaleX: scale, y: scale)
        device.center = CGPoint(x: size.width / 2, y: height / 2)
        scroll.contentSize = CGSize(width: size.width, height: height)
    }

    private func render() {
        guard isViewLoaded else { return }
        device.render(store)
        var configuration = UIButton.Configuration.tinted()
        configuration.title = store.connection.label
        configuration.image = UIImage(systemName: store.isDemo ? "circle.dotted" : "network")
        configuration.imagePadding = 7
        configuration.cornerStyle = .capsule
        configuration.baseForegroundColor = store.connection.allowsCommands ? MicroTheme.ink : MicroTheme.muted
        configuration.baseBackgroundColor = .white
        configuration.contentInsets = NSDirectionalEdgeInsets(top: 7, leading: 12, bottom: 7, trailing: 12)
        configuration.titleTextAttributesTransformer = UIConfigurationTextAttributesTransformer { attributes in
            var result = attributes; result.font = .systemFont(ofSize: 12, weight: .semibold); return result
        }
        connectionButton.configuration = configuration
        if case .failed(let reason) = store.connection { connectionButton.accessibilityValue = reason }
        else { connectionButton.accessibilityValue = nil }
        if let thread = store.selected {
            let model = store.models.first { $0.id == thread.modelID }?.name ?? thread.modelID
            let suffix = store.uncertain.isEmpty ? "\(model) · \(thread.effort)" : "操作待确认"
            selectionLabel.text = "\(thread.title)\n\(suffix)"
        } else { selectionLabel.text = nil }
        if let receipt = store.lastReceipt, receipt.status == .applied, receipt.requestID != lastFeedback {
            lastFeedback = receipt.requestID
            UINotificationFeedbackGenerator().notificationOccurred(.success)
        }
    }

    private func perform(_ kind: MicroCommandKind) {
        guard let thread = store.selected, store.supports(kind) else { return }
        switch kind {
        case .fast: store.execute(kind, value: String(!thread.fast))
        case .plan: store.execute(kind, value: String(!thread.plan))
        case .approve, .decline, .fork, .stop:
            let labels: [MicroCommandKind: String] = [.approve: "批准请求", .decline: "拒绝请求", .fork: "分叉会话", .stop: "停止任务"]
            let title = labels[kind] ?? "确认"
            let detail = (kind == .approve || kind == .decline)
                ? "\(thread.title)\n\n\(thread.approvalSummary ?? "")" : thread.title
            let alert = UIAlertController(title: title, message: detail, preferredStyle: .alert)
            alert.addAction(UIAlertAction(title: "取消", style: .cancel))
            alert.addAction(UIAlertAction(title: title, style: kind == .stop || kind == .decline ? .destructive : .default) { [weak self] _ in
                guard let self, let current = self.store.selected, current.id == thread.id,
                      current.turnID == thread.turnID, current.approvalID == thread.approvalID else { return }
                self.store.execute(kind)
            })
            present(alert, animated: true)
        default: store.execute(kind)
        }
    }

    private func threadMenu(_ id: String) -> UIMenu? {
        guard let thread = store.snapshot?.threads.first(where: { $0.id == id }) else { return nil }
        let open = UIAction(title: "在桌面打开", image: UIImage(systemName: "desktopcomputer"),
                            attributes: store.canControl && thread.capabilities.contains(.open) ? [] : .disabled) { [weak self] _ in
            self?.store.select(id); self?.perform(.open)
        }
        let stop = UIAction(title: "停止任务", image: UIImage(systemName: "stop.circle"),
                            attributes: store.canControl && thread.turnID != nil && thread.capabilities.contains(.stop)
                            ? .destructive : [.disabled, .destructive]) { [weak self] _ in
            self?.store.select(id); self?.perform(.stop)
        }
        return UIMenu(title: thread.title, children: [open, stop])
    }

    private func showModels() {
        guard let selected = store.selected else { return }
        let alert = UIAlertController(title: "模型", message: nil, preferredStyle: .actionSheet)
        for model in store.models {
            let action = UIAlertAction(title: (model.id == selected.modelID ? "✓ " : "") + model.name, style: .default) { [weak self] _ in
                guard let self, self.store.selectedID == selected.id else { return }
                self.store.execute(.model, value: model.id)
            }
            action.isEnabled = store.supports(.model)
            alert.addAction(action)
        }
        alert.addAction(UIAlertAction(title: "取消", style: .cancel))
        alert.popoverPresentationController?.sourceView = device.quota
        alert.popoverPresentationController?.sourceRect = device.quota.bounds
        present(alert, animated: true)
    }

    @objc private func showConnection() {
        let controller = ConnectionViewController(store: store)
        let navigation = UINavigationController(rootViewController: controller)
        navigation.sheetPresentationController?.detents = [.medium(), .large()]
        navigation.sheetPresentationController?.prefersGrabberVisible = true
        present(navigation, animated: true)
    }

    private func showError(_ message: String) {
        guard presentedViewController == nil, view.window != nil else { return }
        let alert = UIAlertController(title: "操作未完成", message: message, preferredStyle: .alert)
        alert.addAction(UIAlertAction(title: "好", style: .default))
        present(alert, animated: true)
    }
}
