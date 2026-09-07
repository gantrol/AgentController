namespace AgentController.Application.Diagnostics;

public enum DeveloperDiagnosticSeverity
{
    Information,
    Warning,
    Error,
}

public enum DeveloperDiagnosticRunStatus
{
    Succeeded,
    Failed,
    Cancelled,
    TimedOut,
    ToolUnavailable,
    Misconfigured,
}

public sealed record DeveloperDiagnosticLocation(
    string? FilePath,
    int? StartLine = null,
    int? StartColumn = null,
    int? EndLine = null,
    int? EndColumn = null,
    bool IsWithinWorkspace = false);

public sealed record DeveloperDiagnosticEvidence(
    string ProviderId,
    string? ToolVersion,
    string BuildId,
    string? ArtifactPath = null);

public sealed record DeveloperDiagnosticObservation(
    DeveloperDiagnosticSeverity Severity,
    string Code,
    string Message,
    DeveloperDiagnosticLocation Location,
    string? Project,
    DeveloperDiagnosticEvidence Evidence,
    string? CascadesFromFingerprint = null);

public sealed record DeveloperDiagnostic(
    string Fingerprint,
    DeveloperDiagnosticSeverity Severity,
    string Code,
    string Message,
    DeveloperDiagnosticLocation Location,
    string? Project,
    int OccurrenceCount,
    IReadOnlyList<DeveloperDiagnosticEvidence> Evidence,
    string? CascadesFromFingerprint = null);

public sealed record DeveloperPerformanceMeasurement(
    string Name,
    TimeSpan Duration,
    string ProviderId,
    string BuildId,
    string? ArtifactPath = null);

public sealed record DeveloperDiagnosticContext(
    string WorkspaceRoot,
    string TargetPath,
    string Configuration,
    long? DocumentVersion = null);

public sealed record DeveloperDiagnosticRun(
    string ProviderId,
    string BuildId,
    DeveloperDiagnosticRunStatus Status,
    DeveloperDiagnosticContext Context,
    DateTimeOffset StartedAt,
    TimeSpan Elapsed,
    IReadOnlyList<DeveloperDiagnostic> Diagnostics,
    IReadOnlyList<DeveloperPerformanceMeasurement> Performance,
    int? ExitCode = null,
    string? FailureCode = null,
    bool OutputTruncated = false,
    string? StandardOutput = null,
    string? StandardError = null);

public sealed record DeveloperDiagnosticRequest(
    string WorkspaceRoot,
    string TargetPath,
    string Configuration = "Debug",
    bool NoRestore = true,
    bool CaptureBuildTrace = false,
    bool AllowProjectExecution = false,
    string? ArtifactDirectory = null,
    TimeSpan? Timeout = null,
    long? DocumentVersion = null);

public enum DeveloperDiagnosticProviderStatus
{
    Available,
    Unsupported,
    Misconfigured,
}

public sealed record DeveloperDiagnosticProviderCapability(
    string ProviderId,
    DeveloperDiagnosticProviderStatus Status,
    string? ToolVersion = null,
    string? ReasonCode = null);

public interface IDeveloperDiagnosticProvider
{
    string Id { get; }

    ValueTask<DeveloperDiagnosticProviderCapability> ProbeAsync(
        CancellationToken cancellationToken = default);

    Task<DeveloperDiagnosticRun> AnalyzeAsync(
        DeveloperDiagnosticRequest request,
        CancellationToken cancellationToken = default);
}
