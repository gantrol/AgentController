namespace CodexController.Services;

public sealed class AxisRepeater
{
    private readonly Dictionary<string, AxisState> _states = [];

    public void Update(
        string id,
        int direction,
        int repeatDelayMs,
        int repeatIntervalMs,
        Action<int> action)
    {
        var now = Environment.TickCount64;
        if (!_states.TryGetValue(id, out var state))
        {
            state = new AxisState();
            _states[id] = state;
        }

        if (direction == 0)
        {
            state.Direction = 0;
            state.NextAt = 0;
            return;
        }

        if (state.Direction != direction)
        {
            state.Direction = direction;
            state.NextAt = now + repeatDelayMs;
            action(direction);
            return;
        }

        if (now >= state.NextAt)
        {
            state.NextAt = now + repeatIntervalMs;
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
    }
}
