import UIKit

final class MicroDeviceView: UIView {
    let encoder = EncoderControl(frame: CGRect(x: 92, y: 102, width: 88, height: 88))
    let joystick = JoystickControl(frame: CGRect(x: 406, y: 98, width: 96, height: 96))
    let quota = QuotaControl(frame: CGRect(x: 88, y: 416, width: 96, height: 96))
    let codexKey = KeycapControl(frame: CGRect(x: 406, y: 416, width: 96, height: 96))
    private let body = GradientView()
    private let plate = GradientView()
    private let controlPage = UIView(frame: CGRect(origin: .zero, size: MicroTheme.designSize))
    private let monitorPage = UIView(frame: CGRect(origin: .zero, size: MicroTheme.designSize))
    private let dots = UIPageControl()
    private let controlGlows = CALayer()
    private let monitorGlows = CALayer()
    private var agentKeys: [KeycapControl] = []
    private var monitorKeys: [KeycapControl] = []
    private var agentBlooms: [CALayer] = []
    private var monitorBlooms: [CALayer] = []
    private let actions: [KeycapControl] = (0..<4).map { _ in KeycapControl() }
    private let microphone = KeycapControl(frame: CGRect(x: 194, y: 416, width: 202, height: 96))
    var onPage: ((Int) -> Void)?
    var onThread: ((String) -> Void)?
    var onCommand: ((MicroCommandKind) -> Void)?
    var menuForThread: ((String) -> UIMenu?)?
    private var shownPage = -1

