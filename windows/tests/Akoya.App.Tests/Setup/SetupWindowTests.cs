using System.Windows;
using Akoya.App.Setup;
using Akoya.App.Tests.Snapshots;
using Akoya.Core.Dictation;
using Xunit;

namespace Akoya.App.Tests.Setup;

public class SetupWindowTests
{
    private static (SetupWindow Window, SetupRig Rig, List<string> Events) Open()
    {
        var rig = new SetupRig(Devices.Laptop(), Devices.Buds());
        var events = new List<string>();
        rig.Model.Finished += () => events.Add("finished");
        var window = new SetupWindow(rig.Model);
        window.Closed += (_, _) => events.Add("closed");
        window.Show();
        return (window, rig, events);
    }

    [Fact]
    public void HostCloseOrDismiss_DoesNotFinishSetup_ButTearsDown()
    {
        SnapshotHarness.RunSta(() =>
        {
            var (window, rig, events) = Open();
            rig.Model.GetStarted();
            Assert.True(rig.Levels.Running);

            window.Close();

            Assert.Equal(["closed"], events);
            Assert.False(rig.Levels.Running);
            Assert.Equal(0, rig.Clock.Pending);

            var (second, _, secondEvents) = Open();
            second.Dismiss();
            Assert.Equal(["closed"], secondEvents);
        });
    }

    [Fact]
    public void CloseOnTryIt_EndsPracticeWithoutFinishing()
    {
        SnapshotHarness.RunSta(() =>
        {
            var (window, rig, events) = Open();
            rig.Model.GetStarted();
            rig.Model.Continue();

            window.Close();

            Assert.Equal(["closed"], events);
            Assert.Equal(1, rig.Practice.Ends);
        });
    }

    [Fact]
    public void Skip_FinishesOnce_AndClosesTheWindow()
    {
        SnapshotHarness.RunSta(() =>
        {
            var (window, rig, events) = Open();

            rig.Model.SkipCommand.Execute(null);

            Assert.Equal(["finished", "closed"], events);
            Assert.False(window.IsVisible);
        });
    }

    [Fact]
    public void Done_FinishesAndCloses()
    {
        SnapshotHarness.RunSta(() =>
        {
            var (window, rig, events) = Open();
            rig.Model.GetStarted();
            rig.Model.Continue();
            rig.Practice.Phase(DictationPhase.Recording);
            rig.Practice.Text("Hello.", true);
            rig.Practice.Phase(DictationPhase.Idle);

            rig.Model.PrimaryAction();

            Assert.Equal(["finished", "closed"], events);
            Assert.False(window.IsVisible);
        });
    }

    [Fact]
    public void CardIsCentredOnThePrimaryWorkArea()
    {
        SnapshotHarness.RunSta(() =>
        {
            var (window, _, _) = Open();
            var area = SystemParameters.WorkArea;
            var cardCentreX = window.Left + SetupWindow.ShadowPadding.Left + 190;
            var cardCentreY = window.Top + SetupWindow.ShadowPadding.Top + 260;
            Assert.InRange(cardCentreX, area.Left + area.Width / 2 - 1, area.Left + area.Width / 2 + 1);
            Assert.InRange(cardCentreY, area.Top + area.Height / 2 - 1, area.Top + area.Height / 2 + 1);
            window.Close();
        });
    }
}
