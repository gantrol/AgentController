using CodexController.Controllers;

namespace CodexController.Tests;

public sealed class ControllerProfileRegistryTests
{
    [Theory]
    [InlineData(0x045E, "xbox")]
    [InlineData(0xD7D7, "flydigi")]
    public void KnownVendorIdsResolveBuiltInProfiles(
        int vid,
        string expectedProfileId)
    {
        var identity = new DeviceIdentity(
            (ushort)vid,
            null,
            null,
            "Raw HID");

        var profile =
            ControllerProfileRegistry.BuiltIn.Resolve(identity);

        Assert.Equal(expectedProfileId, profile.Id);
    }

    [Fact]
    public void GenericCypressVendorIdDoesNotImpersonateFlydigi()
    {
        var identity = new DeviceIdentity(
            0x04B4,
            null,
            "Cypress HID Device",
            "Raw HID");

        var profile =
            ControllerProfileRegistry.BuiltIn.Resolve(identity);

        Assert.Equal("generic", profile.Id);
    }

    [Fact]
    public void Ultimate2UsesObservedVendorAndProductPair()
    {
        var identity = new DeviceIdentity(
            0x2DC8,
            0x6013,
            null,
            "Raw HID");

        var profile =
            ControllerProfileRegistry.BuiltIn.Resolve(identity);

        Assert.Equal("8bitdo-ultimate2", profile.Id);
    }

    [Fact]
    public void Other8BitDoProductsDoNotImpersonateUltimate2()
    {
        var identity = new DeviceIdentity(
            0x2DC8,
            0x9999,
            "8BitDo Controller",
            "Raw HID");

        var profile =
            ControllerProfileRegistry.BuiltIn.Resolve(identity);

        Assert.Equal("generic", profile.Id);
    }

    [Theory]
    [InlineData(
        "8BitDo Ultimate 2 Wireless Controller",
        "8bitdo-ultimate2")]
    [InlineData("Xbox Wireless Controller", "xbox")]
    [InlineData("Flydigi Vader 4 Pro", "flydigi")]
    [InlineData("FLY DIGI APEX", "flydigi")]
    public void RawNamesResolveProfilesWhenVidIsUnavailable(
        string rawName,
        string expectedProfileId)
    {
        var identity = new DeviceIdentity(
            null,
            null,
            rawName,
            "Windows Gaming Input");

        var profile =
            ControllerProfileRegistry.BuiltIn.Resolve(identity);

        Assert.Equal(expectedProfileId, profile.Id);
    }

    [Fact]
    public void UnknownControllerUsesGenericFallback()
    {
        var identity = new DeviceIdentity(
            0x1234,
            0x5678,
            "Acme Turbo Pad",
            "Raw HID");

        var known =
            ControllerProfileRegistry.BuiltIn.TryResolveKnown(
                identity,
                out var profile);

        Assert.False(known);
        Assert.Same(BuiltInControllerProfiles.Generic, profile);
    }

    [Fact]
    public void XInputBackendAloneDoesNotAssumeXboxHardware()
    {
        var identity = new DeviceIdentity(
            null,
            null,
            null,
            "XInput");

        var profile =
            ControllerProfileRegistry.BuiltIn.Resolve(identity);

        Assert.Equal("generic", profile.Id);
    }

    [Fact]
    public void VendorIdOutranksContradictoryNameHeuristic()
    {
        var identity = new DeviceIdentity(
            0x045E,
            null,
            "8BitDo compatibility controller",
            "Raw HID");

        var profile =
            ControllerProfileRegistry.BuiltIn.Resolve(identity);

        Assert.Equal("xbox", profile.Id);
    }

