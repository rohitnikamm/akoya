using System.Text;
using System.Text.Json;
using Akoya.Core.Hotkey;

namespace Akoya.Core.Settings;

/// <summary>
/// Reads and writes <see cref="AkoyaSettings"/> as camelCase JSON with enums as strings. Load never
/// throws for content problems: a missing or unparseable file yields defaults, and each field falls
/// back to its own default when it is missing, of the wrong type, or an unknown enum name. Save
/// writes a sibling .tmp file and then moves it over the target, so a crash never leaves a
/// half-written settings file.
/// </summary>
public static class SettingsStore
{
    private static readonly AkoyaSettings Defaults = new();

    /// <summary>
    /// Loads settings, falling back to defaults for a missing or corrupt file and per field for bad
    /// values. IO errors other than file-not-found (for example a sharing violation or access denied)
    /// propagate. Callers must not <see cref="Save"/> after a Load that threw, or they would overwrite
    /// a file they could not read with defaults.
    /// </summary>
    public static AkoyaSettings Load(string path)
    {
        string text;
        try
        {
            text = File.ReadAllText(path, Encoding.UTF8);
        }
        catch (FileNotFoundException)
        {
            return Defaults;
        }
        catch (DirectoryNotFoundException)
        {
            return Defaults;
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(text);
        }
        catch (JsonException)
        {
            return Defaults;
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return Defaults;

            var settings = Defaults;
            foreach (var property in doc.RootElement.EnumerateObject())
            {
                var value = property.Value;
                if (Is(property, "hotkey"))
                    settings = settings with { Hotkey = ReadEnum(value, Defaults.Hotkey) };
                else if (Is(property, "microphoneId"))
                    settings = settings with { MicrophoneId = ReadMicrophoneId(value) };
                else if (Is(property, "launchAtStartup"))
                    settings = settings with { LaunchAtStartup = ReadBool(value, Defaults.LaunchAtStartup) };
                else if (Is(property, "setupDone"))
                    settings = settings with { SetupDone = ReadBool(value, Defaults.SetupDone) };
                else if (Is(property, "provider"))
                    settings = settings with { Provider = ReadEnum(value, Defaults.Provider) };
                else if (Is(property, "look"))
                    settings = settings with { Look = ReadEnum(value, Defaults.Look) };
                else if (Is(property, "redTint"))
                    settings = settings with { RedTint = ReadBool(value, Defaults.RedTint) };
            }
            return settings;
        }
    }

    /// <summary>
    /// Atomically replaces the settings file. Call from one thread at a time: concurrent saves to the
    /// same path share the same .tmp file and can clobber each other. The replace is retried briefly
    /// (5 attempts) if another process holds the file, then the last error is rethrown and the
    /// original file is left intact.
    /// </summary>
    public static void Save(string path, AkoyaSettings settings)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var tempPath = path + ".tmp";
        try
        {
            using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
            {
                writer.WriteStartObject();
                writer.WriteString("hotkey", settings.Hotkey.ToString());
                if (settings.MicrophoneId is null)
                    writer.WriteNull("microphoneId");
                else
                    writer.WriteString("microphoneId", settings.MicrophoneId);
                writer.WriteBoolean("launchAtStartup", settings.LaunchAtStartup);
                writer.WriteBoolean("setupDone", settings.SetupDone);
                writer.WriteString("provider", settings.Provider.ToString());
                writer.WriteString("look", settings.Look.ToString());
                writer.WriteBoolean("redTint", settings.RedTint);
                writer.WriteEndObject();
                writer.Flush();
                stream.Flush(true);
            }
            MoveWithRetry(tempPath, path);
        }
        catch
        {
            try { File.Delete(tempPath); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
    }

    // Antivirus and indexers can briefly hold a freshly written file open on Windows, which makes the
    // replace fail with a sharing violation. Retry a few times before giving up.
    private const int MoveAttempts = 5;

    private static void MoveWithRetry(string source, string destination)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(source, destination, overwrite: true);
                return;
            }
            catch (Exception e) when (attempt < MoveAttempts && e is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(Random.Shared.Next(20, 51));
            }
        }
    }

    private static bool Is(JsonProperty property, string name) =>
        property.NameEquals(name) || string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase);

    // A string is the only valid shape; an empty string means "no microphone chosen", same as null.
    private static string? ReadMicrophoneId(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String)
            return Defaults.MicrophoneId;
        var id = value.GetString();
        return string.IsNullOrEmpty(id) ? null : id;
    }

    private static bool ReadBool(JsonElement value, bool fallback) => value.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => fallback,
    };

    private static T ReadEnum<T>(JsonElement value, T fallback) where T : struct, Enum
    {
        if (value.ValueKind != JsonValueKind.String)
            return fallback;
        var name = value.GetString();
        // IsDefined rejects numeric strings such as "7" that Enum.TryParse would accept.
        return Enum.TryParse<T>(name, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed) && !IsNumeric(name)
            ? parsed
            : fallback;
    }

    private static bool IsNumeric(string? name) =>
        !string.IsNullOrEmpty(name) && (char.IsDigit(name[0]) || name[0] == '-' || name[0] == '+');
}
