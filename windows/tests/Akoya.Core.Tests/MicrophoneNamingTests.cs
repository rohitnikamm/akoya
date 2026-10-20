using Akoya.Core.Mics;
using Xunit;

namespace Akoya.Core.Tests;

public class MicrophoneNamingTests
{
    private static EndpointFacts Ep(
        string id,
        string friendly,
        string description,
        string enumerator,
        EndpointFormFactor form = EndpointFormFactor.Microphone,
        bool parent = true,
        bool isDefault = false) =>
        new(id, friendly, description, form, enumerator, parent, isDefault);

    private static EndpointFacts Laptop(string id = "laptop") =>
        Ep(id, "Microphone Array (Realtek(R) Audio)", "Microphone", "HDAUDIO");

    private static EndpointFacts Buds(string id = "buds") =>
        Ep(id, "Headset (Galaxy Buds2 Pro Hands-Free AG Audio)", "Galaxy Buds2 Pro Hands-Free AG Audio",
            "BTHHFENUM", EndpointFormFactor.Headset);

    private static EndpointFacts Brio(string id = "brio") =>
        Ep(id, "Microphone (Logitech BRIO)", "Microphone", "USB");

    private static EndpointFacts Integrated(string id = "integrated") =>
        Ep(id, "Microphone (Integrated Camera)", "Microphone", "USB");

    private static EndpointFacts HyperX(string id = "hyperx") =>
        Ep(id, "Headset Microphone (HyperX Cloud III)", "HyperX Cloud III", "USB", EndpointFormFactor.Headset);

    private static EndpointFacts Nvidia(string id = "nvidia") =>
        Ep(id, "Microphone (NVIDIA Broadcast)", "Microphone", "ROOT", parent: false);

    private static EndpointFacts Voicemeeter(string id = "vm") =>
        Ep(id, "VoiceMeeter Output (VB-Audio VoiceMeeter VAIO)", "VoiceMeeter Output", "ROOT", parent: false);

    private static EndpointFacts StereoMix(string id = "mix") =>
        Ep(id, "Stereo Mix (Realtek(R) Audio)", "Stereo Mix", "HDAUDIO", EndpointFormFactor.LineLevel);

    private static NamedMic Only(params EndpointFacts[] endpoints) =>
        Assert.Single(MicrophoneNaming.Name(endpoints));

    [Fact]
    public void LaptopArray_IsThisPcsMicrophone()
    {
        var mic = Only(Laptop());
        Assert.Equal("This PC's microphone", mic.Name);
        Assert.False(mic.InMore);
    }

    [Fact]
    public void IntelAudioInternalMic_IsThisPcsMicrophone() =>
        Assert.Equal("This PC's microphone",
            Only(Ep("x", "Microphone (Intel(R) Smart Sound Technology)", "Microphone", "INTELAUDIO")).Name);

    [Fact]
    public void HeadsetJackOnHdAudio_IsNotThisPcsMicrophone()
    {
        var mic = Only(Ep("jack", "Headset Microphone (Realtek(R) Audio)", "Headset Microphone", "HDAUDIO",
            EndpointFormFactor.Headset));
        Assert.NotEqual("This PC's microphone", mic.Name);
        Assert.False(mic.InMore);
    }

    [Fact]
    public void Bluetooth_UsesDeviceNameWithoutSuffixes()
    {
        var mic = Only(Buds());
        Assert.Equal("Galaxy Buds2 Pro", mic.Name);
        Assert.False(mic.InMore);
    }

    [Theory]
    [InlineData("Headset (Galaxy Buds2 Pro Hands-Free AG Audio)", "Headset")]
    [InlineData("Headset (Galaxy Buds2 Pro Hands-Free AG Audio)", "Galaxy Buds2 Pro Hands-Free")]
    [InlineData("Headset (Galaxy Buds2 Pro Hands-Free AG Audio)", "Galaxy Buds2 Pro AG Audio")]
    [InlineData("Headset (Galaxy Buds2 Pro Hands-Free AG Audio)", "Galaxy Buds2 Pro Headset")]
    [InlineData("Headset (Galaxy Buds2 Pro Hands-Free AG Audio)", "Headset (Galaxy Buds2 Pro Hands-Free AG Audio)")]
    [InlineData("Microphone (Galaxy Buds2 Pro Hands-Free)", "Microphone")]
    [InlineData("Headset (GALAXY BUDS2 PRO HANDS-FREE AG AUDIO)", "Headset")]
    public void Bluetooth_StripsEveryKnownSuffixAndWrapper(string friendly, string description)
    {
        var mic = Only(Ep("bt", friendly, description, "BTHENUM", EndpointFormFactor.Headset));
        Assert.Equal(friendly.Contains("GALAXY") ? "GALAXY BUDS2 PRO" : "Galaxy Buds2 Pro", mic.Name);
    }

