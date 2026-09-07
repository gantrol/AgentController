namespace AgentController.Adapters.DeveloperTools;

public sealed record DotNetBuildDiagnosticOptions
{
    public string DotNetExecutable { get; init; } = "dotnet";

    public TimeSpan DefaultTimeout { get; init; } = TimeSpan.FromMinutes(5);

    public int MaxCapturedCharacters { get; init; } = 1_000_000;

    public long MaxSarifBytes { get; init; } = 16 * 1024 * 1024;

    internal void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(DotNetExecutable);

        var executableName = Path.GetFileName(DotNetExecutable);
        if (!executableName.Equals("dotnet", StringComparison.OrdinalIgnoreCase) &&
            !executableName.Equals(
                "dotnet.exe",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Only the dotnet executable is allowed.",
                nameof(DotNetExecutable));
        }

        if (!Path.IsPathFullyQualified(DotNetExecutable) &&
            !string.Equals(
                executableName,
                DotNetExecutable,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "A configured dotnet path must be absolute.",
                nameof(DotNetExecutable));
        }

        if (DefaultTimeout <= TimeSpan.Zero ||
            DefaultTimeout > TimeSpan.FromMinutes(30))
        {
            throw new ArgumentOutOfRangeException(nameof(DefaultTimeout));
        }

        if (MaxCapturedCharacters is < 4_096 or > 16_000_000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxCapturedCharacters));
        }

        if (MaxSarifBytes is < 4_096 or > 256 * 1024 * 1024)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxSarifBytes));
        }
    }
}
