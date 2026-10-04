import UIKit

final class ConnectionViewController: UITableViewController {
    private let store: MicroStore
    private let endpoint = UITextField()
    private let token = UITextField()
    private let pin = UITextField()

    init(store: MicroStore) { self.store = store; super.init(style: .insetGrouped) }
    required init?(coder: NSCoder) { fatalError("init(coder:) is unavailable") }

    override func viewDidLoad() {
        super.viewDidLoad()
        title = "连接"
        view.backgroundColor = MicroTheme.backdrop
        navigationItem.rightBarButtonItem = UIBarButtonItem(systemItem: .done, primaryAction: UIAction { [weak self] _ in
            self?.dismiss(animated: true)
        })
        tableView.keyboardDismissMode = .interactive
        for field in [endpoint, token, pin] {
            field.font = .preferredFont(forTextStyle: .body)
            field.autocapitalizationType = .none
            field.autocorrectionType = .no
            field.clearButtonMode = .whileEditing
            field.adjustsFontForContentSizeCategory = true
        }
        endpoint.placeholder = "wss://micro.local:8443/micro"
        endpoint.keyboardType = .URL
        endpoint.textContentType = .URL
        token.placeholder = "设备令牌"
        token.isSecureTextEntry = true
        pin.placeholder = "SHA-256（可选）"
        if let saved = HostKeychain.load() {
            endpoint.text = saved.endpoint; token.text = saved.token; pin.text = saved.certificateSHA256
        }
    }

    override func numberOfSections(in tableView: UITableView) -> Int { 4 }
    override func tableView(_ tableView: UITableView, numberOfRowsInSection section: Int) -> Int {
        section == 1 ? 3 : (section == 2 ? 2 : 1)
    }

    override func tableView(_ tableView: UITableView, titleForHeaderInSection section: Int) -> String? {
        section == 1 ? "Codex Micro Host" : nil
    }

    override func tableView(_ tableView: UITableView, cellForRowAt indexPath: IndexPath) -> UITableViewCell {
        let cell = UITableViewCell(style: .default, reuseIdentifier: nil)
        cell.backgroundColor = MicroTheme.paper
        if indexPath.section == 1 {
            let field = [endpoint, token, pin][indexPath.row]
            field.translatesAutoresizingMaskIntoConstraints = false
            cell.contentView.addSubview(field)
            NSLayoutConstraint.activate([
                field.leadingAnchor.constraint(equalTo: cell.contentView.leadingAnchor, constant: 16),
                field.trailingAnchor.constraint(equalTo: cell.contentView.trailingAnchor, constant: -16),
                field.topAnchor.constraint(equalTo: cell.contentView.topAnchor, constant: 12),
                field.bottomAnchor.constraint(equalTo: cell.contentView.bottomAnchor, constant: -12),
                field.heightAnchor.constraint(greaterThanOrEqualToConstant: 24)
            ])
            field.accessibilityLabel = ["Host 地址", "设备令牌", "证书 SHA-256"][indexPath.row]
        } else {
            var configuration = cell.defaultContentConfiguration()
            switch indexPath.section {
            case 0:
                configuration.text = "演示模式"
                configuration.image = UIImage(systemName: "circle.dotted")
                cell.accessoryType = store.isDemo ? .checkmark : .none
            case 2:
                configuration.text = indexPath.row == 0 ? "连接" : "查询待确认操作"
                configuration.textProperties.color = MicroTheme.accent
            default:
                configuration.text = "移除已保存的 Host"
                configuration.textProperties.color = .systemRed
            }
            cell.contentConfiguration = configuration
        }
        return cell
    }

    override func tableView(_ tableView: UITableView, didSelectRowAt indexPath: IndexPath) {
        tableView.deselectRow(at: indexPath, animated: true)
        switch indexPath.section {
        case 0: store.useDemo(); dismiss(animated: true)
        case 2 where indexPath.row == 1: store.queryPending(); dismiss(animated: true)
        case 2:
            let credentials = HostCredentials(endpoint: (endpoint.text ?? "").trimmingCharacters(in: .whitespacesAndNewlines),
                                              token: token.text ?? "", certificateSHA256: pin.text ?? "")
            do {
                _ = try credentials.validatedURL()
                try HostKeychain.save(credentials)
                store.useHost(credentials)
                dismiss(animated: true)
            } catch { showError(error.localizedDescription) }
        case 3:
            HostKeychain.remove()
            endpoint.text = nil; token.text = nil; pin.text = nil
            store.useDemo()
            tableView.reloadData()
        default: break
        }
    }

    private func showError(_ message: String) {
        let alert = UIAlertController(title: "连接未完成", message: message, preferredStyle: .alert)
        alert.addAction(UIAlertAction(title: "好", style: .default))
        present(alert, animated: true)
    }
}
