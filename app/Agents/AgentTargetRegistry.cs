using System.Collections.ObjectModel;

namespace CodexController.Agents;

/// <summary>
/// Resolves persisted agent IDs to installed target adapters. Unknown IDs
/// safely fall back to the configured default target.
/// </summary>
public sealed class AgentTargetRegistry
{
    private readonly IReadOnlyDictionary<AgentId, IAgentTarget> _byId;

    public AgentTargetRegistry(
        IEnumerable<IAgentTarget> targets,
        AgentId defaultTargetId)
    {
        ArgumentNullException.ThrowIfNull(targets);

        var targetList = targets.ToArray();
        if (targetList.Length == 0)
        {
            throw new ArgumentException(
                "At least one agent target must be registered.",
                nameof(targets));
        }

        var duplicate = targetList
            .GroupBy(target => target.Id)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new ArgumentException(
                $"Agent target '{duplicate.Key}' is registered more than once.",
                nameof(targets));
        }

        var byId = targetList.ToDictionary(target => target.Id);
        if (!byId.TryGetValue(defaultTargetId, out var defaultTarget))
        {
            throw new ArgumentException(
                $"Default agent target '{defaultTargetId}' is not registered.",
                nameof(defaultTargetId));
        }

        _byId = new ReadOnlyDictionary<AgentId, IAgentTarget>(byId);
        Targets = new ReadOnlyCollection<IAgentTarget>(targetList);
        Default = defaultTarget;
    }

    public IReadOnlyList<IAgentTarget> Targets { get; }

    public IAgentTarget Default { get; }

    public IAgentTarget Resolve(string? persistedId)
    {
        if (
            !string.IsNullOrWhiteSpace(persistedId) &&
            TryCreateId(persistedId.Trim(), out var id) &&
            _byId.TryGetValue(id, out var target))
        {
            return target;
        }

        return Default;
    }

    private static bool TryCreateId(
        string value,
        out AgentId id)
    {
        try
        {
            id = new AgentId(value);
            return true;
        }
        catch (ArgumentException)
        {
            id = default;
            return false;
        }
    }
}