    [Fact]
    public void SingleWebcam_IsWebcamMic()
    {
        var mic = Only(Brio());
        Assert.Equal("Webcam mic", mic.Name);
        Assert.False(mic.InMore);
    }

    [Fact]
    public void TwoWebcams_AreNamedByModel()
    {
        var names = MicrophoneNaming.Name(new[] { Brio(), Integrated() }).Select(m => m.Name).ToArray();
        Assert.Equal(new[] { "Webcam mic (Integrated Camera)", "Webcam mic (Logitech BRIO)" }, names);
    }

    [Theory]
    [InlineData("Microphone (HD Pro Webcam C920)", "Microphone")]
    [InlineData("Microphone (Cam Link 4K)", "Microphone")]
    [InlineData("Microphone (USB Camera)", "Microphone")]
    [InlineData("Microphone (HD Webcam)", "HD Webcam")]
    [InlineData("Microphone (WEBCAM C270)", "Microphone")]
    public void UsbWebcamWords_AreWebcams(string friendly, string description) =>
        Assert.Equal("Webcam mic", Only(Ep("cam", friendly, description, "USB")).Name);

    [Fact]
    public void UsbHeadset_UsesDeviceDescription()
    {
        var mic = Only(HyperX());
        Assert.Equal("HyperX Cloud III", mic.Name);
        Assert.False(mic.InMore);
    }

    [Fact]
    public void UsbMic_WithGenericDescription_UsesWrapperName()
    {
        var mic = Only(Ep("mv7", "Microphone (Shure MV7)", "Microphone", "USB"));
        Assert.Equal("Shure MV7", mic.Name);
    }

    [Fact]
    public void NvidiaBroadcast_IsMore()
    {
        var mic = Only(Nvidia());
        Assert.True(mic.InMore);
        Assert.Equal("NVIDIA Broadcast", mic.Name);
    }

    [Fact]
    public void Voicemeeter_IsMoreAndNamedVoicemeeter()
    {
        var mic = Only(Voicemeeter());
        Assert.True(mic.InMore);
        Assert.Equal("Voicemeeter", mic.Name);
    }

    [Fact]
    public void Voicemeeter_MatchesCaseInsensitivelyAndAnyEndpoint()
    {
        var mic = Only(Ep("vm2", "VOICEMEETER AUX OUTPUT (VB-AUDIO VOICEMEETER VAIO3)", "Microphone", "ROOT",
            EndpointFormFactor.LineLevel, parent: false));
        Assert.True(mic.InMore);
        Assert.Equal("Voicemeeter", mic.Name);
    }

    [Fact]
    public void StereoMix_IsMore()
    {
        var mic = Only(StereoMix());
        Assert.True(mic.InMore);
        Assert.Equal("Stereo Mix", mic.Name);
    }

    [Theory]
    [InlineData("What U Hear (Sound Blaster Z)", "What U Hear")]
    [InlineData("Wave Out Mix (Realtek(R) Audio)", "Wave Out Mix")]
    [InlineData("STEREO MIX (Realtek(R) Audio)", "STEREO MIX")]
    public void LoopbackNames_AreMoreEvenWithMicrophoneFormFactor(string friendly, string expected)
    {
        var mic = Only(Ep("lb", friendly, "Microphone", "HDAUDIO"));
        Assert.True(mic.InMore);
        Assert.Equal(expected, mic.Name);
    }

    [Fact]
    public void DigitalAudioDisplayDevice_IsMore()
    {
        var mic = Only(Ep("hdmi", "Digital Audio (HDMI) (NVIDIA High Definition Audio)", "Digital Audio (HDMI)",
            "HDAUDIO", EndpointFormFactor.DigitalAudioDisplayDevice));
        Assert.True(mic.InMore);
    }

    [Theory]
    [InlineData("Microphone (Steam Streaming Microphone)", "Steam Streaming Microphone")]
    [InlineData("Microphone (Krisp Microphone)", "Krisp Microphone")]
    [InlineData("Microphone (OBS Virtual Audio)", "OBS Virtual Audio")]
    [InlineData("Microphone (Discord Audio)", "Discord Audio")]
    public void KnownVirtualVendors_AreMore(string friendly, string expected)
    {
        var mic = Only(Ep("v", friendly, "Microphone", "SWD", parent: true));
        Assert.True(mic.InMore);
        Assert.Equal(expected, mic.Name);
    }

