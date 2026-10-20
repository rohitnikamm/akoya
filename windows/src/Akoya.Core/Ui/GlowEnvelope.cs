namespace Akoya.Core.Ui;

/// <summary>
/// Smooths the voice level that drives the rim glow so it breathes rather than flickers:
/// a one-pole smoother with a fast attack and a slower release (alpha = 1 - exp(-dt / tau)).
/// </summary>
public sealed class GlowEnvelope
{
    private readonly double _attack;
    private readonly double _release;

    public GlowEnvelope(double attack = 0.10, double release = 0.45)
    {
        _attack = attack;
        _release = release;
    }

    public double Value { get; private set; }

    /// <summary>Moves the smoothed value toward <paramref name="target"/> over <paramref name="dt"/> seconds and returns it.</summary>
    public double Step(double target, double dt)
    {
        if (!(dt > 0)) return Value;
        var tau = target > Value ? _attack : _release;
        var alpha = tau > 0 ? 1 - Math.Exp(-dt / tau) : 1;
        Value += (target - Value) * alpha;
        return Value;
    }

    /// <summary>
    /// Glow opacity for a smoothed voice level (0 to 1): 0.2 + 0.6 x level while recording,
    /// 0 at rest, and a fixed 0.5 while recording with reduced motion on.
    /// </summary>
    public static double Opacity(double level, bool recording, bool reduceMotion)
    {
        if (!recording) return 0;
        if (reduceMotion) return 0.5;
        return 0.2 + 0.6 * Math.Min(Math.Max(level, 0), 1);
    }
}
