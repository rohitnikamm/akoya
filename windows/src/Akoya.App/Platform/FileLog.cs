using System.Globalization;
using System.IO;
using System.Text;

namespace Akoya.App.Platform;

/// <summary>
/// <c>%LocalAppData%\Akoya\logs\akoya.log</c>, rolled at 1 MB into <c>akoya.1.log</c> and <c>akoya.2.log</c> (three
/// files at most). Thread-safe; logging never throws. Callers never pass transcripts: exceptions are logged as type
/// and message only.
/// </summary>
public sealed class FileLog : ILog
{
    public const long MaxBytes = 1024 * 1024;
    public const int Files = 3;

    private static readonly Encoding NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private readonly object _gate = new();
    private readonly string _directory;
    private readonly long _maxBytes;

    public FileLog(string directory, long maxBytes = MaxBytes)
    {
        _directory = directory;
        _maxBytes = maxBytes;
    }

    public static string DefaultDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Akoya", "logs");

    public string CurrentPath => Path.Combine(_directory, "akoya.log");

    public void Info(string message) => Write("INFO", message);

    public void Error(string message, Exception? exception = null) =>
        Write("ERROR", exception is null ? message : $"{message}: {Describe(exception)}");

    /// <summary>"InvalidOperationException: message" (inner exceptions appended); no stack, no data.</summary>
    public static string Describe(Exception exception)
    {
        var text = new StringBuilder();
        for (var e = exception; e is not null; e = e.InnerException)
        {
            if (text.Length > 0) text.Append(" <- ");
            text.Append(e.GetType().Name).Append(": ").Append(e.Message);
        }
        return text.ToString();
    }

    private void Write(string level, string message)
    {
        var line = string.Create(CultureInfo.InvariantCulture,
            $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} {level} {message}{Environment.NewLine}");
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(_directory);
                var path = CurrentPath;
                var info = new FileInfo(path);
                if (info.Exists && info.Length + line.Length > _maxBytes) Roll();
                File.AppendAllText(path, line, NoBom);
            }
            catch (Exception)
            {
                // A log that can't be written must not take the app down.
            }
        }
    }

    private void Roll()
    {
        for (var i = Files - 1; i >= 1; i--)
        {
            var source = i == 1 ? CurrentPath : Path.Combine(_directory, $"akoya.{i - 1}.log");
            var target = Path.Combine(_directory, $"akoya.{i}.log");
            if (File.Exists(source)) File.Move(source, target, overwrite: true);
        }
    }
}
