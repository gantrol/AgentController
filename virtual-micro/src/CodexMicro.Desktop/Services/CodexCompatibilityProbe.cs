using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace CodexMicro.Desktop.Services;

internal enum CodexCompatibilityDisposition
{
    Incompatible,
    Unreviewed,
    Changed,
    Reviewed,
}

internal sealed record CodexCompatibilityResult(
    CodexCompatibilityDisposition Disposition,
    string Build,
    string Fingerprint,
    string Detail,
    string? PackageRoot)
{
    public bool IsCompatible =>
        Disposition is not CodexCompatibilityDisposition.Incompatible;

    public bool IsReviewed =>
        Disposition is CodexCompatibilityDisposition.Reviewed;
}

internal sealed class CodexCompatibilityProbe
{
    private const string PackagesRegistryPath =
        @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages";

    private static readonly IReadOnlyDictionary<string, BuildManifest> Manifests =
        new Dictionary<string, BuildManifest>(StringComparer.OrdinalIgnoreCase)
        {
            ["26.715.4045.0"] = new(
                "26.715.4045.0",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [".vite/build/codex-micro-service-D1zOebWq.js"] =
                        "5caa5193214fcd29ef0e9fe66ed641d9646a114ea9122bdc1fc2bf78491c2746",
                    ["webview/assets/codex-micro-layout-upYsAgUW.js"] =
                        "2ae8829dfafbf0fb9262e270421d8d6f6279d02dec5821b5c8af075a2898ed50",
                    ["webview/assets/codex-micro-bridge-BzpuGF_V.js"] =
                        "dc09025adabce2c654fdeabf36ddb728ad293eba7040efa137b7a2b20dfa1d1b",
                    ["webview/assets/codex-micro-settings-dLpS1GVV.js"] =
                        "f75c5fc01113d27f732d00bb29a3474521e098b43dde48adc8c70075847ea698",
                    ["webview/assets/codex-micro-slot-signals-nIAsNNU3.js"] =
                        "7041d68667af1ed519961bcb1ec61bc5a514f1bec1f8365b97ce740c522b542c",
                    ["node_modules/@worklouder/device-kit-oai/dist/rpc_api_oai/rpc_api_oai.js"] =
                        "80815366885246cd9644e13b770f38c7f9c0587db13cc8979310571ba0fa029a",
                    ["node_modules/@worklouder/device-kit-oai/node_modules/@worklouder/wl-device-kit/dist/index.js"] =
                        "f44d8d09e10a4608bf37f2860cd4807c3be9b0242f91d8258df540e277cd7548",
                }),
            ["26.715.3651.0"] = new(
                "26.715.3651.0",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [".vite/build/codex-micro-service-DprRobf3.js"] =
                        "233e215d0e3a8aded974e3292abc0bd80d7b69034cb2689bfdb69652274acf35",
                    ["webview/assets/codex-micro-layout-C6c1ekuT.js"] =
                        "61429d468ee9d065558818a1d036437530c4f570a3990645dbd5f556da87b5e2",
                    ["webview/assets/codex-micro-bridge-D6CTRQ3Q.js"] =
                        "fd34130f011b77e8033e8161a251a93979d64529b98e93e06684963752ce7ea3",
                    ["webview/assets/codex-micro-settings-BASRZFa7.js"] =
                        "3645db1eadf8c5a6cceaddeb32d59903ec2c7e18059528ab04f92ce7c7890568",
                    ["webview/assets/codex-micro-slot-signals-BW2AHYmp.js"] =
                        "8d7095e33f438b5db8a0a622014fd8367fb49f5a87cf0bfad2f909781e0852c1",
                    ["node_modules/@worklouder/device-kit-oai/dist/rpc_api_oai/rpc_api_oai.js"] =
                        "80815366885246cd9644e13b770f38c7f9c0587db13cc8979310571ba0fa029a",
                    ["node_modules/@worklouder/device-kit-oai/node_modules/@worklouder/wl-device-kit/dist/index.js"] =
                        "f44d8d09e10a4608bf37f2860cd4807c3be9b0242f91d8258df540e277cd7548",
                }),
            ["26.707.12708.0"] = new(
                "26.707.12708.0",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [".vite/build/codex-micro-service-CR6sUcZG.js"] =
                        "0bb261e3eed89ff69384754ab67df49c9f10dbd2fa567104c5859f43d026c911",
                    ["webview/assets/codex-micro-slot-signals-SFcKxWqG.js"] =
                        "e5f0084a27fc0e908c4514a5d3bd0a90dba3f953a48521fb4ae2a43b1e5b28bb",
                    ["webview/assets/codex-micro-bridge-D90_rd6W.js"] =
                        "df6063eb17046594e769050c6bbb3ed169b1352bbd5867fffb4d1f8c724f3e93",
                    ["node_modules/@worklouder/device-kit-oai/dist/rpc_api_oai/rpc_api_oai.js"] =
                        "80815366885246cd9644e13b770f38c7f9c0587db13cc8979310571ba0fa029a",
                    ["node_modules/@worklouder/device-kit-oai/node_modules/@worklouder/wl-device-kit/dist/index.js"] =
                        "f44d8d09e10a4608bf37f2860cd4807c3be9b0242f91d8258df540e277cd7548",
                }),
        };

    public Task<CodexCompatibilityResult> ProbeAsync(
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Probe(cancellationToken), cancellationToken);

    private static CodexCompatibilityResult Probe(
        CancellationToken cancellationToken)
    {
        var package = FindInstalledPackage();
        if (package is null)
        {
            return Incompatible(
                "not-installed",
                "Codex MSIX registration was not found for the current user.");
        }

        var build = GetBuild(package.Value.PackageId);
        var asarPath = Path.Combine(
            package.Value.PackageRoot,
            "app",
            "resources",
            "app.asar");
        if (!File.Exists(asarPath))
        {
            return new CodexCompatibilityResult(
                CodexCompatibilityDisposition.Unreviewed,
                build,
                "unverified",
                "Codex package layout changed and app.asar was not found; continuing in advisory mode.",
                package.Value.PackageRoot);
        }

        if (!Manifests.TryGetValue(build, out var manifest))
        {
            try
            {
                using var archive = new AsarArchiveReader(asarPath);
            }
            catch (Exception exception) when (
                exception is IOException or
                    UnauthorizedAccessException or
                    InvalidDataException or
                    JsonException)
            {
                return new CodexCompatibilityResult(
                    CodexCompatibilityDisposition.Unreviewed,
                    build,
                    "unverified",
                    $"Compatibility fingerprint could not be inspected; continuing in advisory mode: {exception.Message}",
                    package.Value.PackageRoot);
            }

            return new CodexCompatibilityResult(
                CodexCompatibilityDisposition.Unreviewed,
                build,
                "unreviewed",
                "This Codex build is newer than the reviewed Micro manifests; continuing in compatibility mode.",
                package.Value.PackageRoot);
        }

        try
        {
            using var archive = new AsarArchiveReader(asarPath);
            var observed = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var expected in manifest.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var hash = archive.ComputeSha256(expected.Key);
                observed[expected.Key] = hash;
                if (!hash.Equals(expected.Value, StringComparison.OrdinalIgnoreCase))
                {
                    return new CodexCompatibilityResult(
                        CodexCompatibilityDisposition.Changed,
                        build,
                        "changed",
                        $"Reviewed Codex Micro file changed; continuing in advisory mode: {expected.Key}",
                        package.Value.PackageRoot);
                }
            }

            var canonical = string.Join(
                "\n",
                observed.Select(item => $"{item.Key}={item.Value}"));
            var fingerprint = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
                .ToLowerInvariant();
            return new CodexCompatibilityResult(
                CodexCompatibilityDisposition.Reviewed,
                build,
                fingerprint,
                "Codex build and reviewed Micro bundle hashes match.",
                package.Value.PackageRoot);
        }
        catch (Exception exception) when (
            exception is IOException or
                UnauthorizedAccessException or
                InvalidDataException or
                JsonException)
        {
            return new CodexCompatibilityResult(
                CodexCompatibilityDisposition.Changed,
                build,
                "unverified",
                $"Compatibility fingerprint could not be verified; continuing in advisory mode: {exception.Message}",
                package.Value.PackageRoot);
        }
    }

    private static (string PackageId, string PackageRoot)? FindInstalledPackage()
    {
        using var packages = Registry.CurrentUser.OpenSubKey(PackagesRegistryPath);
        if (packages is null)
        {
            return null;
        }

        return packages
            .GetSubKeyNames()
            .Where(name => name.StartsWith(
                "OpenAI.Codex_",
                StringComparison.OrdinalIgnoreCase))
            .Select(name =>
            {
                using var package = packages.OpenSubKey(name);
                var root = package?.GetValue("PackageRootFolder") as string;
                return (PackageId: name, PackageRoot: root);
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.PackageRoot))
            .OrderByDescending(item => ParseVersion(GetBuild(item.PackageId)))
            .Select(item => (item.PackageId, item.PackageRoot!))
            .Cast<(string PackageId, string PackageRoot)?>()
            .FirstOrDefault();
    }

    private static string GetBuild(string packageId)
    {
        var parts = packageId.Split('_');
        return parts.Length > 1 ? parts[1] : "unknown";
    }

    private static Version ParseVersion(string value) =>
        Version.TryParse(value, out var version) ? version : new Version();

    private static CodexCompatibilityResult Incompatible(
        string build,
        string detail,
        string? packageRoot = null) => new(
            CodexCompatibilityDisposition.Incompatible,
            build,
            "unverified",
            detail,
            packageRoot);

    private sealed record BuildManifest(
        string Build,
        IReadOnlyDictionary<string, string> Files);
}

