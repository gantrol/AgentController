using System.Collections.ObjectModel;

namespace CodexController.Controllers;

/// <summary>
/// Optional raw-HID layout for a profile. XInput-compatible profiles normally
/// do not need one.
/// </summary>
public sealed record RawMapping
{
    public RawMapping(
        IReadOnlyDictionary<int, LogicalInput> buttonIndices,
        int leftXIndex = 0,
        int leftYIndex = 1,
        int rightXIndex = 2,
        int rightYIndex = 3,
        int? leftTriggerIndex = 4,
        int? rightTriggerIndex = 5)
    {
        ArgumentNullException.ThrowIfNull(buttonIndices);

        if (buttonIndices.Keys.Any(index => index < 0))
        {
            throw new ArgumentOutOfRangeException(
                nameof(buttonIndices),
                "Raw button indices cannot be negative.");
        }

        if (
            leftXIndex < 0 ||
            leftYIndex < 0 ||
            rightXIndex < 0 ||
            rightYIndex < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(leftXIndex),
                "Raw stick axis indices cannot be negative.");
        }

        if (leftTriggerIndex < 0 || rightTriggerIndex < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(leftTriggerIndex),
                "Raw trigger axis indices cannot be negative.");
        }

        ButtonIndices = new ReadOnlyDictionary<int, LogicalInput>(
            new Dictionary<int, LogicalInput>(buttonIndices));
        LeftXIndex = leftXIndex;
        LeftYIndex = leftYIndex;
        RightXIndex = rightXIndex;
        RightYIndex = rightYIndex;
        LeftTriggerIndex = leftTriggerIndex;
        RightTriggerIndex = rightTriggerIndex;
    }

    public IReadOnlyDictionary<int, LogicalInput> ButtonIndices { get; }

    public int LeftXIndex { get; }

    public int LeftYIndex { get; }

    public int RightXIndex { get; }

    public int RightYIndex { get; }

    public int? LeftTriggerIndex { get; }

    public int? RightTriggerIndex { get; }
}
