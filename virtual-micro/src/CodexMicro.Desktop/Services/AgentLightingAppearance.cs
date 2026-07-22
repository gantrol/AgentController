using System.Windows.Media;
using CodexMicro.Protocol;

namespace CodexMicro.Desktop.Services;

internal readonly record struct AgentLightingAppearance(
    bool IsActive,
    Color Color,
    double MaximumOpacity,
    double MinimumOpacity,
    TimeSpan? PulseHalfCycle,
    string StatusName,
    string EffectName)
{
    private static readonly Color InactiveColor =
        Color.FromRgb(0x8D, 0xB5, 0xFF);

    internal static AgentLightingAppearance From(SlotLighting? lighting)
    {
        if (
            lighting is null ||
            lighting.Color == 0 ||
            lighting.Brightness <= 0 ||
            lighting.Effect == 0)
        {
            return new AgentLightingAppearance(
                false,
                InactiveColor,
                0,
                0,
                null,
                "未分配",
                "off");
        }

        var brightness = Math.Clamp(lighting.Brightness, 0, 1);
        var color = Color.FromRgb(
            (byte)(lighting.Color >> 16),
            (byte)(lighting.Color >> 8),
            (byte)lighting.Color);
        var pulseFloor = lighting.Effect switch
        {
            4 => brightness * 0.18,
            6 => brightness * 0.55,
            _ => brightness,
        };
        TimeSpan? pulseHalfCycle = lighting.Effect is 4 or 6
            ? ResolvePulseHalfCycle(lighting.Speed)
            : null;

        return new AgentLightingAppearance(
            true,
            color,
            brightness,
            pulseFloor,
            pulseHalfCycle,
            ResolveStatusName(lighting.Color),
            ResolveEffectName(lighting.Effect));
    }

    private static TimeSpan ResolvePulseHalfCycle(double speed)
    {
        var normalizedSpeed = double.IsFinite(speed)
            ? Math.Clamp(speed, 0, 1)
            : 0;
        return TimeSpan.FromMilliseconds(1100 - (normalizedSpeed * 550));
    }

    private static string ResolveStatusName(int color) => color switch
    {
        0xFFFFFF => "空闲",
        0x304FFE => "思考中",
        0x00FF4C => "已完成",
        0xFF6D00 => "等待输入",
        0xFF0033 => "错误",
        _ => "已点亮",
    };

    private static string ResolveEffectName(int effect) => effect switch
    {
        0 => "off",
        1 => "solid",
        2 => "snake",
        3 => "rainbow",
        4 => "breath",
        5 => "gradient",
        6 => "shallow breath",
        _ => $"effect {effect}",
    };
}
