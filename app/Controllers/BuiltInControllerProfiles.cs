namespace CodexController.Controllers;

public static class BuiltInControllerProfiles
{
    public static ControllerProfile Ultimate2 { get; } =
        new(
            id: "8bitdo-ultimate2",
            displayName: "8BitDo Ultimate 2",
            visual: ControllerVisual.Xbox,
            glyphs: CreateXboxGlyphs(
                view: "−",
                menu: "+",
                leftAuxiliary: "L4",
                rightAuxiliary: "R4"),
            rawMapping: new RawMapping(
                new Dictionary<int, LogicalInput>
                {
                    [0] = LogicalInput.FaceSouth,
                    [1] = LogicalInput.FaceEast,
                    [2] = LogicalInput.FaceWest,
                    [3] = LogicalInput.FaceNorth,
                    [8] = LogicalInput.View,
                    [9] = LogicalInput.Menu,
                    [10] = LogicalInput.LeftStickPress,
                    [11] = LogicalInput.RightStickPress,
                }),
            tuning: new StickTuning(stickDeadZone: 0.22),
            vendorTool: new Uri(
                "https://app.8bitdo.com/Ultimate-Software-V2/"));

    public static ControllerProfile Xbox { get; } =
        new(
            id: "xbox",
            displayName: "Xbox Controller",
            visual: ControllerVisual.Xbox,
            glyphs: CreateXboxGlyphs(
                view: "▣",
                menu: "☰",
                leftAuxiliary: "P3",
                rightAuxiliary: "P1"),
            tuning: new StickTuning(stickDeadZone: 0.24),
            vendorTool: new Uri(
                "https://apps.microsoft.com/detail/9nblggh30xj3"));

    public static ControllerProfile Flydigi { get; } =
        new(
            id: "flydigi",
            displayName: "Flydigi Controller",
            visual: ControllerVisual.Xbox,
            glyphs: CreateXboxGlyphs(
                view: "−",
                menu: "+",
                leftAuxiliary: "M3",
                rightAuxiliary: "M4"),
            tuning: new StickTuning(stickDeadZone: 0.2),
            vendorTool: new Uri(
                "https://www.flydigi.com/index/down?nav_id=2"));

    public static ControllerProfile Generic { get; } =
        new(
            id: "generic",
            displayName: "Game Controller",
            visual: ControllerVisual.Generic,
            glyphs: CreateXboxGlyphs(
                view: "View",
                menu: "Menu",
                leftAuxiliary: "L4",
                rightAuxiliary: "R4"),
            rawMapping: CreateStandardRawMapping(),
            tuning: new StickTuning());

    public static ControllerProfileRegistry CreateRegistry() =>
        new(
            new[]
            {
                new ControllerProfileRegistration(
                    Ultimate2,
                    new[]
                    {
                        new ControllerMatchRule(
                            vid: 0x2DC8,
                            pid: 0x6013),
                        new ControllerMatchRule(
                            nameContains: "8BitDo Ultimate 2"),
                    }),
                new ControllerProfileRegistration(
                    Xbox,
                    new[]
                    {
                        new ControllerMatchRule(vid: 0x045E),
                        new ControllerMatchRule(
                            nameContains: "Xbox"),
                    }),
                new ControllerProfileRegistration(
                    Flydigi,
                    new[]
                    {
                        new ControllerMatchRule(vid: 0xD7D7),
                        new ControllerMatchRule(
                            nameContains: "Flydigi"),
                        new ControllerMatchRule(
                            nameContains: "Fly Digi"),
                    }),
            },
            Generic);

    private static IReadOnlyDictionary<LogicalInput, string>
        CreateXboxGlyphs(
            string view,
            string menu,
            string leftAuxiliary,
            string rightAuxiliary) =>
        new Dictionary<LogicalInput, string>
        {
            [LogicalInput.FaceSouth] = "A",
            [LogicalInput.FaceEast] = "B",
            [LogicalInput.FaceWest] = "X",
            [LogicalInput.FaceNorth] = "Y",
            [LogicalInput.LeftStick] = "L",
            [LogicalInput.RightStick] = "R",
            [LogicalInput.LeftStickPress] = "LS",
            [LogicalInput.RightStickPress] = "RS",
            [LogicalInput.DPadUp] = "↑",
            [LogicalInput.DPadDown] = "↓",
            [LogicalInput.DPadLeft] = "←",
            [LogicalInput.DPadRight] = "→",
            [LogicalInput.LeftShoulder] = "LB",
            [LogicalInput.RightShoulder] = "RB",
            [LogicalInput.LeftTrigger] = "LT",
            [LogicalInput.RightTrigger] = "RT",
            [LogicalInput.View] = view,
            [LogicalInput.Menu] = menu,
            [LogicalInput.Guide] = "⏻",
            [LogicalInput.LeftAuxiliary] = leftAuxiliary,
            [LogicalInput.RightAuxiliary] = rightAuxiliary,
        };

    private static RawMapping CreateStandardRawMapping() =>
        new(
            new Dictionary<int, LogicalInput>
            {
                [0] = LogicalInput.FaceSouth,
                [1] = LogicalInput.FaceEast,
                [2] = LogicalInput.FaceWest,
                [3] = LogicalInput.FaceNorth,
                [8] = LogicalInput.View,
                [9] = LogicalInput.Menu,
                [10] = LogicalInput.LeftStickPress,
                [11] = LogicalInput.RightStickPress,
            });
}
