using System.Runtime.InteropServices;
using Akoya.App.Platform;
using Akoya.Core.Mics;
using NAudio.CoreAudioApi;
using Xunit;

namespace Akoya.App.Tests;

public class MicrophoneCatalogTests
{
    [Fact]
    public void Catalog_Snapshot_DoesNotThrow()
    {
        // The CI runner may have no audio service or no devices at all: an empty list is fine.
        using var catalog = new MicrophoneCatalog();
        var snapshot = catalog.Snapshot();

        Assert.NotNull(snapshot);
        Assert.All(snapshot, facts => Assert.False(string.IsNullOrEmpty(facts.Id)));
        Assert.True(snapshot.Count(f => f.IsDefault) <= 1);
    }

    [Fact]
    public void Catalog_ChangedCanBeSubscribedAndDropped()
    {
        using var catalog = new MicrophoneCatalog();
        Action handler = () => { };
        catalog.Changed += handler;
        catalog.Changed -= handler;
    }

    [Theory]
    [InlineData(@"USB\VID_046D&PID_0825&MI_02\7&1A2B3C&0&0002", "USB")]
    [InlineData(@"SWD\MMDEVAPI\{0.0.1.00000000}.{ABCD}", "SWD")]
    [InlineData(@"ROOT\VBAUDIO\0000", "ROOT")]
    [InlineData(@"HDAUDIO\FUNC_01&VEN_10EC&DEV_0256\4&2&0&0001", "HDAUDIO")]
    [InlineData("NoSeparator", "NoSeparator")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void ParseEnumerator_IsTheTextBeforeTheFirstBackslash(string? instanceId, string expected)
    {
        Assert.Equal(expected, MicrophoneCatalog.ParseEnumerator(instanceId));
    }

    [Theory]
    [InlineData("USB", null, null, true)]
    [InlineData("BTHENUM", null, null, true)]
    [InlineData("HDAUDIO", null, null, true)]
    [InlineData("SWD", @"SWD\MMDEVAPI\{0.0.1.00000000}.{ABCD}", null, false)]
    [InlineData("SWD", @"SWD\MMDEVAPI\{0.0.1.00000000}.{ABCD}", @"USB\VID_046D&PID_0825\5&1&0&2", true)]
    [InlineData("SWD", @"SWD\MMDEVAPI\{0.0.1.00000000}.{ABCD}", @"{2}.\\?\bthhfenum#...", true)]
    [InlineData("SWD", @"SWD\MMDEVAPI\{0.0.1.00000000}.{ABCD}", @"BTHENUM\{0000111E-0000-1000-8000-00805F9B34FB}", true)]
    [InlineData("SWD", @"SWD\MMDEVAPI\x", @"HDAUDIO\FUNC_01&VEN_10EC&DEV_0256\4&2&0&0001", true)]
    [InlineData("ROOT", @"ROOT\VBAUDIO\0000", null, false)]
    [InlineData("ROOT", @"ROOT\VBAUDIO\0000", @"ROOT\MEDIA\0000", false)]
    [InlineData("", "", null, false)]
    public void HasPhysicalParent_FollowsTheDocumentedRule(
        string enumerator, string? instanceId, string? parentHint, bool expected)
    {
        Assert.Equal(expected, MicrophoneCatalog.HasPhysicalParent(enumerator, instanceId, parentHint));
    }

    [Theory]
    [InlineData(@"SWD\MMDEVAPI\{0.0.1.00000000}.{ABCD}", @"USB\VID_046D&PID_0825\5&1&0&2", "USB")]
    [InlineData(@"SWD\MMDEVAPI\{0.0.1.00000000}.{ABCD}", @"HDAUDIO\FUNC_01&VEN_10EC\4&2&0&0001", "HDAUDIO")]
    [InlineData(@"SWD\MMDEVAPI\{0.0.1.00000000}.{ABCD}", @"BTHENUM\{0000111E}", "BTHENUM")]
    [InlineData(@"SWD\MMDEVAPI\{0.0.1.00000000}.{ABCD}", null, "SWD")]
    [InlineData(@"ROOT\VBAUDIO\0000", @"ROOT\MEDIA\0000", "ROOT")]
    [InlineData(@"USB\VID_046D&PID_0825\5&1&0&2", @"HDAUDIO\FUNC_01", "USB")]
    [InlineData(null, null, "")]
    public void ResolveEnumerator_PrefersTheRealBusOverTheSoftwareEndpoint(
        string? instanceId, string? parentHint, string expected)
    {
        Assert.Equal(expected, MicrophoneCatalog.ResolveEnumerator(instanceId, parentHint));
    }

    [Theory]
    [InlineData(2u, EndpointFormFactor.LineLevel)]
    [InlineData(3u, EndpointFormFactor.Headphones)]
    [InlineData(4u, EndpointFormFactor.Microphone)]
    [InlineData(5u, EndpointFormFactor.Headset)]
    [InlineData(9u, EndpointFormFactor.DigitalAudioDisplayDevice)]
    [InlineData(10u, EndpointFormFactor.Unknown)]
    [InlineData(0u, EndpointFormFactor.Other)]
    [InlineData(1u, EndpointFormFactor.Other)]
    [InlineData(8u, EndpointFormFactor.Other)]
    [InlineData(99u, EndpointFormFactor.Other)]
    public void MapFormFactor_MatchesTheWindowsEnum(uint raw, EndpointFormFactor expected)
    {
        Assert.Equal(expected, MicrophoneCatalog.MapFormFactor(raw));
    }

    [Fact]
    public void MapFormFactor_MissingValueIsUnknown()
    {
        Assert.Equal(EndpointFormFactor.Unknown, MicrophoneCatalog.MapFormFactor(null));
    }

    [Fact]
    public void PropertyKeys_MatchTheDocumentedValues()
    {
        Assert.Equal(new Guid("1da5d803-d492-4edd-8c23-e0c0ffee7f0e"), PropertyKeys.PKEY_AudioEndpoint_FormFactor.formatId);
        Assert.Equal(0, PropertyKeys.PKEY_AudioEndpoint_FormFactor.propertyId);
        Assert.Equal(new Guid("78c34fc8-104a-4aca-9ea4-524d52996e57"), PropertyKeys.PKEY_Device_InstanceId.formatId);
        Assert.Equal(256, PropertyKeys.PKEY_Device_InstanceId.propertyId);
    }
}

public class MicPrivacyTests
{
    [Theory]
    [InlineData(null, null, true)]
    [InlineData("Allow", null, true)]
    [InlineData("Allow", "Allow", true)]
    [InlineData(null, "Allow", true)]
    [InlineData("Deny", null, false)]
    [InlineData("Deny", "Allow", false)]
    [InlineData("Allow", "Deny", false)]
    [InlineData(null, "Deny", false)]
    [InlineData("deny", "Allow", false)]
    [InlineData("Allow", "DENY", false)]
    [InlineData("Prompt", "Allow", true)]
    public void IsAllowed_OnlyDenyBlocks(string? global, string? nonPackaged, bool expected)
    {
        Assert.Equal(expected, MicPrivacy.IsAllowed(global, nonPackaged));
    }

