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
                    settings = settings with { MicrophoneId = value.ValueKind == JsonValueKind.String ? value.GetString() : Defaults.MicrophoneId };
                else if (Is(property, "launchAtStartup"))
                    settings = settings with { LaunchAtStartup = ReadBool(value, Defaults.LaunchAtStartup) };
                else if (Is(property, "setupDone"))
                    settings = settings with { SetupDone = ReadBool(value, Defaults.SetupDone) };
                else if (Is(property, "provider"))
                    settings = settings with { Provider = ReadEnum(value, Defaults.Provider) };
            }
            return settings;
        }
    }

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
                writer.WriteEndObject();
                writer.Flush();
                stream.Flush(true);
            }
            File.Move(tempPath, path, overwrite: true);
        }
        catch
        {
            try { File.Delete(tempPath); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
    }

    private static bool Is(JsonProperty property, string name) =>
        property.NameEquals(name) || string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase);

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
