using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Akoya.App.Tray;
using Akoya.Core.Dictation;
using Akoya.Core.Mics;
using Xunit;

namespace Akoya.App.Tests.Snapshots;

/// <summary>
/// The tray panel in each state, on the mockup's page colour (docs/design/all-in-one-dropdown-target.html, #05070d)
/// with room for its drop shadow, for side-by-side comparison with the mockups.
/// </summary>
public class TrayPanelSnapshots
{
    private const double Width = 360;
    private const double Height = 640;
    private const string LastText = "Ship the release notes after lunch, then ping the team.";

    internal static readonly NamedMic Laptop = new("{laptop}", "This PC's microphone", InMore: false, IsDefault: true);
    internal static readonly NamedMic HyperX = new("{hyperx}", "HyperX Cloud III", InMore: false, IsDefault: false);
    internal static readonly NamedMic Webcam = new("{webcam}", "Webcam mic", InMore: false, IsDefault: false);

    [Fact]
    public void Ready() => Render("tray-ready", _ => { });

    [Fact]
    public void Listening() => Render("tray-listening", vm =>
    {
        vm.SetPhase(DictationPhase.Recording);
        vm.SetVoiceLevel(1.0); // peak voice: full strength, as in docs/design/blue-glow-target.png
    });

    [Fact]
    public void LastDictation() => Render("tray-last-dictation", vm => vm.SetLastDictation(LastText));

    [Fact]
    public void MicMissing() => Render("tray-mic-missing", vm =>
    {
        vm.SetMicrophones([Laptop, Webcam], savedId: HyperX.Id, savedName: HyperX.Name);
        vm.SetLastDictation(LastText);
    });

    [Fact]
    public void UpdateReady() => Render("tray-update-ready", vm =>
    {
        vm.SetLastDictation(LastText);
        vm.SetUpdateReady(true);
    });

    [Fact]
    public void HotkeyMenuOpen() => Render("tray-hotkey-menu-open", vm =>
    {
        vm.SetLastDictation(LastText);
        vm.ToggleHotkeyMenu.Execute(null);
    });

    [Fact]
    public void MicMenuOpen() => Render("tray-mic-menu-open", vm =>
    {
        vm.SetLastDictation(LastText);
        vm.ToggleMicMenu.Execute(null);
    });

    [Fact]
    public void PillRow() => Render("tray-pill-row", _ => { });

    [Fact]
    public void PillRowOpen() => Render("tray-pill-row-open", vm => vm.TogglePillOptions.Execute(null));

    [Fact]
    public void Downloading() => Render("tray-downloading", vm => vm.SetModel(ModelState.Loading, 0.62));

    [Fact]
    public void ModelFailed() => Render("tray-model-failed", vm => vm.SetModel(ModelState.Failed));

    /// <summary>A panel with three mics (the laptop's the default and selected), Launch at startup on.</summary>
    internal static TrayPanelViewModel Model(Action<TrayPanelViewModel> setup)
    {
        var vm = new TrayPanelViewModel(reduceMotion: () => false); // the breathing glow, whatever the runner sets
        vm.SetMicrophones([Laptop, HyperX, Webcam], savedId: null);
        vm.LaunchAtStartup = true;
        setup(vm);
        return vm;
    }

    private static void Render(string name, Action<TrayPanelViewModel> setup)
    {
        SnapshotHarness.Render(
            () =>
            {
                var scene = new Grid { Background = new SolidColorBrush(Color.FromRgb(0x05, 0x07, 0x0D)), ClipToBounds = true };
                scene.Children.Add(new TrayPanelView
                {
                    DataContext = Model(setup),
                    Margin = new Thickness(30, 30, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top,
                });
                return scene;
            },
            Width, Height, name, PillSnapshots.Theme);
    }
}
