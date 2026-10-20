using System.Diagnostics;
using Akoya.App.Platform;
using Xunit;

namespace Akoya.App.Tests;

public class ElevationCheckTests
{
    [Fact]
    public void CurrentProcess_IsDeterminable_AndMatchesTheLazyCheck()
    {
        var elevated = ElevationCheck.IsProcessElevated(Environment.ProcessId);

        Assert.NotNull(elevated);
        Assert.Equal(elevated, ElevationCheck.IsCurrentProcessElevated());
    }

    [Fact]
    public void ForegroundIsElevated_NeverThrows_AndIsFalseWhenAkoyaIsElevated()
    {
        var foreground = ElevationCheck.ForegroundIsElevated();

        if (ElevationCheck.IsCurrentProcessElevated())
        {
            Assert.False(foreground);
        }
    }

    [Fact]
    public void ChildProcess_OfThisProcess_HasTheSameElevation()
    {
        using var child = Process.Start(new ProcessStartInfo("cmd.exe", "/c ping -n 3 127.0.0.1 > nul") { CreateNoWindow = true })!;
        try
        {
            Assert.Equal(ElevationCheck.IsCurrentProcessElevated(), ElevationCheck.IsProcessElevated(child.Id));
        }
        finally
        {
            child.Kill(true);
        }
    }
}
