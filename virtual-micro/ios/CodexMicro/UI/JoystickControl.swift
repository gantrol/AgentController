import UIKit

final class JoystickControl: UIControl {
    private let cap = CAGradientLayer()
    private let arrows = CAShapeLayer()
    var onUp: (() -> Void)?
    var planEnabled = false { didSet { arrows.strokeColor = (planEnabled ? MicroTheme.accent : MicroTheme.muted).cgColor } }

    override init(frame: CGRect) {
        super.init(frame: frame)
        layer.addSublayer(arrows)
        layer.addSublayer(cap)
        cap.colors = [UIColor(hex: 0x4F5550).cgColor, UIColor(hex: 0x272C28).cgColor]
        cap.startPoint = CGPoint(x: 0.2, y: 0.1)
        cap.endPoint = CGPoint(x: 0.9, y: 1)
        cap.borderWidth = 2
        cap.borderColor = UIColor(hex: 0x222723).cgColor
        cap.shadow(.black, opacity: 0.25, radius: 2, y: 3)
        arrows.fillColor = UIColor.clear.cgColor
        arrows.strokeColor = MicroTheme.muted.cgColor
        arrows.lineWidth = 1.5
        arrows.lineCap = .round
        arrows.lineJoin = .round
        isAccessibilityElement = true
        accessibilityLabel = "摇杆向上：Plan"
        accessibilityTraits = .button
    }

    required init?(coder: NSCoder) { fatalError("init(coder:) is unavailable") }

    override func layoutSubviews() {
        super.layoutSubviews()
        cap.frame = CGRect(x: 14.5, y: 14.5, width: 67, height: 67)
        cap.cornerRadius = 33.5
        arrows.frame = bounds
        let path = UIBezierPath()
        for points in [[CGPoint(x: 42, y: 10), CGPoint(x: 48, y: 4), CGPoint(x: 54, y: 10)],
                       [CGPoint(x: 10, y: 42), CGPoint(x: 4, y: 48), CGPoint(x: 10, y: 54)],
                       [CGPoint(x: 86, y: 42), CGPoint(x: 92, y: 48), CGPoint(x: 86, y: 54)],
                       [CGPoint(x: 42, y: 86), CGPoint(x: 48, y: 92), CGPoint(x: 54, y: 86)]] {
            path.move(to: points[0]); path.addLine(to: points[1]); path.addLine(to: points[2])
        }
        arrows.path = path.cgPath
    }

    override func beginTracking(_ touch: UITouch, with event: UIEvent?) -> Bool { move(touch); return true }
    override func continueTracking(_ touch: UITouch, with event: UIEvent?) -> Bool { move(touch); return true }
    override func endTracking(_ touch: UITouch?, with event: UIEvent?) {
        cap.setAffineTransform(.identity)
        guard let point = touch?.location(in: self), bounds.contains(point), point.y < 42 else { return }
        UISelectionFeedbackGenerator().selectionChanged()
        onUp?()
    }
    override func cancelTracking(with event: UIEvent?) { cap.setAffineTransform(.identity) }
    override func accessibilityActivate() -> Bool { onUp?(); return true }

    private func move(_ touch: UITouch) {
        let point = touch.location(in: self)
        cap.setAffineTransform(CGAffineTransform(translationX: min(max((point.x - 48) / 5, -7), 7),
                                                y: min(max((point.y - 48) / 5, -7), 7)))
    }
}
