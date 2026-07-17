namespace CodexController.Services;

public sealed class AxisRepeater
{
    private readonly Dictionary<string, AxisState> _states = [];
    private readonly Func<long> _tickProvider;

    public AxisRepeater(Func<long>? tickProvider = null)
    {
        _tickProvider =
            tickProvider ?? (() => Environment.TickCount64);
    }

    public void Update(
        string id,
        int direction,
        int repeatDelayMs,
        int repeatIntervalMs,
        Action<int> action)
    {
        var now = _tickProvider();
        var state = StateFor(id);
        if (!PrepareDirection(
                state,
                direction,
                now,
                out var started,
                out _))
        {
            return;
        }

        ApplyTiming(
            state,
            direction,
            now,
            Math.Max(1, repeatDelayMs),
            Math.Max(1, repeatIntervalMs),
            started,
            action);
    }

    public void UpdateAnalog(
        string id,
        int direction,
        double magnitude,
        double engageDeadZone,
        int configuredDelayMs,
        int configuredIntervalMs,
        Action<int> action)
    {
        var now = _tickProvider();
        var state = StateFor(id);
        if (!PrepareDirection(
                state,
                direction,
                now,
                out var started,
                out var heldDurationMs))
        {
            return;
        }

        var timing = AnalogRepeatTimingPolicy.Resolve(
            magnitude,
            engageDeadZone,
            heldDurationMs,
            configuredDelayMs,
            configuredIntervalMs);
        ApplyTiming(
            state,
            direction,
            now,
            timing.InitialDelayMs,
            timing.IntervalMs,
            started,
            action);
    }

    private AxisState StateFor(string id)
    {
        if (!_states.TryGetValue(id, out var state))
        {
            state = new AxisState();
            _states[id] = state;
        }

        return state;
    }

    private static bool PrepareDirection(
        AxisState state,
        int direction,
        long now,
        out bool started,
        out long heldDurationMs)
    {
        if (direction == 0)
        {
            state.Reset();
            started = false;
            heldDurationMs = 0;
            return false;
        }

        started = state.Direction != direction;
        if (started)
        {
            state.Direction = direction;
            state.DirectionStartedAt = now;
        }

        heldDurationMs = Math.Max(
            0,
            now - state.DirectionStartedAt);
        return true;
    }

    private static void ApplyTiming(
        AxisState state,
        int direction,
        long now,
        int delay,
        int interval,
        bool started,
        Action<int> action)
    {
        if (started)
        {
            state.LastActionAt = now;
            state.ScheduledDurationMs = delay;
            state.NextAt = now + delay;
            state.WaitingForInitialRepeat = true;
            action(direction);
            return;
        }

        var desiredDuration = state.WaitingForInitialRepeat
            ? delay
            : interval;
        if (desiredDuration < state.ScheduledDurationMs)
        {
            state.ScheduledDurationMs = desiredDuration;
            state.NextAt = Math.Min(
                state.NextAt,
                state.LastActionAt + desiredDuration);
        }

        if (now >= state.NextAt)
        {
            state.LastActionAt = now;
            state.ScheduledDurationMs = interval;
            state.NextAt = now + interval;
            state.WaitingForInitialRepeat = false;
            action(direction);
        }
    }

    public void Reset()
    {
        _states.Clear();
    }

    private sealed class AxisState
    {
        public int Direction { get; set; }
        public long DirectionStartedAt { get; set; }
        public long NextAt { get; set; }
        public long LastActionAt { get; set; }
        public int ScheduledDurationMs { get; set; }
        public bool WaitingForInitialRepeat { get; set; }

        public void Reset()
        {
            Direction = 0;
            DirectionStartedAt = 0;
            NextAt = 0;
            LastActionAt = 0;
            ScheduledDurationMs = 0;
            WaitingForInitialRepeat = false;
        }
    }
}
