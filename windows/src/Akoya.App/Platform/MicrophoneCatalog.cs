using System.Text.RegularExpressions;
using Akoya.Core.Mics;
using NAudio.CoreAudioApi;

namespace Akoya.App.Platform;

/// <summary>
/// The active capture endpoints, as the facts <see cref="MicrophoneNaming"/> needs.
/// <para>
/// <see cref="Changed"/> is raised on a Windows audio worker thread (not the UI thread) when a
/// capture endpoint is added, removed, changes state, or the default capture device changes. The
/// handler must return quickly and only re-queue work (the App layer posts to the UI thread and
/// calls <see cref="Snapshot"/> there).
/// </para>
/// </summary>
public sealed partial class MicrophoneCatalog : IDisposable
{
    private readonly object _gate = new();
    private Action? _changed;
    private MMDeviceEnumerator? _watchEnumerator;
    private MMDeviceNotificationClient? _client;
    private bool _disposed;

    public event Action? Changed
    {
        add
        {
            lock (_gate)
            {
                _changed += value;
                EnsureWatching();
            }
        }
        remove
        {
            lock (_gate)
            {
                _changed -= value;
                if (_changed is null) StopWatching();
            }
        }
    }

    /// <summary>
    /// Every active capture endpoint right now. Never throws: if Windows audio is unavailable
    /// (service stopped, no devices) the result is empty.
    /// </summary>
    public IReadOnlyList<EndpointFacts> Snapshot()
    {
        var result = new List<EndpointFacts>();
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var defaultId = DefaultCaptureId(enumerator);
            using var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
            for (var i = 0; i < devices.Count; i++)
            {
                try
                {
                    using var device = devices[i];
                    result.Add(Describe(device, defaultId));
                }
                catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
                {
                    // This endpoint vanished or is unreadable; skip it, keep the rest.
                }
            }
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException
                                       or DllNotFoundException)
        {
            // No audio service or no enumerator: report what we have (possibly nothing).
        }
        return result;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _changed = null;
            StopWatching();
        }
    }

    // --- Facts -----------------------------------------------------------------------------

    private static string? DefaultCaptureId(MMDeviceEnumerator enumerator)
    {
        // Communications and Console usually agree; the Console default is the one Windows shows
        // as "Default device" in Sound settings, and the one the recorder uses for "system default".
        try
        {
            if (!enumerator.HasDefaultAudioEndpoint(DataFlow.Capture, Role.Console)) return null;
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
            return device.ID;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            return null;
        }
    }

    private static EndpointFacts Describe(MMDevice device, string? defaultId)
    {
        var id = device.ID;
        var friendly = ReadString(device, PropertyKeys.PKEY_Device_FriendlyName);
        if (friendly.Length == 0) friendly = SafeFriendlyName(device);
        var description = ReadString(device, PropertyKeys.PKEY_Device_DeviceDesc);
        var instanceId = ReadString(device, PropertyKeys.PKEY_Device_InstanceId);
        var parentHint = ReadString(device, PropertyKeys.PKEY_Device_ControllerDeviceId);
        var formFactor = MapFormFactor(ReadUInt(device, PropertyKeys.PKEY_AudioEndpoint_FormFactor));

        var enumerator = ParseEnumerator(instanceId);
        return new EndpointFacts(
            id,
            friendly,
            description,
            formFactor,
            ResolveEnumerator(instanceId, parentHint),
            HasPhysicalParent(enumerator, instanceId, parentHint),
            string.Equals(id, defaultId, StringComparison.Ordinal));
    }

    private static string SafeFriendlyName(MMDevice device)
    {
        try { return device.FriendlyName ?? ""; }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException) { return ""; }
    }

    private static object? ReadProperty(MMDevice device, PropertyKey key)
    {
        try
        {
            var store = device.Properties;
            return store.Contains(key) ? store[key].Value : null;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException
                                       or KeyNotFoundException)
        {
            return null;
        }
    }

    private static string ReadString(MMDevice device, PropertyKey key) =>
        (ReadProperty(device, key) as string)?.Trim() ?? "";

    private static uint? ReadUInt(MMDevice device, PropertyKey key)
    {
        var value = ReadProperty(device, key);
        try { return value is IConvertible c ? c.ToUInt32(null) : null; }
        catch (Exception ex) when (ex is FormatException or OverflowException) { return null; }
    }

    // --- Rules (public so they are unit-tested) --------------------------------------------

    /// <summary>The text before the first backslash of a device instance ID ("USB", "SWD", "HDAUDIO"...).</summary>
    public static string ParseEnumerator(string? instanceId)
    {
        if (string.IsNullOrWhiteSpace(instanceId)) return "";
        var text = instanceId.Trim();
        var slash = text.IndexOf('\\');
        return slash < 0 ? text : text[..slash];
    }

    [GeneratedRegex(@"(?<![A-Za-z0-9])(USB|BTH[A-Za-z]*|HDAUDIO|INTELAUDIO)(?![A-Za-z0-9])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PhysicalBusPattern();

    private static string? PhysicalBus(string? text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        var match = PhysicalBusPattern().Match(text);
        return match.Success ? match.Value.ToUpperInvariant() : null;
    }

    private static bool IsSoftwareBus(string enumerator) =>
        enumerator.Length == 0
        || string.Equals(enumerator, "SWD", StringComparison.OrdinalIgnoreCase)
        || string.Equals(enumerator, "ROOT", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The bus enumerator to hand to naming. A capture endpoint's own instance ID is normally
    /// <c>SWD\MMDEVAPI\...</c>, which says nothing about the hardware, so when the instance ID is
    /// a software bus (SWD, ROOT or empty) and the parent hint (<c>PKEY_Device_ControllerDeviceId</c>)
    /// or the instance ID names a USB, BTH*, HDAUDIO or INTELAUDIO parent, that bus is used instead.
    /// </summary>
    public static string ResolveEnumerator(string? instanceId, string? parentHint)
    {
        var own = ParseEnumerator(instanceId);
        if (!IsSoftwareBus(own)) return own;
        return PhysicalBus(instanceId) ?? PhysicalBus(parentHint) ?? own;
    }

    /// <summary>
    /// False for software-only endpoints. Rule: any enumerator other than SWD/ROOT/empty is a real
    /// bus; SWD and ROOT are virtual unless the instance ID or the parent hint references a USB,
    /// BTH*, HDAUDIO or INTELAUDIO device.
    /// </summary>
    public static bool HasPhysicalParent(string enumerator, string? instanceId, string? parentHint)
    {
        if (string.IsNullOrWhiteSpace(enumerator)) return false;
        if (!IsSoftwareBus(enumerator)) return true;
        return PhysicalBus(instanceId) is not null || PhysicalBus(parentHint) is not null;
    }

    /// <summary>
    /// Maps the Windows EndpointFormFactor values (2 line level, 3 headphones, 4 microphone, 5 headset,
    /// 9 digital audio display, 10 unknown); anything else, and any other number, is Other; a missing
    /// property is Unknown.
    /// </summary>
    public static EndpointFormFactor MapFormFactor(uint? raw) => raw switch
    {
        null => EndpointFormFactor.Unknown,
        2 => EndpointFormFactor.LineLevel,
        3 => EndpointFormFactor.Headphones,
        4 => EndpointFormFactor.Microphone,
        5 => EndpointFormFactor.Headset,
        9 => EndpointFormFactor.DigitalAudioDisplayDevice,
        10 => EndpointFormFactor.Unknown,
        _ => EndpointFormFactor.Other,
    };

    // --- Notifications ---------------------------------------------------------------------

    private void EnsureWatching()
    {
        if (_disposed || _client is not null) return;
        try
        {
            _watchEnumerator = new MMDeviceEnumerator();
            _client = _watchEnumerator.CreateNotificationClient(useSynchronizationContext: false);
            _client.DeviceAdded += OnDeviceEvent;
            _client.DeviceRemoved += OnDeviceEvent;
            _client.DeviceStateChanged += OnDeviceEvent;
            _client.DefaultDeviceChanged += OnDefaultChanged;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException
                                       or DllNotFoundException)
        {
            // Without the audio service there is nothing to watch; Snapshot() reports empty.
            StopWatching();
        }
    }

    private void StopWatching()
    {
        _client?.Dispose();
        _client = null;
        _watchEnumerator?.Dispose();
        _watchEnumerator = null;
    }

    private void OnDeviceEvent(object? sender, EventArgs e) => Raise();

    private void OnDefaultChanged(object? sender, DefaultDeviceChangedEventArgs e)
    {
        if (e.Flow == DataFlow.Capture) Raise();
    }

    private void Raise()
    {
        Action? handler;
        lock (_gate) handler = _changed;
        handler?.Invoke();
    }
}