internal sealed class AsarArchiveReader : IDisposable
{
    private readonly FileStream _stream;
    private readonly JsonDocument _header;
    private readonly long _contentOffset;

    public AsarArchiveReader(string path)
    {
        _stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        Span<byte> prefix = stackalloc byte[8];
        _stream.ReadExactly(prefix);
        var headerSize = BitConverter.ToUInt32(prefix[4..]);
        if (headerSize is < 8 or > 32 * 1024 * 1024)
        {
            throw new InvalidDataException("ASAR header size is invalid.");
        }

        var header = new byte[headerSize];
        _stream.ReadExactly(header);
        var jsonLength = BitConverter.ToUInt32(header.AsSpan(4));
        if (jsonLength > header.Length - 8)
        {
            throw new InvalidDataException("ASAR JSON header is truncated.");
        }

        _header = JsonDocument.Parse(header.AsMemory(8, checked((int)jsonLength)));
        _contentOffset = 8L + headerSize;
    }

    public string ComputeSha256(string archivePath)
    {
        var node = _header.RootElement;
        foreach (var part in archivePath.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (
                !node.TryGetProperty("files", out var files) ||
                !files.TryGetProperty(part, out node))
            {
                throw new InvalidDataException(
                    $"ASAR entry is missing: {archivePath}");
            }
        }

        if (
            !node.TryGetProperty("size", out var sizeElement) ||
            !sizeElement.TryGetInt32(out var size) ||
            size < 0 ||
            !node.TryGetProperty("offset", out var offsetElement) ||
            !long.TryParse(offsetElement.GetString(), out var offset) ||
            offset < 0)
        {
            throw new InvalidDataException(
                $"ASAR entry metadata is invalid: {archivePath}");
        }

        var bytes = new byte[size];
        _stream.Position = checked(_contentOffset + offset);
        _stream.ReadExactly(bytes);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    public void Dispose()
    {
        _header.Dispose();
        _stream.Dispose();
    }
}