    [Fact]
    public void BuiltInProfilesExposeGlyphAndVendorMetadata()
    {
        var ultimate2 = BuiltInControllerProfiles.Ultimate2;
        var xbox = BuiltInControllerProfiles.Xbox;
        var flydigi = BuiltInControllerProfiles.Flydigi;
        var generic = BuiltInControllerProfiles.Generic;

        Assert.Equal(
            "A",
            ultimate2.GetGlyph(LogicalInput.FaceSouth));
        Assert.Equal(
            "X",
            xbox.GetGlyph(LogicalInput.FaceWest));
        Assert.Equal(
            "M4",
            flydigi.GetGlyph(LogicalInput.RightAuxiliary));
        Assert.Equal(
            "⧉",
            xbox.GetGlyph(LogicalInput.View));
        Assert.Equal(
            "☰",
            xbox.GetGlyph(LogicalInput.Menu));
        Assert.Equal(
            "−",
            ultimate2.GetGlyph(LogicalInput.View));
        Assert.Equal(
            "+",
            ultimate2.GetGlyph(LogicalInput.Menu));
        Assert.Equal(
            "−",
            flydigi.GetGlyph(LogicalInput.View));
        Assert.Equal(
            "+",
            flydigi.GetGlyph(LogicalInput.Menu));
        Assert.Equal(
            "View",
            generic.GetGlyph(LogicalInput.View));
        Assert.Equal(
            "Menu",
            generic.GetGlyph(LogicalInput.Menu));
        Assert.Equal(ControllerVisual.Xbox, ultimate2.Visual);
        Assert.Equal(ControllerVisual.Generic, generic.Visual);
        Assert.Equal(
            "app.8bitdo.com",
            ultimate2.VendorTool?.Host);
        Assert.Equal(
            "apps.microsoft.com",
            xbox.VendorTool?.Host);
        Assert.Equal(
            "www.flydigi.com",
            flydigi.VendorTool?.Host);
        Assert.Null(generic.VendorTool);
    }

    [Fact]
    public void EveryBuiltInProfileHasAGlyphForEveryLogicalInput()
    {
        foreach (
            var profile in
            ControllerProfileRegistry.BuiltIn.Profiles)
        {
            foreach (
                var input in
                Enum.GetValues<LogicalInput>())
            {
                Assert.True(
                    profile.Glyphs.ContainsKey(input),
                    $"{profile.Id} is missing a glyph for {input}.");
            }
        }
    }

    [Fact]
    public void Ultimate2CarriesCurrentRawHidButtonLayout()
    {
        var mapping =
            Assert.IsType<RawMapping>(
                BuiltInControllerProfiles.Ultimate2.RawMapping);

        Assert.Equal(
            LogicalInput.FaceSouth,
            mapping.ButtonIndices[0]);
        Assert.Equal(
            LogicalInput.View,
            mapping.ButtonIndices[8]);
        Assert.Equal(
            LogicalInput.RightStickPress,
            mapping.ButtonIndices[11]);
        Assert.Equal(0, mapping.LeftXIndex);
        Assert.Equal(5, mapping.RightTriggerIndex);
    }

    [Fact]
    public void ProfileCopiesGlyphDictionary()
    {
        var glyphs = new Dictionary<LogicalInput, string>
        {
            [LogicalInput.FaceSouth] = "A",
        };
        var profile = new ControllerProfile(
            "test-pad",
            "Test Pad",
            ControllerVisual.Generic,
            glyphs);

        glyphs[LogicalInput.FaceSouth] = "changed";

        Assert.Equal(
            "A",
            profile.GetGlyph(LogicalInput.FaceSouth));
    }

    [Fact]
    public void EquallySpecificConflictingRulesUseSafeFallback()
    {
        var fallback = TestProfile("fallback");
        var registry = new ControllerProfileRegistry(
            new[]
            {
                new ControllerProfileRegistration(
                    TestProfile("first"),
                    new[]
                    {
                        new ControllerMatchRule(
                            nameContains: "Controller"),
                    }),
                new ControllerProfileRegistration(
                    TestProfile("second"),
                    new[]
                    {
                        new ControllerMatchRule(
                            nameContains: "Controller"),
                    }),
            },
            fallback);

        var profile = registry.Resolve(new DeviceIdentity(
            null,
            null,
            "Ambiguous Controller",
            "Raw HID"));

        Assert.Same(fallback, profile);
    }

    private static ControllerProfile TestProfile(string id) =>
        new(
            id,
            id,
            ControllerVisual.Generic,
            new Dictionary<LogicalInput, string>
            {
                [LogicalInput.FaceSouth] = "A",
            });
}
