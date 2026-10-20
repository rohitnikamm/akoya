using Xunit;

namespace Akoya.Core.Tests;

public class SmokeTests
{
    [Fact]
    public void Core_IsReachable()
    {
        Assert.NotNull(typeof(Akoya.Core.Marker).Assembly);
    }

    [Fact]
    public void TEMP_Fails() => Assert.Equal(1, 2);
}
