namespace AgentController.Platform.Controllers;

[Flags]
public enum ControllerButtons : ulong
{
    None = 0,
    South = 1UL << 0,
    East = 1UL << 1,
    West = 1UL << 2,
    North = 1UL << 3,
    DpadUp = 1UL << 4,
    DpadRight = 1UL << 5,
    DpadDown = 1UL << 6,
    DpadLeft = 1UL << 7,
    LeftShoulder = 1UL << 8,
    RightShoulder = 1UL << 9,
    Menu = 1UL << 10,
    Options = 1UL << 11,
    Home = 1UL << 12,
    LeftStick = 1UL << 13,
    RightStick = 1UL << 14,
}

[Flags]
public enum ControllerFeatures
{
    None = 0,
    ExtendedGamepad = 1 << 0,
    Battery = 1 << 1,
    Haptics = 1 << 2,
    Light = 1 << 3,
    BackgroundEvents = 1 << 4,
    SystemRemapping = 1 << 5,
}

public readonly record struct StickPosition(float X, float Y)
{
    public StickPosition Clamped() => new(
        Math.Clamp(X, -1f, 1f),
        Math.Clamp(Y, -1f, 1f));
}

public sealed record ControllerInputSnapshot(
    string Id,
    string DisplayName,
    string ProductCategory,
    ControllerButtons Buttons,
    StickPosition LeftStick,
    StickPosition RightStick,
    float LeftTrigger,
    float RightTrigger,
    float? BatteryLevel,
    ControllerFeatures Features,
    bool IsCurrent,
    bool IsIdentityStable);

public interface IControllerInputSource : IDisposable
{
    string BackendName { get; }

    bool IsAvailable { get; }

    bool SupportsBackgroundEvents { get; }

    IReadOnlyList<ControllerInputSnapshot> Poll();
}
