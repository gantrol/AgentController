using System.Text;

namespace AgentController.Application.Diagnostics;

public static class DeveloperDiagnosticAggregator
{
    public static IReadOnlyList<DeveloperDiagnostic> Aggregate(
        IEnumerable<DeveloperDiagnosticObservation> observations)
    {
        ArgumentNullException.ThrowIfNull(observations);

        var groups = new Dictionary<string, AggregateState>(
            StringComparer.Ordinal);
        var order = new List<string>();

        foreach (var observation in observations)
        {
            ArgumentNullException.ThrowIfNull(observation);
            Validate(observation);

            var fingerprint = CreateFingerprint(observation);
            if (!groups.TryGetValue(fingerprint, out var state))
            {
                state = new AggregateState(observation);
                groups.Add(fingerprint, state);
                order.Add(fingerprint);
            }
            else
            {
                state.Add(observation);
            }
        }

        var result = new DeveloperDiagnostic[order.Count];
        for (var index = 0; index < order.Count; index++)
        {
            var fingerprint = order[index];
            result[index] = groups[fingerprint].Build(fingerprint);
        }

        return result;
    }

    public static string CreateFingerprint(
        DeveloperDiagnosticObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        Validate(observation);

        var path = NormalizePath(observation.Location.FilePath);
        var code = observation.Code.Trim().ToUpperInvariant();
        var message = NormalizeWhitespace(observation.Message);
        return string.Join(
            '\u001f',
            code,
            message,
            path,
            observation.Location.StartLine?.ToString() ?? string.Empty,
            observation.Location.StartColumn?.ToString() ?? string.Empty);
    }

    private static void Validate(DeveloperDiagnosticObservation observation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(observation.Code);
        ArgumentException.ThrowIfNullOrWhiteSpace(observation.Message);
        ArgumentNullException.ThrowIfNull(observation.Location);
        ArgumentNullException.ThrowIfNull(observation.Evidence);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            observation.Evidence.ProviderId);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            observation.Evidence.BuildId);
    }

    private static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var normalized = path
            .Trim()
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        return OperatingSystem.IsWindows()
            ? normalized.ToUpperInvariant()
            : normalized;
    }

    private static string NormalizeWhitespace(string value)
    {
        var builder = new StringBuilder(value.Length);
        var pendingSpace = false;
        foreach (var character in value.Trim())
        {
            if (char.IsWhiteSpace(character))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(character);
        }

        return builder.ToString();
    }

    private sealed class AggregateState
    {
        private DeveloperDiagnosticObservation _primary;
        private readonly List<DeveloperDiagnosticEvidence> _evidence = [];
        private readonly HashSet<EvidenceKey> _evidenceKeys = [];
        private int _occurrenceCount;

        internal AggregateState(DeveloperDiagnosticObservation observation)
        {
            _primary = observation;
            Add(observation);
        }

        internal void Add(DeveloperDiagnosticObservation observation)
        {
            _occurrenceCount++;
            if (observation.Severity > _primary.Severity)
            {
                _primary = observation;
            }

            var evidence = observation.Evidence;
            var key = new EvidenceKey(
                evidence.ProviderId,
                evidence.ToolVersion,
                evidence.BuildId,
                evidence.ArtifactPath);
            if (_evidenceKeys.Add(key))
            {
                _evidence.Add(evidence);
            }
        }

        internal DeveloperDiagnostic Build(string fingerprint) => new(
            fingerprint,
            _primary.Severity,
            _primary.Code.Trim(),
            NormalizeWhitespace(_primary.Message),
            _primary.Location,
            _primary.Project,
            _occurrenceCount,
            _evidence.ToArray(),
            _primary.CascadesFromFingerprint);

        private readonly record struct EvidenceKey(
            string ProviderId,
            string? ToolVersion,
            string BuildId,
            string? ArtifactPath);
    }
}
