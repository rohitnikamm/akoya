namespace Akoya.Core.Mics;

/// <summary>Maps a level step (1 to 8) onto the dots of a setup row meter.</summary>
public static class MeterLevel
{
    public static int LitDots(int step, int dots = 10)
    {
        var s = Math.Min(8, Math.Max(1, step));
        return (int)Math.Round((s - 1) / 7.0 * dots, MidpointRounding.AwayFromZero);
    }
}
