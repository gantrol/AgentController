import UIKit

enum MicroTheme {
    static let designSize = CGSize(width: 590, height: 610)
    static let ink = UIColor(hex: 0x2F3438)
    static let muted = UIColor(hex: 0x737C82)
    static let paper = UIColor(hex: 0xF7F8F6)
    static let mint = UIColor(hex: 0x98EDC1)
    static let accent = UIColor(hex: 0x7185EA)
    static let backdrop = UIColor(hex: 0xE5ECE8)

    static func status(_ status: ThreadStatus) -> UIColor {
        switch status {
        case .idle: return UIColor(hex: 0x8F86B8)
        case .running: return UIColor(hex: 0x304FFE)
        case .completed: return UIColor(hex: 0x00FF4C)
        case .waiting: return UIColor(hex: 0xFF6D00)
        case .error: return UIColor(hex: 0xFF0033)
        }
    }

    static func statusName(_ status: ThreadStatus) -> String {
        switch status {
        case .idle: return "空闲"
        case .running: return "运行中"
        case .completed: return "完成未读"
        case .waiting: return "等待输入"
        case .error: return "错误"
        }
    }
}

extension UIColor {
    convenience init(hex: UInt32, alpha: CGFloat = 1) {
        self.init(red: CGFloat((hex >> 16) & 255) / 255,
                  green: CGFloat((hex >> 8) & 255) / 255,
                  blue: CGFloat(hex & 255) / 255, alpha: alpha)
    }
}

extension CALayer {
    func shadow(_ color: UIColor, opacity: Float, radius: CGFloat, y: CGFloat = 0) {
        shadowColor = color.cgColor
        shadowOpacity = opacity
        shadowRadius = radius
        shadowOffset = CGSize(width: 0, height: y)
    }
}

final class GradientView: UIView {
    override class var layerClass: AnyClass { CAGradientLayer.self }
    var gradient: CAGradientLayer { layer as! CAGradientLayer }
}
