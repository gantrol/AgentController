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
        var delay = Math.Max(1, repeatDelayMs);
        var interval = Math.Max(1, repeatIntervalMs);
        if (!_states.TryGetValue(id, out var state))
        {
            state = new AxisState();
            _states[id] = state;
        }

        if (direction == 0)
        {
            state.Direction = 0;
            state.NextAt = 0;
            state.LastActionAt = 0;
            state.ScheduledDurationMs = 0;
            state.WaitingForInitialRepeat = false;
            return;
        }

        if (state.Direction != direction)
        {
            state.Direction = direction;
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
        public long NextAt { get; set; }
        public long LastActionAt { get; set; }
        public int ScheduledDurationMs { get; set; }
        public bool WaitingForInitialRepeat { get; set; }
    }
}