    override init(frame: CGRect) {
        super.init(frame: frame)
        buildShell()
        addSubview(controlPage)
        addSubview(monitorPage)
        controlPage.layer.addSublayer(controlGlows)
        monitorPage.layer.addSublayer(monitorGlows)
        controlGlows.frame = bounds
        monitorGlows.frame = bounds
        for slot in [1, 2, 4, 5, 6, 7] {
            let key = KeycapControl(frame: keyFrame(slot))
            agentKeys.append(key)
            agentBlooms.append(addBloom(for: key, to: controlGlows))
            controlPage.addSubview(key)
        }
        for slot in Array(0..<12) + [13, 14] {
            let key = KeycapControl(frame: keyFrame(slot))
            monitorKeys.append(key)
            monitorBlooms.append(addBloom(for: key, to: monitorGlows))
            monitorPage.addSubview(key)
        }
        controlPage.addSubview(encoder)
        controlPage.addSubview(joystick)
        for (index, key) in actions.enumerated() {
            key.frame = keyFrame(index + 8)
            controlPage.addSubview(key)
        }
        controlPage.addSubview(microphone)
        microphone.configure(symbol: "mic", label: "语音（不可用）", enabled: false)
        addSubview(quota)
        addSubview(codexKey)
        dots.numberOfPages = 2
        dots.currentPageIndicatorTintColor = MicroTheme.accent
        dots.pageIndicatorTintColor = UIColor(hex: 0x718592, alpha: 0.32)
        dots.backgroundStyle = .minimal
        dots.frame = CGRect(x: 229, y: 43, width: 132, height: 44)
        dots.addTarget(self, action: #selector(pageChanged), for: .valueChanged)
        dots.accessibilityLabel = "控制页 / 监控页"
        addSubview(dots)
        for direction in [UISwipeGestureRecognizer.Direction.left, .right] {
            let swipe = UISwipeGestureRecognizer(target: self, action: #selector(swiped(_:)))
            swipe.direction = direction
            swipe.delegate = self
            addGestureRecognizer(swipe)
        }
    }

    required init?(coder: NSCoder) { fatalError("init(coder:) is unavailable") }

    func render(_ store: MicroStore) {
        let stale = !store.connection.allowsCommands
        let threads = store.snapshot?.threads ?? []
        updateKeys(agentKeys, blooms: agentBlooms, threads: Array(threads.prefix(6)), store: store, stale: stale)
        updateKeys(monitorKeys, blooms: monitorBlooms, threads: Array(threads.prefix(14)), store: store, stale: stale)
        let kinds: [MicroCommandKind] = [.fast, .approve, .decline, .fork]
        let icons = ["bolt", "checkmark.circle", "xmark.circle", "arrow.triangle.branch"]
        let labels = ["Fast", "批准", "拒绝", "分叉"]
        for index in 0..<4 {
            let kind = kinds[index]
            let approvalReady = kind != .approve && kind != .decline || store.selected?.approvalID != nil
            actions[index].configure(symbol: icons[index], label: labels[index],
                                     enabled: store.supports(kind) && approvalReady,
                                     active: kind == .fast && store.selected?.fast == true)
            actions[index].onTap = { [weak self] in self?.onCommand?(kind) }
        }
        let rotaryAvailable = store.connection.allowsCommands &&
            (store.selected?.capabilities.contains(.effort) == true || store.selected?.capabilities.contains(.model) == true)
        encoder.isEnabled = rotaryAvailable
        encoder.isUserInteractionEnabled = rotaryAvailable
        encoder.accessibilityValue = store.selected?.effort
        joystick.isEnabled = store.supports(.plan)
        joystick.planEnabled = store.selected?.plan == true
        codexKey.configure(symbol: "terminal", label: "在桌面打开", enabled: store.supports(.open))
        codexKey.onTap = { [weak self] in self?.onCommand?(.open) }
        codexKey.contextMenu = { [weak self, weak store] in
            guard let id = store?.selectedID else { return nil }
            return self?.menuForThread?(id)
        }
        quota.render(quota: store.snapshot?.quota, connected: store.connection.allowsCommands,
                     synced: !stale && store.selected != nil, busy: store.isBusy, receipt: store.lastReceipt)
        let page = min(max(store.page, 0), 1)
        dots.currentPage = page
        if page != shownPage {
            let animated = shownPage >= 0 && !UIAccessibility.isReduceMotionEnabled
            shownPage = page
            let changes = { self.controlPage.isHidden = page != 0; self.monitorPage.isHidden = page != 1 }
            if animated {
                UIView.transition(with: self, duration: 0.22,
                                  options: [.transitionCrossDissolve, .allowUserInteraction], animations: changes, completion: nil)
            } else { changes() }
        }
        if stale { encoder.cancelInteraction() }
    }

    private func updateKeys(_ keys: [KeycapControl], blooms: [CALayer], threads: [MicroThread],
                            store: MicroStore, stale: Bool) {
        for index in keys.indices {
            let thread = index < threads.count ? threads[index] : nil
            let key = keys[index]
            key.configure(thread: thread, selected: thread?.id == store.selectedID && thread != nil, stale: stale)
            let id = thread?.id
            key.onTap = { [weak self] in if let id { self?.onThread?(id) } }
            key.contextMenu = { [weak self] in id.flatMap { self?.menuForThread?($0) } }
            CATransaction.begin(); CATransaction.setDisableActions(true)
            blooms[index].backgroundColor = key.glowColor.withAlphaComponent(0.12).cgColor
            blooms[index].shadow(key.glowColor, opacity: 1, radius: 14)
            blooms[index].opacity = key.glowOpacity
            CATransaction.commit()
        }
    }

    private func keyFrame(_ slot: Int) -> CGRect {
        CGRect(x: 88 + CGFloat(slot % 4) * 106, y: 98 + CGFloat(slot / 4) * 106, width: 96, height: 96)
    }

    private func addBloom(for key: KeycapControl, to parent: CALayer) -> CALayer {
        let bloom = CALayer()
        bloom.frame = key.frame.insetBy(dx: 2, dy: 2)
        bloom.cornerRadius = 17
        bloom.shadowPath = UIBezierPath(roundedRect: bloom.bounds, cornerRadius: 17).cgPath
        parent.addSublayer(bloom)
        return bloom
    }

    private func buildShell() {
        body.frame = CGRect(x: 23, y: 23, width: 544, height: 564)
        body.gradient.colors = [UIColor.white.cgColor, UIColor(hex: 0xF7FAF9).cgColor,
                                UIColor(hex: 0xE8F0EF).cgColor, UIColor(hex: 0xC7D3D3).cgColor]
        body.layer.cornerRadius = 66
        body.layer.borderWidth = 2
        body.layer.borderColor = UIColor.white.withAlphaComponent(0.85).cgColor
        body.layer.shadow(UIColor(hex: 0x6E7B80), opacity: 0.23, radius: 15, y: 7)
        body.isUserInteractionEnabled = false
        addSubview(body)
        let prism = CALayer()
        prism.frame = CGRect(x: 5, y: 5, width: 534, height: 553)
        prism.cornerRadius = 61
        prism.borderColor = UIColor.white.withAlphaComponent(0.85).cgColor
        prism.borderWidth = 2
        body.layer.addSublayer(prism)
        let depth = CALayer()
        depth.frame = CGRect(x: 11, y: 11, width: 522, height: 542)
        depth.cornerRadius = 56
        depth.backgroundColor = UIColor(hex: 0x748788, alpha: 0.08).cgColor
        depth.borderColor = UIColor(hex: 0xACBDBB, alpha: 0.35).cgColor
        depth.borderWidth = 1
        body.layer.addSublayer(depth)
        plate.frame = CGRect(x: 41, y: 41, width: 508, height: 528)
        plate.gradient.colors = [UIColor(hex: 0xFFFFFF, alpha: 0.96).cgColor,
                                 UIColor(hex: 0xF8FAF8, alpha: 0.93).cgColor,
                                 UIColor(hex: 0xF1F5F2, alpha: 0.92).cgColor,
                                 UIColor(hex: 0xE1EAE8, alpha: 0.9).cgColor]
        plate.layer.cornerRadius = 50
        plate.layer.borderWidth = 1
        plate.layer.borderColor = UIColor.white.withAlphaComponent(0.66).cgColor
        plate.isUserInteractionEnabled = false
        addSubview(plate)
        silk("CODEX  /  MICRO  /  CRYSTAL", center: CGPoint(x: 69, y: 305), angle: -.pi / 2)
        silk("OPTICAL INPUT  /  PROJECT 2077", center: CGPoint(x: 522, y: 305), angle: .pi / 2)
        let brand = UIImageView(image: UIImage(named: "MicroCodex")?.withRenderingMode(.alwaysTemplate))
        brand.frame = CGRect(x: 248, y: 548, width: 13, height: 13)
        brand.tintColor = UIColor(hex: 0x606A70, alpha: 0.5)
        addSubview(brand)
        silk("OPENAI  CODEX", center: CGPoint(x: 303, y: 554.5), angle: 0)
    }

    private func silk(_ text: String, center: CGPoint, angle: CGFloat) {
        let label = UILabel(frame: CGRect(x: 0, y: 0, width: 240, height: 14))
        label.text = text
        label.font = .systemFont(ofSize: 8, weight: .semibold)
        label.textColor = UIColor(hex: 0x606A70, alpha: 0.45)
        label.textAlignment = .center
        label.center = center
        label.transform = CGAffineTransform(rotationAngle: angle)
        label.isAccessibilityElement = false
        addSubview(label)
    }

    @objc private func pageChanged() { onPage?(dots.currentPage) }
    @objc private func swiped(_ gesture: UISwipeGestureRecognizer) {
        onPage?(gesture.direction == .left ? 1 : 0)
    }
}

extension MicroDeviceView: UIGestureRecognizerDelegate {
    func gestureRecognizer(_ gestureRecognizer: UIGestureRecognizer, shouldReceive touch: UITouch) -> Bool {
        var node = touch.view
        while let view = node {
            if view is EncoderControl || view is JoystickControl { return false }
            node = view.superview
        }
        return true
    }
}
