namespace CodexController.Controllers;

internal static class SimpleSpeedInputPolicy
{
    internal static bool? ResolveFastTarget(int verticalDirection) =>
        Math.Sign(verticalDirection) switch
        {
            1 => false,
            -1 => true,
            _ => null,
        };
}
