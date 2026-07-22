using System.Windows.Media;
using CodexMicro.Protocol;

namespace CodexMicro.Desktop.Services;

internal readonly record struct AgentLightingAppearance(
    bool IsActive,
    Color Color,
    double DisplayOpacity,
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
                "未分配",
                "off");
        }

        // Protocol brightness scales a translucent glass layer in the
        // template; it never becomes an opaque circular fill.
        var brightness = Math.Clamp(lighting.Brightness, 0, 1);
        var color = Color.FromRgb(
            (byte)(lighting.Color >> 16),
            (byte)(lighting.Color >> 8),
            (byte)lighting.Color);
        return new AgentLightingAppearance(
            true,
            color,
            brightness,
            ResolveStatusName(lighting.Color),
            ResolveEffectName(lighting.Effect));
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
