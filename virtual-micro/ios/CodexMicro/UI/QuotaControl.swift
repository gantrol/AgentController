import UIKit

final class QuotaControl: UIControl {
    private let face = CAGradientLayer()
    private let tracks = [CAShapeLayer(), CAShapeLayer()]
    private let rings = [CAShapeLayer(), CAShapeLayer()]
    private let leds = [CALayer(), CALayer(), CALayer()]
    private let primary = SevenSegmentView()
    private let secondary = SevenSegmentView()
    private let loading = CAShapeLayer()
    var onTap: (() -> Void)?
    var onLongPress: (() -> Void)?

    override init(frame: CGRect) {
        super.init(frame: frame)
        layer.addSublayer(face)
        face.colors = [UIColor(hex: 0x4C504D).cgColor, UIColor(hex: 0x202422).cgColor]
        face.startPoint = .zero
        face.endPoint = CGPoint(x: 1, y: 1)
        face.borderColor = UIColor(hex: 0x545953).cgColor
        face.borderWidth = 1.5
        face.shadow(.black, opacity: 0.3, radius: 2, y: 2)
        for (track, ring) in zip(tracks, rings) {
            for shape in [track, ring] {
                shape.fillColor = UIColor.clear.cgColor
                shape.lineWidth = 1.6
                shape.lineCap = .round
                layer.addSublayer(shape)
            }
            track.strokeColor = UIColor.white.withAlphaComponent(0.15).cgColor
            ring.strokeColor = UIColor(hex: 0x9EBDFF).cgColor
        }
        loading.fillColor = UIColor.clear.cgColor
        loading.strokeColor = MicroTheme.mint.cgColor
        loading.lineWidth = 1.4
        loading.lineCap = .round
        layer.addSublayer(loading)
        leds.forEach { layer.addSublayer($0) }
        addSubview(primary)
        addSubview(secondary)
        addTarget(self, action: #selector(tapped), for: .touchUpInside)
        addGestureRecognizer(UILongPressGestureRecognizer(target: self, action: #selector(held(_:))))
        isAccessibilityElement = true
        accessibilityLabel = "额度与快捷模型"
        accessibilityTraits = .button
    }

    required init?(coder: NSCoder) { fatalError("init(coder:) is unavailable") }

    func render(quota: MicroQuota?, connected: Bool, synced: Bool, busy: Bool, receipt: MicroReceipt?) {
        let values = [quota?.fiveHour, quota?.weekly]
        for index in 0..<2 {
            let value = values[index]
            let finite = value.flatMap { $0.isFinite ? $0 : nil }
            rings[index].strokeEnd = CGFloat(min(max(finite ?? 0, 0), 100) / 100)
            (index == 0 ? primary : secondary).text = finite.map { String(Int(min(max($0, 0), 100))) } ?? "--"
        }
        let activity: UIColor = busy ? MicroTheme.accent :
            (receipt?.status == .applied ? UIColor(hex: 0x74D9A0) :
                (receipt?.status == .unknown || receipt?.status == .rejected ? UIColor(hex: 0xFFD66E) : UIColor(hex: 0xB8B98B)))
        let colors = [synced ? MicroTheme.accent : UIColor(hex: 0xB8B98B),
                      connected ? MicroTheme.accent : UIColor(hex: 0xB8B98B), activity]
        for (led, color) in zip(leds, colors) {
            led.backgroundColor = color.cgColor
            led.shadow(color, opacity: connected ? 0.65 : 0, radius: 3)
        }
        loading.isHidden = !busy
        if busy && loading.animation(forKey: "pending") == nil && !UIAccessibility.isReduceMotionEnabled {
            let animation = CABasicAnimation(keyPath: "transform.rotation.z")
            animation.toValue = 2 * CGFloat.pi
            animation.duration = 1.3
            animation.repeatCount = .infinity
            loading.add(animation, forKey: "pending")
        } else if !busy { loading.removeAnimation(forKey: "pending") }
        accessibilityValue = "5 小时 \(primary.text)%，每周 \(secondary.text)%"
    }

    override func layoutSubviews() {
        super.layoutSubviews()
        CATransaction.begin(); CATransaction.setDisableActions(true)
        face.frame = CGRect(x: 33, y: 19, width: 58, height: 58)
        face.cornerRadius = 29
        for index in 0..<2 {
            let radius = CGFloat(index == 0 ? 24 : 20.5)
            let path = UIBezierPath(arcCenter: CGPoint(x: 62, y: 48), radius: radius,
                                    startAngle: -.pi / 2, endAngle: .pi * 1.5, clockwise: true).cgPath
            tracks[index].frame = bounds; tracks[index].path = path
            rings[index].frame = bounds; rings[index].path = path
        }
        for index in 0..<3 {
            leds[index].frame = CGRect(x: 10, y: 33 + CGFloat(index) * 11, width: 7, height: 7)
            leds[index].cornerRadius = 3.5
        }
        primary.frame = CGRect(x: 46, y: 33, width: 20, height: 16)
        secondary.frame = CGRect(x: 61, y: 49, width: 18, height: 14)
        loading.frame = CGRect(x: 35, y: 21, width: 54, height: 54)
        loading.path = UIBezierPath(arcCenter: CGPoint(x: 27, y: 27), radius: 26,
                                   startAngle: -.pi / 2, endAngle: 0, clockwise: true).cgPath
        CATransaction.commit()
    }

    @objc private func tapped() { onTap?() }
    @objc private func held(_ gesture: UILongPressGestureRecognizer) {
        if gesture.state == .began { onLongPress?() }
    }
}

private final class SevenSegmentView: UIView {
    var text = "--" { didSet { setNeedsDisplay() } }
    private let masks = [0x3F, 0x06, 0x5B, 0x4F, 0x66, 0x6D, 0x7D, 0x07, 0x7F, 0x6F]

    override init(frame: CGRect) { super.init(frame: frame); isOpaque = false; isUserInteractionEnabled = false }
    required init?(coder: NSCoder) { fatalError("init(coder:) is unavailable") }

    override func draw(_ rect: CGRect) {
        guard let context = UIGraphicsGetCurrentContext(), !text.isEmpty else { return }
        let width = CGFloat(text.count) * 9 - 1.5
        let scale = min(bounds.width / width, bounds.height / 14)
        context.translateBy(x: (bounds.width - width * scale) / 2, y: (bounds.height - 14 * scale) / 2)
        context.scaleBy(x: scale, y: scale)
        UIColor(hex: 0xF7FAFF).setFill()
        let segments = [CGRect(x: 1.3, y: 0, width: 4.9, height: 1.4),
                        CGRect(x: 6.1, y: 1.3, width: 1.4, height: 4.9),
                        CGRect(x: 6.1, y: 7.7, width: 1.4, height: 4.9),
                        CGRect(x: 1.3, y: 12.6, width: 4.9, height: 1.4),
                        CGRect(x: 0, y: 7.7, width: 1.4, height: 4.9),
                        CGRect(x: 0, y: 1.3, width: 1.4, height: 4.9),
                        CGRect(x: 1.3, y: 6.3, width: 4.9, height: 1.4)]
        for character in text {
            let mask = character.wholeNumberValue.map { masks[$0] } ?? 0x40
            for (index, segment) in segments.enumerated() where (mask & (1 << index)) != 0 {
                UIBezierPath(roundedRect: segment, cornerRadius: 0.5).fill()
            }
            context.translateBy(x: 9, y: 0)
        }
    }
}
