using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Akoya.App.Setup;
using Akoya.App.Tests.Setup;
using Akoya.Core.Dictation;
using Akoya.Core.Mics;
using Xunit;

namespace Akoya.App.Tests.Snapshots;

/// <summary>
/// The setup card in every state, on the mockup's page colour with the card 40 DIP in from the top-left, matching the
/// 460×640 crops of docs/design/windows-onboarding-mockup.html used for side-by-side comparison.
/// </summary>
public class SetupSnapshots
{
    private const double Width = 460;
    private const double Height = 640;

    /// <summary>
    /// The mockup's Try it waveform (dots per column 1,2,3,5,7,5,3,2,...) as level steps; columns are always odd in
    /// the app, so the mockup's 2-dot columns become 1.
    /// </summary>
    private static readonly int[] Wave = [1, 2, 3, 5, 6, 5, 3, 2, 3, 5, 6, 7, 6, 5, 3, 2, 3, 5, 6, 5, 3, 2, 1];

    [Fact]
    public void Welcome() => Render("setup-welcome", new SetupRig(Devices.Laptop(), Devices.Buds(), Devices.Webcam()));

    [Fact]
    public void MicList()
    {
        var rig = Microphone(new SetupRig(Devices.Laptop(), Devices.Buds(), Devices.Webcam()));
        rig.Talk(0.4, ("laptop", 7), ("buds", 1), ("cam", 3));
        Assert.Equal(MicStatusKind.HearingYou, rig.Model.Status);
        Render("setup-mic-list", rig);
    }

    [Fact]
    public void MicAmber()
    {
        var rig = Microphone(new SetupRig(Devices.Laptop(isDefault: false), Devices.Buds(isDefault: true), Devices.Webcam()));
        rig.Talk(3.2, ("laptop", 7), ("buds", 1), ("cam", 3));
        Assert.Equal("Galaxy Buds isn't hearing you", rig.Model.StatusText);
        Render("setup-mic-amber", rig);
    }

    [Fact]
    public void MicOne()
    {
        var rig = Microphone(new SetupRig(Devices.Laptop()));
        rig.Talk(0.4, ("laptop", 7));
        Assert.Equal("Is this the mic you want?", rig.Model.MicTitle);
        Render("setup-mic-one", rig);
    }

    [Fact]
    public void MicMore()
    {
        var rig = Microphone(new SetupRig(Devices.HyperX(), Devices.Webcam("brio"), Devices.Nvidia(), Devices.StereoMix()));
        rig.Talk(0.4, ("hyperx", 7), ("brio", 3), ("nvidia", 1), ("mix", 1));
        Render("setup-mic-more", rig);
    }

    [Fact]
    public void MicMoreExpanded()
    {
        var rig = Microphone(new SetupRig(Devices.HyperX(), Devices.Webcam("brio"), Devices.Nvidia(), Devices.StereoMix()));
        rig.Model.ToggleMore();
        rig.Model.SelectMic("nvidia");
        rig.Talk(0.4, ("hyperx", 7), ("brio", 3), ("nvidia", 6), ("mix", 1));
        Render("setup-mic-more-expanded", rig);
    }

    [Fact]
    public void MicBlocked()
    {
        var rig = new SetupRig(Devices.Laptop(), Devices.Buds());
        rig.Privacy.Allowed = false;
        rig.Model.GetStarted();
        Assert.True(rig.Model.IsBlocked);
        Render("setup-mic-blocked", rig);
    }

    [Fact]
    public void TryIt()
    {
        var rig = TryItRig();
        rig.Practice.Phase(DictationPhase.Recording);
        foreach (var step in Wave) rig.Practice.Rms(RmsFor(step));
        rig.Practice.Text("Okay, this is actually really smooth.", false);
        Render("setup-tryit", rig);
    }

    [Fact]
    public void TryItDownloading()
    {
        var rig = TryItRig();
        rig.Model.SetModel(ModelState.Loading, 0.62);
        Render("setup-tryit-downloading", rig);
    }

    [Fact]
    public void TryItDone()
    {
        var rig = TryItRig();
        rig.Practice.Phase(DictationPhase.Recording);
        rig.Practice.Text("Okay, this is actually really smooth.", true);
        rig.Practice.Phase(DictationPhase.Idle);
        Render("setup-tryit-done", rig);
    }

    private static SetupRig Microphone(SetupRig rig)
    {
        rig.Model.GetStarted();
        return rig;
    }

    private static SetupRig TryItRig()
    {
        var rig = Microphone(new SetupRig(Devices.Laptop(), Devices.Buds(), Devices.Webcam()));
        rig.Model.Continue();
        return rig;
    }

    /// <summary>An RMS in the middle of <paramref name="step"/>'s band (steps span 40/7 dB from -50 dBFS).</summary>
    private static float RmsFor(int step) => (float)Math.Pow(10, (-50 + (step - 0.5) * 40 / 7.0) / 20);

    private static void Render(string name, SetupRig rig)
    {
        SnapshotHarness.Render(
            () =>
            {
                var page = new Grid { Background = new SolidColorBrush(Color.FromRgb(0x04, 0x06, 0x0D)) };
                page.Children.Add(new SetupView
                {
                    DataContext = rig.Model,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(40, 40, 0, 0),
                });
                return page;
            },
            Width, Height, name, PillSnapshots.Theme);
    }
}
