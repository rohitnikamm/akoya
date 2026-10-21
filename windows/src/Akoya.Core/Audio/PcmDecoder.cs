namespace Akoya.Core.Audio;

/// <summary>Converts the raw bytes of a capture buffer into float samples in -1...1.</summary>
public static class PcmDecoder
{
    public static bool IsSupported(int bitsPerSample, bool isFloat) =>
        isFloat ? bitsPerSample == 32 : bitsPerSample is 16 or 24 or 32;

    /// <summary>Interleaved samples, same channel order as the input. A trailing partial sample is ignored.</summary>
    public static float[] ToFloat(ReadOnlySpan<byte> bytes, int bitsPerSample, bool isFloat)
    {
        if (!IsSupported(bitsPerSample, isFloat))
            throw new NotSupportedException($"{bitsPerSample}-bit {(isFloat ? "float" : "PCM")} audio is not supported.");

        var width = bitsPerSample / 8;
        var count = bytes.Length / width;
        var result = new float[count];
        for (var i = 0; i < count; i++)
        {
            var b = bytes.Slice(i * width, width);
            result[i] = (isFloat, bitsPerSample) switch
            {
                (true, _) => BitConverter.ToSingle(b),
                (false, 16) => BitConverter.ToInt16(b) / 32768f,
                (false, 24) => (((b[0] << 8) | (b[1] << 16) | (b[2] << 24)) >> 8) / 8388608f,
                _ => BitConverter.ToInt32(b) / 2147483648f,
            };
        }
        return result;
    }
}
