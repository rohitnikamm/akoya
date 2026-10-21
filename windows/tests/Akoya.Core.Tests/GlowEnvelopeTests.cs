using Akoya.Core.Ui;
using Xunit;

namespace Akoya.Core.Tests;

public class GlowEnvelopeTests
{
    [Fact]
    public void OpacityAtRest_IsZero() =>
        Assert.Equal(0, GlowEnvelope.Opacity(0.9, recording: false, reduceMotion: false));

    [Fact]
    public void Opacity_MapsLevel()
    {
        Assert.Equal(0.8, GlowEnvelope.Opacity(1, true, false));
        Assert.Equal(0.2, GlowEnvelope.Opacity(0, true, false));
    }

    [Fact]
    public void Opacity_ClampsLevelOutsideUnitRange()
    {
        Assert.Equal(0.8, GlowEnvelope.Opacity(3, true, false));
        Assert.Equal(0.2, GlowEnvelope.Opacity(-1, true, false));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0.2, 0.25)]
    [InlineData(0.8, 1)]
    [InlineData(0.95, 1)]
    [InlineData(-0.1, 0)]
    public void LayerOpacity_PeakVoiceIsFullStrength(double opacity, double expected) =>
        Assert.Equal(expected, GlowEnvelope.LayerOpacity(opacity), 6);

    [Fact]
    public void ReduceMotion_IsConstant() =>
        Assert.Equal(0.5, GlowEnvelope.Opacity(0.1, true, true));

    [Fact]
    public void ReduceMotionAtRest_IsZero() =>
        Assert.Equal(0, GlowEnvelope.Opacity(0.1, false, true));

    [Fact]
    public void Attack_FasterThanRelease()
    {
        var rising = new GlowEnvelope();
        Assert.True(rising.Step(1, 0.1) >= 0.6);

        var falling = new GlowEnvelope();
        for (var i = 0; i < 600; i++) falling.Step(1, 1.0 / 60);
        Assert.True(falling.Step(0, 0.1) >= 0.6);
    }

    [Fact]
    public void Envelope_Converges()
    {
        var envelope = new GlowEnvelope();
        var value = 0.0;
        for (var i = 0; i < 120; i++) value = envelope.Step(1, 1.0 / 60);
        Assert.True(value > 0.99);
    }

    [Fact]
    public void NonPositiveDt_LeavesValueUnchanged()
    {
        var envelope = new GlowEnvelope();
        envelope.Step(1, 0.1);
        var before = envelope.Value;
        Assert.Equal(before, envelope.Step(0, 0));
        Assert.Equal(before, envelope.Step(0, -1));
    }
}
