using System.Collections.ObjectModel;

namespace CodexController.Controllers;

/// <summary>
/// One AND-combined identity pattern. A profile may register several patterns,
/// which are evaluated as alternatives.
/// </summary>
public sealed record ControllerMatchRule
{
    public ControllerMatchRule(
        ushort? vid = null,
        ushort? pid = null,
        string? nameContains = null,
        string? backendContains = null)
    {
        NameContains = Normalize(nameContains);
        BackendContains = Normalize(backendContains);

        if (
            vid is null &&
            pid is null &&
            NameContains is null &&
            BackendContains is null)
        {
            throw new ArgumentException(
                "A controller match rule must contain at least one selector.");
        }

        Vid = vid;
        Pid = pid;
    }

    public ushort? Vid { get; }

    public ushort? Pid { get; }

    public string? NameContains { get; }

    public string? BackendContains { get; }

    public bool Matches(DeviceIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);

        return
            (Vid is null || identity.Vid == Vid) &&
            (Pid is null || identity.Pid == Pid) &&
            Contains(identity.RawName, NameContains) &&
            Contains(identity.Backend, BackendContains);
    }

    internal int Specificity =>
        (Vid is null ? 0 : 1_000) +
        (Pid is null ? 0 : 500) +
        (NameContains is null ? 0 : 100 + NameContains.Length) +
        (BackendContains is null ? 0 : 25 + BackendContains.Length);

    private static bool Contains(
        string? value,
        string? expectedFragment) =>
        expectedFragment is null ||
        (!string.IsNullOrWhiteSpace(value) &&
         value.Contains(
             expectedFragment,
             StringComparison.OrdinalIgnoreCase));

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
}

public sealed record ControllerProfileRegistration
{
    public ControllerProfileRegistration(
        ControllerProfile profile,
        IReadOnlyList<ControllerMatchRule> rules)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(rules);

        if (rules.Count == 0)
        {
            throw new ArgumentException(
                "A profile registration must contain at least one match rule.",
                nameof(rules));
        }

        Profile = profile;
        Rules = new ReadOnlyCollection<ControllerMatchRule>(
            rules.ToArray());
    }

    public ControllerProfile Profile { get; }

    public IReadOnlyList<ControllerMatchRule> Rules { get; }
}

/// <summary>
/// Resolves identity metadata to the most specific registered profile.
/// Vendor/product IDs outrank name-only heuristics.
/// </summary>
public sealed class ControllerProfileRegistry
{
    private readonly IReadOnlyList<ControllerProfileRegistration>
        _registrations;

    public ControllerProfileRegistry(
        IEnumerable<ControllerProfileRegistration> registrations,
        ControllerProfile fallback)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        ArgumentNullException.ThrowIfNull(fallback);

        var registrationList = registrations.ToArray();
        var duplicateId = registrationList
            .Select(item => item.Profile.Id)
            .Append(fallback.Id)
            .GroupBy(id => id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateId is not null)
        {
            throw new ArgumentException(
                $"Controller profile ID '{duplicateId.Key}' is duplicated.",
                nameof(registrations));
        }

        _registrations =
            new ReadOnlyCollection<ControllerProfileRegistration>(
                registrationList);
        Fallback = fallback;
        Profiles = new ReadOnlyCollection<ControllerProfile>(
            registrationList
                .Select(item => item.Profile)
                .Append(fallback)
                .ToArray());
    }

    public static ControllerProfileRegistry BuiltIn { get; } =
        BuiltInControllerProfiles.CreateRegistry();

    public ControllerProfile Fallback { get; }

    public IReadOnlyList<ControllerProfile> Profiles { get; }

    public ControllerProfile Resolve(DeviceIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);

        ControllerProfile? bestProfile = null;
        var bestSpecificity = -1;
        var isAmbiguous = false;

        foreach (var registration in _registrations)
        {
            foreach (var rule in registration.Rules)
            {
                if (!rule.Matches(identity))
                {
                    continue;
                }

                if (rule.Specificity > bestSpecificity)
                {
                    bestProfile = registration.Profile;
                    bestSpecificity = rule.Specificity;
                    isAmbiguous = false;
                    continue;
                }

                if (
                    rule.Specificity == bestSpecificity &&
                    bestProfile is not null &&
                    !ReferenceEquals(
                        bestProfile,
                        registration.Profile))
                {
                    isAmbiguous = true;
                }
            }
        }

        return isAmbiguous
            ? Fallback
            : bestProfile ?? Fallback;
    }

    public bool TryResolveKnown(
        DeviceIdentity identity,
        out ControllerProfile profile)
    {
        profile = Resolve(identity);
        return !ReferenceEquals(profile, Fallback);
    }
}