    [Fact]
    public void SwdWithoutPhysicalParent_IsMore()
    {
        var mic = Only(Ep("swd", "Microphone (Some Virtual Thing)", "Microphone", "SWD", parent: false));
        Assert.True(mic.InMore);
        Assert.Equal("Some Virtual Thing", mic.Name);
    }

    [Fact]
    public void SwdWithPhysicalParent_IsNotMore()
    {
        var mic = Only(Ep("swd", "Microphone (Some Real Thing)", "Microphone", "SWD", parent: true));
        Assert.False(mic.InMore);
    }

    [Fact]
    public void Unknown_UsesFriendlyNameWithoutMicrophoneWrapper()
    {
        var mic = Only(Ep("u", "Microphone (Mystery Device)", "Microphone", "PCI"));
        Assert.Equal("Mystery Device", mic.Name);
        Assert.False(mic.InMore);
    }

    [Fact]
    public void Unknown_WithoutWrapper_KeepsFriendlyName() =>
        Assert.Equal("Mystery Device", Only(Ep("u", "Mystery Device", "Mystery Device", "PCI")).Name);

    [Fact]
    public void Unknown_KeepsNonMicrophoneWrapper() =>
        Assert.Equal("Line In (Mystery Card)", Only(Ep("u", "Line In (Mystery Card)", "Line In", "PCI")).Name);

    [Fact]
    public void DuplicatePlainNames_GetNumbers()
    {
        var names = MicrophoneNaming.Name(new[]
        {
            HyperX("a"), HyperX("b"), HyperX("c"),
        }).Select(m => m.Name).ToArray();
        Assert.Equal(new[] { "HyperX Cloud III", "HyperX Cloud III 2", "HyperX Cloud III 3" }, names);
    }

    [Fact]
    public void DuplicateBuiltIns_GetNumbers()
    {
        var names = MicrophoneNaming.Name(new[] { Laptop("a"), Laptop("b") }).Select(m => m.Name).ToArray();
        Assert.Equal(new[] { "This PC's microphone", "This PC's microphone 2" }, names);
    }

    [Fact]
    public void DuplicatesAreCaseInsensitive()
    {
        var names = MicrophoneNaming.Name(new[]
        {
            Ep("a", "Microphone (Shure MV7)", "Microphone", "USB"),
            Ep("b", "Microphone (SHURE MV7)", "Microphone", "USB"),
        }).Select(m => m.Name).ToArray();
        Assert.Equal(2, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Order_BuiltInFirstThenAlphabeticalWithWebcamsLastThenMore()
    {
        var names = MicrophoneNaming.Name(new[]
        {
            StereoMix(), Brio(), Nvidia(), Buds(), HyperX(), Laptop(),
        }).Select(m => (m.Name, m.InMore)).ToArray();

        Assert.Equal(new[]
        {
            ("This PC's microphone", false),
            ("Galaxy Buds2 Pro", false),
            ("HyperX Cloud III", false),
            ("Webcam mic", false),
            ("NVIDIA Broadcast", true),
            ("Stereo Mix", true),
        }, names);
    }

    [Fact]
    public void Order_DeskList()
    {
        var names = MicrophoneNaming.Name(new[] { StereoMix(), Brio(), Nvidia(), HyperX() })
            .Select(m => m.Name).ToArray();
        Assert.Equal(new[] { "HyperX Cloud III", "Webcam mic", "NVIDIA Broadcast", "Stereo Mix" }, names);
    }

    [Fact]
    public void Order_DoesNotDependOnInputOrder()
    {
        var all = new[] { StereoMix(), Brio(), Integrated(), Nvidia(), Voicemeeter(), Buds(), HyperX(), Laptop() };
        var forward = MicrophoneNaming.Name(all).Select(m => m.Id).ToArray();
        var backward = MicrophoneNaming.Name(all.AsEnumerable().Reverse().ToArray()).Select(m => m.Id).ToArray();
        Assert.Equal(forward, backward);
    }

    [Fact]
    public void IsDefault_IsCarriedThrough()
    {
        var mics = MicrophoneNaming.Name(new[] { HyperX(), Ep("l", "Microphone Array (Realtek(R) Audio)",
            "Microphone", "HDAUDIO", isDefault: true) });
        Assert.Equal("l", Assert.Single(mics, m => m.IsDefault).Id);
    }

    [Fact]
    public void Ids_AreCarriedThrough() =>
        Assert.Equal("{0.0.1.00000000}.{abc}",
            Only(Ep("{0.0.1.00000000}.{abc}", "Microphone (Shure MV7)", "Microphone", "USB")).Id);

    [Fact]
    public void NoEndpoints_GivesNoMics() => Assert.Empty(MicrophoneNaming.Name(Array.Empty<EndpointFacts>()));
}
