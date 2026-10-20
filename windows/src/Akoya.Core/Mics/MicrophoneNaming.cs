using System.Text.RegularExpressions;

namespace Akoya.Core.Mics;

/// <summary>Turns Windows endpoint facts into the plain names shown in setup and the tray.</summary>
public static class MicrophoneNaming
{
    private const string BuiltInName = "This PC's microphone";
    private const string WebcamName = "Webcam mic";
    private const string VoicemeeterName = "Voicemeeter";
    private const string HeadsetName = "Headset mic";

    private enum Kind
    {
        BuiltIn = 0,
        Other = 1,
        Webcam = 2,
        More = 3,
    }

    private static readonly HashSet<string> GenericNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Microphone", "Mic", "Headset", "Headset Microphone", "Microphone Array",
    };

    private static readonly string[] BluetoothSuffixes =
    {
        " Hands-Free AG Audio", " Hands-Free", " AG Audio", " Headset",
    };

    private static readonly Regex LoopbackNames = new(
        @"\b(stereo mix|what u hear|wave out mix)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex VirtualVendors = new(
        @"\b(vb-audio|nvidia broadcast|steam|discord|obs|krisp)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex VoicemeeterRx = new(
        @"voicemeeter", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex WebcamWords = new(
        @"\b(camera|webcam|cam|brio|usb video)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private sealed record Classified(EndpointFacts Facts, Kind Kind, string Name);

    public static IReadOnlyList<NamedMic> Name(IReadOnlyList<EndpointFacts> endpoints)
    {
        var classified = endpoints.Select(Classify).ToList();

        var webcamCount = classified.Count(c => c.Kind == Kind.Webcam);
        if (webcamCount > 1)
        {
            classified = classified
                .Select(c => c.Kind == Kind.Webcam ? c with { Name = $"{WebcamName} ({c.Name})" } : c)
                .ToList();
        }
        else
        {
            classified = classified
                .Select(c => c.Kind == Kind.Webcam ? c with { Name = WebcamName } : c)
                .ToList();
        }

        var ordered = classified
            .OrderBy(c => c.Kind)
            .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.Facts.Id, StringComparer.Ordinal);

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<NamedMic>();
        foreach (var c in ordered)
        {
            var name = c.Name;
            for (var n = 2; !used.Add(name); n++)
            {
                name = $"{c.Name} {n}";
            }

            result.Add(new NamedMic(c.Facts.Id, name, c.Kind == Kind.More, c.Facts.IsDefault));
        }

        return result;
    }

    private static Classified Classify(EndpointFacts f)
    {
        var friendly = (f.FriendlyName ?? "").Trim();
        var description = (f.DeviceDescription ?? "").Trim();
        var enumerator = (f.Enumerator ?? "").Trim();
        var both = friendly + " | " + description;

        if (VoicemeeterRx.IsMatch(both))
        {
            return new Classified(f, Kind.More, VoicemeeterName);
        }

        var loopback = f.FormFactor is EndpointFormFactor.LineLevel or EndpointFormFactor.DigitalAudioDisplayDevice
            || LoopbackNames.IsMatch(both);
        if (loopback)
        {
            return new Classified(f, Kind.More, LoopbackLabel(f));
        }

        var virtualByVendor = VirtualVendors.IsMatch(both);
        var virtualByBus = (Is(enumerator, "SWD") || Is(enumerator, "ROOT")) && !f.HasPhysicalParent;
        if (virtualByVendor || virtualByBus)
        {
            return new Classified(f, Kind.More, DeviceName(f));
        }

        if ((Is(enumerator, "HDAUDIO") || Is(enumerator, "INTELAUDIO"))
            && f.FormFactor is EndpointFormFactor.Microphone or EndpointFormFactor.Unknown)
        {
            return new Classified(f, Kind.BuiltIn, BuiltInName);
        }

        if ((Is(enumerator, "HDAUDIO") || Is(enumerator, "INTELAUDIO")) && f.FormFactor is EndpointFormFactor.Headset)
        {
            return new Classified(f, Kind.Other, HeadsetName);
        }

        if (enumerator.StartsWith("BTH", StringComparison.OrdinalIgnoreCase))
        {
            return new Classified(f, Kind.Other, BluetoothName(f));
        }

        if (Is(enumerator, "USB"))
        {
            return WebcamWords.IsMatch(both)
                ? new Classified(f, Kind.Webcam, DeviceName(f))
                : new Classified(f, Kind.Other, DeviceName(f));
        }

        return new Classified(f, Kind.Other, Fallback(f));
    }

    private static bool Is(string value, string expected) =>
        string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);

    private static bool IsGeneric(string s) => GenericNames.Contains(s.Trim());

    /// <summary>Splits "Outer (Inner)" using balanced parentheses, so "Stereo Mix (Realtek(R) Audio)" works.</summary>
    private static (string Outer, string Inner)? SplitWrapper(string s)
    {
        s = s.Trim();
        if (s.Length < 3 || s[^1] != ')')
        {
            return null;
        }

        var depth = 0;
        for (var i = s.Length - 1; i >= 0; i--)
        {
            if (s[i] == ')')
            {
                depth++;
            }
            else if (s[i] == '(' && --depth == 0)
            {
                var outer = s[..i].Trim();
                var inner = s[(i + 1)..^1].Trim();
                return outer.Length == 0 || inner.Length == 0 ? null : (outer, inner);
            }
        }

        return null;
    }

    /// <summary>Removes a generic "Microphone (...)" / "Headset (...)" wrapper; anything else is returned as is.</summary>
    private static string Unwrap(string s)
    {
        s = s.Trim();
        while (SplitWrapper(s) is { } w && IsGeneric(w.Outer))
        {
            s = w.Inner;
        }

        return s;
    }

    /// <summary>The device's own name: its description unless that is generic, else the friendly name's inner part.</summary>
    private static string DeviceName(EndpointFacts f)
    {
        var fromDescription = Unwrap(f.DeviceDescription ?? "");
        if (fromDescription.Length > 0 && !IsGeneric(fromDescription))
        {
            return fromDescription;
        }

        return Fallback(f);
    }

    private static string Fallback(EndpointFacts f)
    {
        var name = Unwrap(f.FriendlyName ?? "");
        return name.Length > 0 ? name : "Microphone";
    }

    private static string LoopbackLabel(EndpointFacts f)
    {
        var friendly = (f.FriendlyName ?? "").Trim();
        if (SplitWrapper(friendly) is { } w && !IsGeneric(w.Outer))
        {
            return w.Outer;
        }

        if (friendly.Length > 0 && SplitWrapper(friendly) is null && !IsGeneric(friendly))
        {
            return friendly;
        }

        return DeviceName(f);
    }

    private static string BluetoothName(EndpointFacts f)
    {
        var name = StripBluetooth(DeviceName(f));
        return name.Length > 0 && !IsGeneric(name) ? name : StripBluetooth(Fallback(f));
    }

    private static string StripBluetooth(string s)
    {
        s = Unwrap(s);
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var suffix in BluetoothSuffixes)
            {
                if (s.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    s = s[..^suffix.Length].TrimEnd();
                    changed = true;
                }
            }
        }

        return s;
    }
}
