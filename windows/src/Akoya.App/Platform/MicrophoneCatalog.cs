using System.Text.RegularExpressions;
using Akoya.Core.Mics;
using NAudio.CoreAudioApi;

namespace Akoya.App.Platform;

/// <summary>
/// The active capture endpoints, as the facts <see cref="MicrophoneNaming"/> needs.
/// <para>
/// <see cref="Changed"/> is raised when a capture endpoint is added, removed, changes state, or the
/// default capture device changes. It is posted to the <see cref="SynchronizationContext"/> that was
/// current when the catalog was constructed (the UI thread), or run on the thread pool when there is
/// none; it is never raised on the Windows audio worker thread that reported the change, so a handler
/// may call <see cref="Snapshot"/> or subscribe and unsubscribe freely.
/// </para>
/// </summary>
public sealed partial class MicrophoneCatalog : IDisposable
{
    private readonly object _gate = new();
    private readonly EventPoster _poster = new(SynchronizationContext.Current);
    private Action? _changed;
    private bool _disposed;

    public event Action? Changed
    {
        add
        {
            lock (_gate)
            {
                if (_disposed) return;
                Volatile.Write(ref _changed, _changed + value);
                EnsureWatching();
            }
        }
        remove
        {
            Watch? stale = null;
            lock (_gate)
            {
                Volatile.Write(ref _changed, _changed - value);
                if (_changed is null) stale = DetachWatch();
            }
            // Unregistering waits for any notification being dispatched, so it must not hold _gate.
            stale?.Dispose();
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
        Watch? stale;
        lock (_gate)
        {
            _disposed = true;
            Volatile.Write(ref _changed, null);
            stale = DetachWatch();
        }
        stale?.Dispose();
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
            string.Equals(id, defaultId, StringComparison.OrdinalIgnoreCase));
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

    [GeneratedRegex(@"^\{\d+\}\.")]
    private static partial Regex BraceIndexPrefix();

    /// <summary>Fallback: a known physical bus name anywhere in the text.</summary>
    private static string? PhysicalBus(string? text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        var match = PhysicalBusPattern().Match(text);
        return match.Success ? match.Value.ToUpperInvariant() : null;
    }

    /// <summary>
    /// The bus enumerator of the parent device named by <c>PKEY_Device_ControllerDeviceId</c>, upper case:
    /// a leading <c>{n}.</c> is dropped, then either the segment between <c>\\?\</c> and the first <c>#</c>
    /// (an interface path such as <c>\\?\bthhfenum#...</c>) or the text before the first backslash
    /// (an instance id such as <c>PCI\VEN_1102...</c>). Empty when there is nothing to parse.
    /// </summary>
    public static string ParentEnumerator(string? parentHint)
    {
        if (string.IsNullOrWhiteSpace(parentHint)) return "";
        var text = BraceIndexPrefix().Replace(parentHint.Trim(), "");
        if (text.StartsWith(@"\\?\", StringComparison.Ordinal))
        {
            text = text[4..];
            var hash = text.IndexOf('#');
            return (hash < 0 ? text : text[..hash]).Trim().ToUpperInvariant();
        }
        var slash = text.IndexOf('\\');
        return (slash < 0 ? text : text[..slash]).Trim().ToUpperInvariant();
    }

    private static bool IsSoftwareBus(string enumerator) =>
        enumerator.Length == 0
        || string.Equals(enumerator, "SWD", StringComparison.OrdinalIgnoreCase)
        || string.Equals(enumerator, "ROOT", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The bus enumerator to hand to naming. A capture endpoint's own instance ID is normally
    /// <c>SWD\MMDEVAPI\...</c>, which says nothing about the hardware, so when it is a software bus
    /// (SWD, ROOT or empty) the parent's enumerator (see <see cref="ParentEnumerator"/>) is used if it
    /// is a real bus (PCI, ACPI, USB, HDAUDIO, BTHENUM...). If the parent is unreadable or also
    /// virtual, a USB, BTH*, HDAUDIO or INTELAUDIO token in either string is used as a fallback.
    /// </summary>
    public static string ResolveEnumerator(string? instanceId, string? parentHint)
    {
        var own = ParseEnumerator(instanceId);
        if (!IsSoftwareBus(own)) return own;
        var parent = ParentEnumerator(parentHint);
        if (!IsSoftwareBus(parent)) return parent;
        return PhysicalBus(instanceId) ?? PhysicalBus(parentHint) ?? own;
    }

    /// <summary>
    /// False for software-only endpoints. Rule: any enumerator other than SWD/ROOT/empty is a real
    /// bus; SWD and ROOT count as physical only when the parent's enumerator is a real bus or a
    /// USB / BTH* / HDAUDIO / INTELAUDIO token appears in the instance id or parent hint.
    /// </summary>
    public static bool HasPhysicalParent(string enumerator, string? instanceId, string? parentHint)
    {
        if (string.IsNullOrWhiteSpace(enumerator)) return false;
        if (!IsSoftwareBus(enumerator)) return true;
        if (!IsSoftwareBus(ParentEnumerator(parentHint))) return true;
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

    /// <summary>The registered notification client and the enumerator that owns it.</summary>
    private sealed class Watch : IDisposable
    {
        public MMDeviceEnumerator Enumerator { get; }

        public MMDeviceNotificationClient Client { get; }

        public Watch(MMDeviceEnumerator enumerator, MMDeviceNotificationClient client)
        {
            Enumerator = enumerator;
            Client = client;
        }

        public void Dispose()
        {
            Client.Dispose();
            Enumerator.Dispose();
        }
    }

    private Watch? _watch;

    private void EnsureWatching()
    {
        if (_disposed || _watch is not null) return;
        MMDeviceEnumerator? enumerator = null;
        try
        {
            enumerator = new MMDeviceEnumerator();
            var client = enumerator.CreateNotificationClient(useSynchronizationContext: false);
            client.DeviceAdded += OnDeviceEvent;
            client.DeviceRemoved += OnDeviceEvent;
            client.DeviceStateChanged += OnDeviceEvent;
            client.DefaultDeviceChanged += OnDefaultChanged;
            _watch = new Watch(enumerator, client);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException
                                       or DllNotFoundException)
        {
            // Without the audio service there is nothing to watch; Snapshot() reports empty.
            enumerator?.Dispose();
        }
    }

    /// <summary>Takes the watch out of service under the lock; the caller disposes it after releasing the lock.</summary>
    private Watch? DetachWatch()
    {
        var watch = _watch;
        _watch = null;
        return watch;
    }

    // These run on a Windows audio worker thread while it holds an internal lock: take none of ours,
    // and hand the real work to the poster.
    private void OnDeviceEvent(object? sender, EventArgs e) => Raise();

    private void OnDefaultChanged(object? sender, DefaultDeviceChangedEventArgs e)
    {
        if (e.Flow == DataFlow.Capture) Raise();
    }

    private void Raise()
    {
        if (Volatile.Read(ref _changed) is null) return;
        _poster.Post(() => Volatile.Read(ref _changed)?.Invoke());
    }
}
