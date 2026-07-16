using CodexController.Controllers;
using CodexController.Core.Bridge;
using CodexController.Models;

namespace CodexController.Services;

public sealed class ControllerStateBuffer
{
    public const int DefaultCapacity = 256;

    private readonly object _sync = new();
    private readonly List<ControllerState> _states;
    private readonly int _capacity;
    private bool _drainScheduled;
    private long _droppedStateCount;

    public ControllerStateBuffer(int capacity = DefaultCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _capacity = capacity;
        _states = new List<ControllerState>(capacity);
    }

    public int Capacity => _capacity;

    public int Count
    {
        get
        {
            lock (_sync)
            {
                return _states.Count;
            }
        }
    }

    public long DroppedStateCount
    {
        get
        {
            lock (_sync)
            {
                return _droppedStateCount;
            }
        }
    }

    /// <summary>
    /// Adds a state and returns true when the caller should schedule a
    /// dispatcher drain. Further enqueues return false until Drain is called.
    /// </summary>
    public bool Enqueue(ControllerState state)
    {
        lock (_sync)
        {
            if (
                _states.Count > 0 &&
                CanCoalesce(_states[^1], state))
            {
                _states[^1] = state;
            }
            else
            {
                if (_states.Count == _capacity)
                {
                    _states.RemoveAt(0);
                    _droppedStateCount++;
                }

                _states.Add(state);
            }

            if (_drainScheduled)
            {
                return false;
            }

            _drainScheduled = true;
            return true;
        }
    }

    /// <summary>
    /// Removes the currently buffered states in input order and allows the
    /// next enqueue to request another dispatcher drain.
    /// </summary>
    public ControllerState[] Drain()
    {
        lock (_sync)
        {
            if (_states.Count == 0)
            {
                _drainScheduled = false;
                return [];
            }

            var drained = _states.ToArray();
            _states.Clear();
            _drainScheduled = false;
            return drained;
        }
    }

    private static bool CanCoalesce(
        ControllerState previous,
        ControllerState next)
    {
        return
            previous.IsConnected == next.IsConnected &&
            previous.Buttons == next.Buttons &&
            LeftTriggerRegion(previous) ==
                LeftTriggerRegion(next) &&
            RightTriggerRegion(previous) ==
                RightTriggerRegion(next);
    }

    private static int LeftTriggerRegion(ControllerState state)
    {
        if (!state.IsConnected)
        {
            return 0;
        }

        if (
            state.LeftTrigger <=
            BridgeTimings.PushToTalkReleaseThreshold)
        {
            return 0;
        }

        return
            state.LeftTrigger <
            BridgeTimings.PushToTalkEngageThreshold
                ? 1
                : 2;
    }

    private static int RightTriggerRegion(ControllerState state)
    {
        if (!state.IsConnected)
        {
            return 0;
        }

        if (
            state.RightTrigger <=
            RadialInputMap.TurnCandidateReleaseThreshold)
        {
            return 0;
        }

        if (
            state.RightTrigger <
            RadialInputMap.TurnCandidateThreshold)
        {
            return 1;
        }

        if (
            state.RightTrigger <=
            RadialInputMap.TurnReleaseThreshold)
        {
            return 2;
        }

        return
            state.RightTrigger <
            RadialInputMap.TurnEngageThreshold
                ? 3
                : 4;
    }
}
