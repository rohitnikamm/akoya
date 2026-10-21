using Xunit;

namespace Akoya.App.Tests;

public class SpeechModelTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(8, 4)]
    [InlineData(16, 8)]
    [InlineData(32, 8)]  // Rohit's PC: 8 threads transcribe a short dictation faster than 16 (GPU spike, 2026-10-21)
    [InlineData(64, 8)]
    public void Threads_HalfTheLogicalCores_AtMostEight(int processors, int expected) =>
        Assert.Equal(expected, SpeechModel.ThreadsFor(processors));
}
