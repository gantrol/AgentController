import UIKit

final class KeycapControl: UIControl {
    private let cap = CALayer()
    private let side = CALayer()
    private let wash = CALayer()
    private let well = CALayer()
    private let wellRim = CAGradientLayer()
    private let rimMask = CAShapeLayer()
    private let dot = CALayer()
    private let seam = CALayer()
    private let symbol = UIImageView()
    private let haptic = UISelectionFeedbackGenerator()
    var onTap: (() -> Void)?
    var contextMenu: (() -> UIMenu?)?
    private(set) var glowColor: UIColor = .clear
    private(set) var glowOpacity: Float = 0

    override init(frame: CGRect) {
        super.init(frame: frame)
        isAccessibilityElement = true
        accessibilityTraits = .button
        layer.addSublayer(side)
        layer.addSublayer(cap)
        cap.addSublayer(wash)
        cap.addSublayer(well)
        cap.addSublayer(wellRim)
        cap.addSublayer(dot)
        layer.addSublayer(seam)
        wellRim.mask = rimMask
        wellRim.colors = [UIColor(hex: 0x747B77, alpha: 0.16).cgColor,
                          UIColor.white.withAlphaComponent(0.9).cgColor]
        wellRim.startPoint = .zero
        wellRim.endPoint = CGPoint(x: 1, y: 1)
        rimMask.fillColor = UIColor.clear.cgColor
        rimMask.strokeColor = UIColor.black.cgColor
        side.backgroundColor = UIColor(hex: 0xADB4B0).cgColor
        side.shadow(UIColor(hex: 0x363C39), opacity: 0.20, radius: 5, y: 4)
        cap.borderWidth = 1.4
        cap.borderColor = UIColor.white.withAlphaComponent(0.94).cgColor
        seam.borderWidth = 1.5
        seam.backgroundColor = UIColor.clear.cgColor
        symbol.contentMode = .scaleAspectFit
        symbol.tintColor = UIColor(hex: 0x34413C)
        symbol.isUserInteractionEnabled = false
        addSubview(symbol)
        addTarget(self, action: #selector(tapped), for: .touchUpInside)
        addInteraction(UIContextMenuInteraction(delegate: self))
    }

    required init?(coder: NSCoder) { fatalError("init(coder:) is unavailable") }

    func configure(thread: MicroThread?, selected: Bool, stale: Bool) {
        symbol.isHidden = true
        let status = thread?.status ?? .idle
        let active = thread != nil && status != .idle && !stale
        let color = MicroTheme.status(status)
        let mintSelection = selected && !active && !stale
        CATransaction.begin()
        CATransaction.setDisableActions(true)
        cap.backgroundColor = MicroTheme.paper.cgColor
        wash.backgroundColor = color.cgColor
        wash.opacity = active ? (selected ? 0.20 : 0.08) : 0
        well.backgroundColor = (active ? color.withAlphaComponent(selected ? 0.48 : 0.32)
                               : MicroTheme.paper).cgColor
        dot.backgroundColor = (mintSelection ? MicroTheme.mint : UIColor(hex: 0x8F86B8)).cgColor
        dot.opacity = thread == nil ? 0.20 : (active ? 0.75 : 0.60)
        seam.borderColor = (selected ? MicroTheme.mint : .clear).cgColor
        seam.shadow(MicroTheme.mint, opacity: selected ? 0.65 : 0, radius: 3)
        well.borderWidth = mintSelection ? 2 : 0
        well.borderColor = MicroTheme.mint.withAlphaComponent(0.60).cgColor
        well.shadow(mintSelection ? MicroTheme.mint : color,
                    opacity: active ? 0.48 : (mintSelection ? 0.25 : 0), radius: 5)
        glowColor = active ? color : (mintSelection ? MicroTheme.mint : .clear)
        glowOpacity = active ? (selected ? 0.48 : 0.26) : (mintSelection ? 0.15 : 0)
        CATransaction.commit()
        isEnabled = thread != nil
        accessibilityLabel = thread?.title ?? "空键位"
        accessibilityValue = thread == nil ? nil : (stale ? "状态已过期" : MicroTheme.statusName(status))
        accessibilityTraits = selected ? [.button, .selected] : .button
        setNeedsLayout()
    }

    func configure(symbol name: String, label: String, enabled: Bool, active: Bool = false) {
        isEnabled = enabled
        accessibilityLabel = label
        accessibilityTraits = enabled ? .button : [.button, .notEnabled]
        symbol.isHidden = false
        let assets = ["bolt": "MicroFast", "checkmark.circle": "MicroApprove", "xmark.circle": "MicroReject",
                      "arrow.triangle.branch": "MicroFork", "mic": "MicroMicrophone", "terminal": "MicroCodex"]
        symbol.image = assets[name].flatMap { UIImage(named: $0)?.withRenderingMode(.alwaysTemplate) }
            ?? UIImage(systemName: name, withConfiguration: UIImage.SymbolConfiguration(pointSize: 26, weight: .medium))
        symbol.alpha = enabled ? 1 : 0.26
        symbol.tintColor = active ? MicroTheme.accent : UIColor(hex: 0x34413C)
        cap.backgroundColor = MicroTheme.paper.cgColor
        wash.opacity = 0
        well.backgroundColor = MicroTheme.paper.cgColor
        well.borderWidth = 0
        well.shadowOpacity = 0
        dot.opacity = 0
        seam.borderColor = UIColor.clear.cgColor
        seam.shadowOpacity = 0
        glowOpacity = 0
        accessibilityValue = active ? "已开启" : nil
        setNeedsLayout()
    }

    override func layoutSubviews() {
        super.layoutSubviews()
        CATransaction.begin()
        CATransaction.setDisableActions(true)
        let face = bounds.insetBy(dx: 1, dy: 1)
        side.frame = face.offsetBy(dx: 0, dy: 2)
        cap.frame = face.offsetBy(dx: 0, dy: -0.5)
        side.cornerRadius = 14
        cap.cornerRadius = 14
        side.shadowPath = UIBezierPath(roundedRect: side.bounds, cornerRadius: 14).cgPath
        wash.frame = cap.bounds.insetBy(dx: 1, dy: 1)
        wash.cornerRadius = 13
        let wellRect = cap.bounds.insetBy(dx: 9, dy: 9)
        well.frame = wellRect
        well.cornerRadius = wellRect.height / 2
        wellRim.frame = wellRect
        rimMask.frame = wellRim.bounds
        rimMask.path = UIBezierPath(roundedRect: wellRim.bounds.insetBy(dx: 0.8, dy: 0.8),
                                    cornerRadius: wellRect.height / 2).cgPath
        rimMask.lineWidth = 1.6
        dot.frame = CGRect(x: cap.bounds.midX - 9, y: cap.bounds.midY - 9, width: 18, height: 18)
        dot.cornerRadius = 9
        seam.frame = face.insetBy(dx: 1, dy: 1)
        seam.cornerRadius = 13
        symbol.frame = CGRect(x: bounds.midX - 14, y: bounds.midY - 14, width: 28, height: 28)
        CATransaction.commit()
    }

    override var isHighlighted: Bool {
        didSet {
            let pressed = isHighlighted
            UIView.animate(withDuration: UIAccessibility.isReduceMotionEnabled ? 0 : 0.10) {
                self.transform = CGAffineTransform(translationX: 0, y: pressed ? 1.5 : 0)
            }
        }
    }

    @objc private func tapped() { haptic.selectionChanged(); onTap?() }
}

extension KeycapControl: UIContextMenuInteractionDelegate {
    func contextMenuInteraction(_ interaction: UIContextMenuInteraction,
                                configurationForMenuAtLocation location: CGPoint) -> UIContextMenuConfiguration? {
        guard let menu = contextMenu?() else { return nil }
        return UIContextMenuConfiguration(identifier: nil, previewProvider: nil) { _ in menu }
    }
}
