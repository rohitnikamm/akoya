using System.IO;
using System.Windows.Threading;
using Akoya.App.Pill;
using Akoya.App.Platform;
using Akoya.App.Speech;
using Akoya.Core.Dictation;
using Akoya.Core.Hotkey;
using Akoya.Core.Settings;
using Microsoft.Win32;

namespace Akoya.App
{
    /// <summary><see cref="KeyboardHook"/> as an <see cref="IHotkeyHook"/>. Create it on the UI thread.</summary>
    public sealed class KeyboardHookPort : IHotkeyHook
    {
        private readonly KeyboardHook _hook;

        public KeyboardHookPort(HotkeyKey key, Action<HotkeyEvent, DateTimeOffset> onEvent) =>
            _hook = new KeyboardHook(new HotkeyDecoder(key), onEvent);

        public void Install() => _hook.Install();

        public void Reinstall() => _hook.Reinstall();

        public void SetKey(HotkeyKey key) => _hook.SetKey(key);

        public void SetRecordingActive(bool active) => _hook.SetRecordingActive(active);

        public void Reset() => _hook.Reset();

        public void Dispose() => _hook.Dispose();
    }

    /// <summary>Parakeet through <see cref="ModelStore"/> and <see cref="SherpaTranscriber"/>.</summary>
    public sealed class SpeechModel(ModelStore store) : ISpeechModel
    {
        private readonly ModelStore _store = store;

        /// <summary><c>%LocalAppData%\Akoya\models</c>.</summary>
        public static string DefaultRoot { get; } =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Akoya", "models");

        public bool IsInstalled => _store.IsInstalled;

        public Task EnsureAsync(IProgress<double> progress, CancellationToken ct) => _store.EnsureAsync(progress, ct);

        public ITranscriber Load(InferenceProvider provider)
        {
            var transcriber = new SherpaTranscriber(_store.ModelDirectory, provider, Math.Max(1, Environment.ProcessorCount / 2));
            try
            {
                transcriber.WarmUp(); // "Ready" only after a warm-up decode (spec, Model)
                return transcriber;
            }
            catch
            {
                transcriber.Dispose();
                throw;
            }
        }
    }

    /// <summary><see cref="SystemEvents.SessionSwitch"/>, marshalled to the UI thread.</summary>
    public sealed class SystemSessionEvents : ISessionEvents
    {
        private readonly Dispatcher _dispatcher;

        public SystemSessionEvents(Dispatcher dispatcher)
        {
            _dispatcher = dispatcher;
            SystemEvents.SessionSwitch += OnSessionSwitch;
        }

        public event Action? Locked;

        public event Action? Unlocked;

        public void Dispose() => SystemEvents.SessionSwitch -= OnSessionSwitch;

        private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
        {
            Action? raise = e.Reason switch
            {
                SessionSwitchReason.SessionLock or SessionSwitchReason.ConsoleDisconnect
                    or SessionSwitchReason.RemoteDisconnect or SessionSwitchReason.SessionLogoff => () => Locked?.Invoke(),
                SessionSwitchReason.SessionUnlock or SessionSwitchReason.ConsoleConnect
                    or SessionSwitchReason.RemoteConnect => () => Unlocked?.Invoke(),
                _ => null,
            };
            if (raise is not null) _dispatcher.BeginInvoke(raise);
        }
    }

    /// <summary>The real <see cref="PillWindow"/>. Create it on the UI thread.</summary>
    public sealed class PillPresenter : IPillPresenter
    {
        private readonly PillWindow _window = new();

        public PillWindow Window => _window;

        public void Show(PillViewModel model, bool retarget)
        {
            var wasShowing = _window.IsVisible;
            _window.ShowFor(model);
            if (retarget && wasShowing) _window.Retarget();
        }

        public void FadeOut() => _window.FadeOut();

        public void Dispose() => _window.Close();
    }

    /// <summary><see cref="Updater"/> as <see cref="IUpdates"/>.</summary>
    public sealed class UpdaterPort(Updater updater) : IUpdates
    {
        public Task<bool> CheckAndDownloadAsync() => updater.CheckAndDownloadAsync();

        public Exception? LastError => updater.LastError;

        public bool IsInstalled => updater.IsInstalled;

        public bool ScheduleApplyOnExit() => updater.ScheduleApplyOnExit();
    }

    /// <summary>The HKCU Run entry for the current launch path.</summary>
    public sealed class RegistryStartup : IStartupSetting
    {
        public bool IsEnabled() => StartupRegistration.IsEnabled();

        public void Set(bool enabled) => StartupRegistration.Set(enabled, StartupRegistration.LaunchPath);

        public void Refresh()
        {
            // A source checkout must not take over an installed copy's entry.
            if (StartupRegistration.IsInstalledCopy && IsEnabled()) Set(true);
        }
    }

    /// <summary>settings.json at a path (default <c>%AppData%\Akoya\settings.json</c>).</summary>
    public sealed class SettingsFile(string path) : ISettingsFile
    {
        public static string DefaultPath { get; } =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Akoya", "settings.json");

        public AkoyaSettings Load() => SettingsStore.Load(path);

        public void Save(AkoyaSettings settings) => SettingsStore.Save(path, settings);
    }
}

namespace Akoya.App.Platform
{
    public sealed partial class MicrophoneCatalog : IMicrophoneCatalog
    {
    }
}
