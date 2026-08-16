using System.Windows.Media;
using CodexMicro.Protocol;

namespace CodexMicro.Desktop.Services;

internal readonly record struct AgentLightingAppearance(
    bool IsActive,
    bool IsCurrentSession,
    bool UsesWhiteFallback,
    Color Color,
    double DisplayOpacity,
    double WideGlowOpacity,
    double OuterGlowOpacity,
    double CapWashOpacity,
    double LightFieldOpacity,
    double WellWashOpacity,
    string StatusName,
    string EffectName)
{
    private static readonly Color InactiveColor =
        Color.FromRgb(0x8D, 0xB5, 0xFF);

    internal static AgentLightingAppearance From(
        SlotLighting? lighting,
        bool isCurrentSession = false)
    {
        if (
            lighting is null ||
            lighting.Color == 0 ||
            lighting.Brightness <= 0 ||
            lighting.Effect == 0)
        {
            if (isCurrentSession)
            {
                return new AgentLightingAppearance(
                    true,
                    true,
                    true,
                    Colors.White,
                    1,
                    0.90,
                    0.50,
                    0.16,
                    0.42,
                    0.38,
                    "当前会话 · 无对应状态",
                    "white fallback");
            }

            return new AgentLightingAppearance(
                false,
                false,
                false,
                InactiveColor,
                0,
                0,
                0,
                0,
                0,
                0,
                "未分配",
                "off");
        }

        // Protocol brightness scales five independent light carriers. A
        // background session concentrates color in its circular well; the
        // selected session also washes the full cap. All carrier fills remain
        // flat so lighting does not reintroduce a key-surface gradient.
        var brightness = Math.Clamp(lighting.Brightness, 0, 1);
        var color = Color.FromRgb(
            (byte)(lighting.Color >> 16),
            (byte)(lighting.Color >> 8),
            (byte)lighting.Color);
        var usesBrightWhiteGlow =
            isCurrentSession && lighting.Color == 0xFFFFFF;
        return new AgentLightingAppearance(
            true,
            isCurrentSession,
            false,
            color,
            brightness,
            usesBrightWhiteGlow
                ? 0.90
                : isCurrentSession
                    ? 0.38
                    : 0.30,
            usesBrightWhiteGlow
                ? 0.50
                : isCurrentSession
                    ? 0.18
                    : 0.14,
            isCurrentSession ? 0.20 : 0.07,
            isCurrentSession ? 0.38 : 0.34,
            isCurrentSession ? 0.38 : 0.42,
            ResolveStatusName(lighting.Color),
            ResolveEffectName(lighting.Effect));
    }

    /// <summary>
    /// Maps a Harness session independently from selection. A recent or
    /// selected session is navigation state, not proof that work is running,
    /// so idle sessions must remain dark.
    /// </summary>
    internal static AgentLightingAppearance FromHarnessSession(
        bool isRunning,
        bool isCurrentSession)
    {
        if (!isRunning)
        {
            return new AgentLightingAppearance(
                false,
                isCurrentSession,
                false,
                InactiveColor,
                0,
                0,
                0,
                0,
                0,
                0,
                "空闲",
                "off");
        }

        // Follow the Micro status vocabulary used by the native Codex
        // protocol: blue means work is actively thinking/generating, while
        // green is reserved for a completed transition. A Harness snapshot
        // exposes only running/idle, so it must never hold a completed-green
        // key for the whole duration of a turn.
        var color = Color.FromRgb(0x30, 0x4F, 0xFE);
        return new AgentLightingAppearance(
            true,
            isCurrentSession,
            false,
            color,
            isCurrentSession ? 1 : 0.94,
            isCurrentSession ? 0.82 : 0.42,
            isCurrentSession ? 0.48 : 0.22,
            isCurrentSession ? 0.28 : 0.12,
            isCurrentSession ? 0.52 : 0.43,
            isCurrentSession ? 0.48 : 0.40,
            "运行中",
            isCurrentSession ? "selected" : "solid");
    }

    internal static bool IndicatesCurrentSession(SlotLighting lighting) =>
        lighting.SlotId is >= 0 and < 6 && lighting.Effect == 4;

    internal static int? ResolveCurrentSessionSlot(
        IEnumerable<SlotLighting> lighting,
        int? retainedSlot,
        IEnumerable<int>? rosterSlots = null)
    {
        var populatedSlots = rosterSlots?
            .Where(slot => slot is >= 0 and < 6)
            .Distinct()
            .ToArray() ?? [];
        var signalledSlots = lighting
            .Where(IndicatesCurrentSession)
            .Select(slot => slot.SlotId)
            .Distinct()
            .Take(2)
            .ToArray();
        if (signalledSlots.Length == 1)
        {
            return signalledSlots[0];
        }

        if (
            retainedSlot is >= 0 and < 6 &&
            (populatedSlots.Length == 0 || populatedSlots.Contains(retainedSlot.Value)))
        {
            return retainedSlot;
        }

        return populatedSlots.Length == 0 ? null : populatedSlots[0];
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