    [Fact]
    public void IsDesktopAccessAllowed_DoesNotThrow()
    {
        // Whatever the machine's setting is, reading it must not throw (a missing key means allowed).
        _ = MicPrivacy.IsDesktopAccessAllowed();
    }
}

public class CaptureErrorsTests
{
    [Fact]
    public void AccessDenied_IsRecognisedFromComAndManagedForms()
    {
        Assert.True(CaptureErrors.IsAccessDenied(new COMException("denied", unchecked((int)0x80070005))));
        Assert.True(CaptureErrors.IsAccessDenied(new UnauthorizedAccessException()));
        Assert.False(CaptureErrors.IsAccessDenied(new COMException("other", unchecked((int)0x88890004))));
        Assert.False(CaptureErrors.IsAccessDenied(new InvalidOperationException()));
    }

    [Fact]
    public void DeviceGone_IsRecognisedFromComForms()
    {
        Assert.True(CaptureErrors.IsDeviceGone(new COMException("invalidated", unchecked((int)0x88890004))));
        Assert.True(CaptureErrors.IsDeviceGone(new COMException("not found", unchecked((int)0x80070490))));
        Assert.False(CaptureErrors.IsDeviceGone(new COMException("denied", unchecked((int)0x80070005))));
        Assert.False(CaptureErrors.IsDeviceGone(new InvalidOperationException()));
    }
}
