import UIKit

final class EncoderControl: UIControl {
    private let face = CAGradientLayer()
    private let side = CALayer()
    private let marker = CALayer()
    private var lastAngle: CGFloat = 0
    private var accumulated: CGFloat = 0
    private var rotation: CGFloat = -0.25
    private let haptic = UISelectionFeedbackGenerator()
    var onStep: ((Int) -> Void)?
    var onTap: (() -> Void)?
    var onLongPress: (() -> Void)?

    override init(frame: CGRect) {
        super.init(frame: frame)
        layer.addSublayer(side)
        layer.addSublayer(face)
        face.addSublayer(marker)
        side.backgroundColor = UIColor(hex: 0x8E9DA3, alpha: 0.65).cgColor
        face.colors = [UIColor.white.cgColor, UIColor(hex: 0xE8F1F3).cgColor, UIColor(hex: 0xD9E5E8).cgColor]
        face.startPoint = .zero
        face.endPoint = CGPoint(x: 1, y: 1)
        face.borderWidth = 1
        face.borderColor = UIColor.white.withAlphaComponent(0.9).cgColor
        face.shadow(UIColor(hex: 0x657C83), opacity: 0.25, radius: 3, y: 3)
        marker.backgroundColor = UIColor(hex: 0x738187).cgColor
        marker.cornerRadius = 3.5
        let pan = UIPanGestureRecognizer(target: self, action: #selector(panned(_:)))
        let tap = UITapGestureRecognizer(target: self, action: #selector(tapped))
        let hold = UILongPressGestureRecognizer(target: self, action: #selector(held(_:)))
        tap.require(toFail: pan)
        tap.require(toFail: hold)
        addGestureRecognizer(pan)
        addGestureRecognizer(tap)
        addGestureRecognizer(hold)
        isAccessibilityElement = true
        accessibilityLabel = "推理强度"
        accessibilityTraits = [.adjustable, .button]
    }

    required init?(coder: NSCoder) { fatalError("init(coder:) is unavailable") }

    override func layoutSubviews() {
        super.layoutSubviews()
        CATransaction.begin(); CATransaction.setDisableActions(true)
        side.frame = bounds.insetBy(dx: 5, dy: 6).offsetBy(dx: 0, dy: 5)
        side.cornerRadius = side.bounds.width / 2
        face.setAffineTransform(.identity)
        face.frame = bounds.insetBy(dx: 3, dy: 4).offsetBy(dx: 0, dy: -3)
        face.cornerRadius = face.bounds.width / 2
        marker.frame = CGRect(x: face.bounds.midX - 3.5, y: 13, width: 7, height: 31)
        CATransaction.commit()
        applyRotation()
    }

    func cancelInteraction() { accumulated = 0 }

    @objc private func panned(_ gesture: UIPanGestureRecognizer) {
        let point = gesture.location(in: self)
        let angle = atan2(point.y - bounds.midY, point.x - bounds.midX)
        switch gesture.state {
        case .began: lastAngle = angle; accumulated = 0; haptic.prepare()
        case .changed:
            var delta = angle - lastAngle
            if delta > .pi { delta -= 2 * .pi }
            if delta < -.pi { delta += 2 * .pi }
            lastAngle = angle
            rotation += delta
            accumulated += delta
            applyRotation()
            let steps = Int(accumulated / (.pi / 7))
            if steps != 0 {
                accumulated -= CGFloat(steps) * (.pi / 7)
                haptic.selectionChanged()
                onStep?(steps)
            }
        case .ended, .cancelled, .failed: accumulated = 0
        default: break
        }
    }

    private func applyRotation() {
        CATransaction.begin(); CATransaction.setDisableActions(true)
        face.setAffineTransform(CGAffineTransform(rotationAngle: rotation))
        CATransaction.commit()
    }

    @objc private func tapped() { onTap?() }
    @objc private func held(_ gesture: UILongPressGestureRecognizer) {
        if gesture.state == .began { onLongPress?() }
    }
    override func accessibilityIncrement() { onStep?(1) }
    override func accessibilityDecrement() { onStep?(-1) }
    override func accessibilityActivate() -> Bool { onTap?(); return true }
}
