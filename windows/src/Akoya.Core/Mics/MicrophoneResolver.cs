namespace Akoya.Core.Mics;

/// <summary>The outcome of resolving the saved microphone. <c>Missing</c> means a saved ID is no longer present.</summary>
public sealed record MicChoice(NamedMic? Mic, bool Missing);

public static class MicrophoneResolver
{
    /// <summary>
    /// <paramref name="savedId"/> null follows Windows (the default mic). A saved ID that is present wins;
    /// one that is absent gets no fallback. <c>Missing</c> is true whenever there is no usable mic
    /// (saved mic gone, no default mic, or no mic at all).
    /// </summary>
    public static MicChoice Resolve(string? savedId, IReadOnlyList<NamedMic> mics)
    {
        if (savedId is null)
        {
            var follow = mics.FirstOrDefault(m => m.IsDefault);
            return new MicChoice(follow, follow is null);
        }

        var saved = mics.FirstOrDefault(m => string.Equals(m.Id, savedId, StringComparison.OrdinalIgnoreCase));
        return saved is null ? new MicChoice(null, true) : new MicChoice(saved, false);
    }
}
