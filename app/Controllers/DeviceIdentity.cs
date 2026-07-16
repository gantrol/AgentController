namespace CodexController.Controllers;

/// <summary>
/// Stable identity information reported by a controller input backend.
/// A backend may not be able to provide every field.
/// </summary>
public sealed record DeviceIdentity(
    ushort? Vid,
    ushort? Pid,
    string? RawName,
    string Backend)
{
    public string DisplayName =>
        string.IsNullOrWhiteSpace(RawName)
            ? Backend
            : RawName.Trim();
}
