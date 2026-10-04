import Foundation
import Security
import CryptoKit

struct HostCredentials: Codable {
    var endpoint: String
    var token: String
    var certificateSHA256: String

    func validatedURL() throws -> URL {
        guard let url = URL(string: endpoint), url.scheme == "wss", url.host != nil,
              url.user == nil, url.password == nil, url.query == nil, url.fragment == nil,
              !token.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty,
              token.rangeOfCharacter(from: .controlCharacters) == nil else {
            throw MicroError.message("请输入 WSS 地址和设备令牌")
        }
        let pin = normalizedPin
        guard pin.isEmpty || (pin.count == 64 && pin.allSatisfy({ $0.isHexDigit })) else {
            throw MicroError.message("证书指纹须为 64 位 SHA-256")
        }
        return url
    }

    var normalizedPin: String {
        certificateSHA256.replacingOccurrences(of: ":", with: "")
            .replacingOccurrences(of: " ", with: "").lowercased()
    }
}

enum HostKeychain {
    private static let service = "CodexMicro.iOS.host"
    static func load() -> HostCredentials? {
        let query: [String: Any] = [kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service, kSecAttrAccount as String: "host",
            kSecReturnData as String: true, kSecMatchLimit as String: kSecMatchLimitOne]
        var result: CFTypeRef?
        guard SecItemCopyMatching(query as CFDictionary, &result) == errSecSuccess,
              let data = result as? Data else { return nil }
        return try? JSONDecoder().decode(HostCredentials.self, from: data)
    }

    static func save(_ credentials: HostCredentials) throws {
        let data = try JSONEncoder().encode(credentials)
        let query: [String: Any] = [kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service, kSecAttrAccount as String: "host"]
        let changes: [String: Any] = [kSecValueData as String: data,
            kSecAttrAccessible as String: kSecAttrAccessibleWhenUnlockedThisDeviceOnly]
        let status = SecItemUpdate(query as CFDictionary, changes as CFDictionary)
        if status == errSecItemNotFound {
            let entry = query.merging(changes) { _, new in new }
            guard SecItemAdd(entry as CFDictionary, nil) == errSecSuccess else {
                throw MicroError.message("无法保存设备凭据")
            }
        } else if status != errSecSuccess { throw MicroError.message("无法更新设备凭据") }
    }

    static func remove() {
        SecItemDelete([kSecClass as String: kSecClassGenericPassword,
                       kSecAttrService as String: service,
                       kSecAttrAccount as String: "host"] as CFDictionary)
    }
}

final class HostTrustDelegate: NSObject, URLSessionTaskDelegate {
    private let host: String
    private let pin: String
    init(host: String, pin: String) { self.host = host; self.pin = pin }

    func urlSession(_ session: URLSession, task: URLSessionTask,
                    willPerformHTTPRedirection response: HTTPURLResponse, newRequest request: URLRequest,
                    completionHandler: @escaping (URLRequest?) -> Void) {
        completionHandler(nil)
    }

    func urlSession(_ session: URLSession, didReceive challenge: URLAuthenticationChallenge,
                    completionHandler: @escaping (URLSession.AuthChallengeDisposition, URLCredential?) -> Void) {
        guard challenge.protectionSpace.authenticationMethod == NSURLAuthenticationMethodServerTrust else {
            completionHandler(.performDefaultHandling, nil); return
        }
        guard challenge.protectionSpace.host.caseInsensitiveCompare(host) == .orderedSame else {
            completionHandler(.cancelAuthenticationChallenge, nil); return
        }
        guard !pin.isEmpty else { completionHandler(.performDefaultHandling, nil); return }
        guard let trust = challenge.protectionSpace.serverTrust,
              let chain = SecTrustCopyCertificateChain(trust) as? [SecCertificate], let leaf = chain.first else {
            completionHandler(.cancelAuthenticationChallenge, nil); return
        }
        let digest = SHA256.hash(data: SecCertificateCopyData(leaf) as Data)
            .map { String(format: "%02x", $0) }.joined()
        guard digest == pin else { completionHandler(.cancelAuthenticationChallenge, nil); return }
        // An explicitly entered certificate pin is the trust anchor for a private LAN host.
        completionHandler(.useCredential, URLCredential(trust: trust))
    }
}
