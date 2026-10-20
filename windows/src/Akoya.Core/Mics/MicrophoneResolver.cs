namespace Akoya.Core.Mics;

/// <summary>The outcome of resolving the saved microphone. <c>Missing</c> means a saved ID is no longer present.</summary>
public sealed record MicChoice(NamedMic? Mic, bool Missing);

public static class MicrophoneResolver
{
    /// <summary>
    /// <paramref name="savedId"/> null follows Windows (the default mic, or none if no mic is flagged default).
    /// A saved ID that is present wins; one that is absent is reported missing, with no fallback.
    /// </summary>
    public static MicChoice Resolve(string? savedId, IReadOnlyList<NamedMic> mics)
    {
        if (savedId is null)
        {
            return new MicChoice(mics.FirstOrDefault(m => m.IsDefault), false);
        }

        var saved = mics.FirstOrDefault(m => string.Equals(m.Id, savedId, StringComparison.OrdinalIgnoreCase));
        return saved is null ? new MicChoice(null, true) : new MicChoice(saved, false);
    }
}
