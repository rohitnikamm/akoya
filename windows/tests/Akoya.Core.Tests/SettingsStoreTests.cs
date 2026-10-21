using Akoya.Core.Hotkey;
using Akoya.Core.Settings;
using Xunit;

namespace Akoya.Core.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "akoya-settings-" + Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(_dir, "settings.json");

    private void WriteRaw(string json)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, json);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void RoundTripsEveryField()
    {
        var settings = new AkoyaSettings(HotkeyKey.Copilot, "{0.0.1.00000000}.{abc}", true, true, InferenceProvider.Cpu,
            WaveLook.Solid, RedTint: true);
        SettingsStore.Save(FilePath, settings);
        Assert.Equal(settings, SettingsStore.Load(FilePath));
    }

    [Fact]
    public void PillOptions_DefaultToPixelWithoutRedTint()
    {
        var settings = new AkoyaSettings();
        Assert.Equal(WaveLook.Pixel, settings.Look);
        Assert.False(settings.RedTint);
    }

    [Fact]
    public void PillOptions_WrittenAsLookAndRedTint()
    {
        SettingsStore.Save(FilePath, new AkoyaSettings(Look: WaveLook.Solid, RedTint: true));
        var json = File.ReadAllText(FilePath);
        Assert.Contains("\"look\": \"Solid\"", json);
        Assert.Contains("\"redTint\": true", json);
    }

    [Fact]
    public void FileFromBeforeThePillOptions_LoadsWithTheirDefaults()
    {
        WriteRaw("{\"hotkey\":\"RightCtrl\",\"microphoneId\":null,\"launchAtStartup\":true,\"setupDone\":true,\"provider\":\"Auto\"}");
        Assert.Equal(new AkoyaSettings(HotkeyKey.RightCtrl, null, true, true, InferenceProvider.Auto, WaveLook.Pixel, false),
            SettingsStore.Load(FilePath));
    }

    [Theory]
    [InlineData("{\"look\":\"Wavy\",\"redTint\":\"yes\",\"setupDone\":true}")]
    [InlineData("{\"look\":1,\"redTint\":1,\"setupDone\":true}")]
    [InlineData("{\"look\":\"1\",\"redTint\":null,\"setupDone\":true}")]
    public void BadPillOptions_FallBackIndependently(string json)
    {
        WriteRaw(json);
        Assert.Equal(new AkoyaSettings(SetupDone: true), SettingsStore.Load(FilePath));
    }

    [Fact]
    public void PillOptions_ReadCaseInsensitively()
    {
        WriteRaw("{\"look\":\"solid\",\"redTint\":true}");
        Assert.Equal(new AkoyaSettings(Look: WaveLook.Solid, RedTint: true), SettingsStore.Load(FilePath));
    }

    [Fact]
    public void RoundTripsDefaultsWithNullMicrophone()
    {
        SettingsStore.Save(FilePath, new AkoyaSettings());
        Assert.Equal(new AkoyaSettings(), SettingsStore.Load(FilePath));
    }

    [Fact]
    public void WritesCamelCaseNamesAndEnumStrings()
    {
        SettingsStore.Save(FilePath, new AkoyaSettings(HotkeyKey.RightCtrl, "mic", true, true, InferenceProvider.Cpu));
        var json = File.ReadAllText(FilePath);
        Assert.Contains("\"hotkey\": \"RightCtrl\"", json);
        Assert.Contains("\"microphoneId\": \"mic\"", json);
        Assert.Contains("\"launchAtStartup\": true", json);
        Assert.Contains("\"setupDone\": true", json);
        Assert.Contains("\"provider\": \"Cpu\"", json);
    }

    [Fact]
    public void MissingFileGivesDefaults()
    {
        Assert.Equal(new AkoyaSettings(), SettingsStore.Load(FilePath));
    }

    [Fact]
    public void MissingDirectoryGivesDefaults()
    {
        Assert.Equal(new AkoyaSettings(), SettingsStore.Load(Path.Combine(_dir, "nope", "settings.json")));
    }

    [Fact]
    public void TruncatedJsonGivesDefaults()
    {
        WriteRaw("{\"hotkey\":\"Copilot\",\"setupDone\":tr");
        Assert.Equal(new AkoyaSettings(), SettingsStore.Load(FilePath));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("[1,2,3]")]
    [InlineData("\"hotkey\"")]
    [InlineData("null")]
    public void NonObjectOrGarbageGivesDefaults(string content)
    {
        WriteRaw(content);
        Assert.Equal(new AkoyaSettings(), SettingsStore.Load(FilePath));
    }

    [Fact]
    public void UnknownHotkeyFallsBackButOtherFieldsAreKept()
    {
        WriteRaw("{\"hotkey\":\"LeftWin\",\"microphoneId\":\"mic-1\",\"launchAtStartup\":true,\"setupDone\":true,\"provider\":\"Cpu\"}");
        var loaded = SettingsStore.Load(FilePath);
        Assert.Equal(new AkoyaSettings(HotkeyKey.RightAlt, "mic-1", true, true, InferenceProvider.Cpu), loaded);
    }

    [Fact]
    public void UnknownProviderFallsBackButOtherFieldsAreKept()
    {
        WriteRaw("{\"hotkey\":\"Copilot\",\"provider\":\"Gpu9000\",\"setupDone\":true}");
        var loaded = SettingsStore.Load(FilePath);
        Assert.Equal(new AkoyaSettings(HotkeyKey.Copilot, null, false, true, InferenceProvider.Auto), loaded);
    }

    [Fact]
    public void WrongTypedFieldsFallBackIndependently()
    {
        WriteRaw("{\"hotkey\":2,\"microphoneId\":42,\"launchAtStartup\":\"yes\",\"setupDone\":true,\"provider\":\"Cpu\"}");
        var loaded = SettingsStore.Load(FilePath);
        Assert.Equal(new AkoyaSettings(HotkeyKey.RightAlt, null, false, true, InferenceProvider.Cpu), loaded);
    }

    [Fact]
    public void NumericEnumStringsAreRejected()
    {
        WriteRaw("{\"hotkey\":\"1\",\"provider\":\"99\",\"setupDone\":true}");
        var loaded = SettingsStore.Load(FilePath);
        Assert.Equal(new AkoyaSettings(HotkeyKey.RightAlt, null, false, true, InferenceProvider.Auto), loaded);
    }

    [Fact]
    public void NullFieldsFallBackToDefaults()
    {
        WriteRaw("{\"hotkey\":null,\"microphoneId\":null,\"launchAtStartup\":null,\"setupDone\":true,\"provider\":null}");
        Assert.Equal(new AkoyaSettings(SetupDone: true), SettingsStore.Load(FilePath));
    }

    [Fact]
    public void MissingFieldsUseDefaults()
    {
        WriteRaw("{\"hotkey\":\"RightCtrl\"}");
        Assert.Equal(new AkoyaSettings(HotkeyKey.RightCtrl), SettingsStore.Load(FilePath));
    }

    [Fact]
    public void UnknownFieldsAreIgnored()
    {
        WriteRaw("{\"hotkey\":\"Copilot\",\"futureThing\":{\"a\":[1,2]},\"theme\":\"dark\",\"setupDone\":true}");
        Assert.Equal(new AkoyaSettings(HotkeyKey.Copilot, SetupDone: true), SettingsStore.Load(FilePath));
    }

    [Fact]
    public void SaveCreatesMissingDirectories()
    {
        var nested = Path.Combine(_dir, "a", "b", "settings.json");
        SettingsStore.Save(nested, new AkoyaSettings(SetupDone: true));
        Assert.True(File.Exists(nested));
        Assert.True(SettingsStore.Load(nested).SetupDone);
    }

    [Fact]
    public void SaveReplacesExistingFileAndLeavesNoTempFile()
    {
        SettingsStore.Save(FilePath, new AkoyaSettings(HotkeyKey.RightCtrl, "old", false, false, InferenceProvider.Auto));
        SettingsStore.Save(FilePath, new AkoyaSettings(HotkeyKey.Copilot, "new", true, true, InferenceProvider.Cpu));

        Assert.Equal(new AkoyaSettings(HotkeyKey.Copilot, "new", true, true, InferenceProvider.Cpu), SettingsStore.Load(FilePath));
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
        Assert.Equal(new[] { FilePath }, Directory.GetFiles(_dir));
    }

    [Fact]
    public void SaveOverwritesACorruptFile()
    {
        WriteRaw("{{{{ corrupt");
        SettingsStore.Save(FilePath, new AkoyaSettings(HotkeyKey.Copilot));
        Assert.Equal(HotkeyKey.Copilot, SettingsStore.Load(FilePath).Hotkey);
    }

    [Fact]
    public void SaveIgnoresAStaleTempFile()
    {
        WriteRaw("{}");
        File.WriteAllText(FilePath + ".tmp", "stale garbage");
        SettingsStore.Save(FilePath, new AkoyaSettings(SetupDone: true));
        Assert.True(SettingsStore.Load(FilePath).SetupDone);
        Assert.False(File.Exists(FilePath + ".tmp"));
    }

    [Fact]
    public void FileWithUtf8BomLoads()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "{\"hotkey\":\"Copilot\"}", new System.Text.UTF8Encoding(true));
        Assert.Equal(HotkeyKey.Copilot, SettingsStore.Load(FilePath).Hotkey);
    }

    [Theory]
    [InlineData("{\"microphoneId\":\"\"}")]
    [InlineData("{\"microphoneId\":null}")]
    public void EmptyOrNullMicrophoneIdLoadsAsNull(string json)
    {
        WriteRaw(json);
        Assert.Null(SettingsStore.Load(FilePath).MicrophoneId);
    }

    [Fact]
    public void SaveThrowsWhenTargetStaysLockedAndLeavesOriginalIntact()
    {
        if (!OperatingSystem.IsWindows())
            return; // Replacing an open file only fails on Windows.

        SettingsStore.Save(FilePath, new AkoyaSettings(HotkeyKey.RightCtrl, "original", false, false, InferenceProvider.Auto));
        var before = File.ReadAllText(FilePath);

        using (new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.ThrowsAny<Exception>(() => SettingsStore.Save(FilePath, new AkoyaSettings(HotkeyKey.Copilot, "new", true, true, InferenceProvider.Cpu)));
        }

        Assert.Equal(before, File.ReadAllText(FilePath));
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }

    [Fact]
    public void SaveSucceedsWhenLockIsReleasedDuringRetries()
    {
        if (!OperatingSystem.IsWindows())
            return; // Replacing an open file only fails on Windows.

        SettingsStore.Save(FilePath, new AkoyaSettings(HotkeyKey.RightCtrl, "original", false, false, InferenceProvider.Auto));

        var holder = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.None);
        var release = new Thread(() =>
        {
            Thread.Sleep(30);
            holder.Dispose();
        });
        release.Start();
        try
        {
            SettingsStore.Save(FilePath, new AkoyaSettings(HotkeyKey.Copilot, "new", true, true, InferenceProvider.Cpu));
        }
        finally
        {
            release.Join();
            holder.Dispose();
        }

        Assert.Equal(new AkoyaSettings(HotkeyKey.Copilot, "new", true, true, InferenceProvider.Cpu), SettingsStore.Load(FilePath));
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }
}
