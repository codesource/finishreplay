using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.Versioning;
using FinishReplay.Models;

namespace FinishReplay.Services.Camera.Providers.Usb;

/// <summary>
/// Enumerates video capture devices on Windows via DirectShow (the VideoInputDeviceCategory), reading
/// each device's <c>FriendlyName</c> — the exact name ffmpeg's dshow input uses (<c>-i video=Name</c>).
/// In-process COM (no ffmpeg process), and runs on an STA thread for COM apartment safety.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class DirectShowUsbEnumerator
{
    private static readonly Guid SystemDeviceEnumClsid = new("62BE5D10-60EB-11d0-BD3B-00A0C911CE86");
    private static readonly Guid VideoInputCategory = new("860BB310-5D01-11d0-BD3B-00A0C911CE86");

    public static IReadOnlyList<CameraDevice> Enumerate()
    {
        IReadOnlyList<CameraDevice> result = Array.Empty<CameraDevice>();

        // DirectShow COM objects prefer an STA; use a short-lived STA thread.
        var thread = new Thread(() =>
        {
            try { result = EnumerateCore(); }
            catch { result = Array.Empty<CameraDevice>(); }
        })
        { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        return result;
    }

    private static IReadOnlyList<CameraDevice> EnumerateCore()
    {
        var raw = new List<(string Name, string? Path)>();

        var comType = Type.GetTypeFromCLSID(SystemDeviceEnumClsid);
        if (comType is null)
            return Array.Empty<CameraDevice>();

        var devEnum = (ICreateDevEnum)Activator.CreateInstance(comType)!;
        try
        {
            var category = VideoInputCategory;
            var hr = devEnum.CreateClassEnumerator(ref category, out var enumMoniker, 0);
            if (hr != 0 || enumMoniker is null)
                return Array.Empty<CameraDevice>(); // S_FALSE => no devices in this category

            try
            {
                var monikers = new IMoniker[1];
                while (enumMoniker.Next(1, monikers, IntPtr.Zero) == 0)
                {
                    var moniker = monikers[0];
                    try
                    {
                        var name = ReadProperty(moniker, "FriendlyName");
                        if (!string.IsNullOrWhiteSpace(name))
                            raw.Add((name, ReadProperty(moniker, "DevicePath")));
                    }
                    finally
                    {
                        if (moniker is not null)
                            Marshal.ReleaseComObject(moniker);
                    }
                }
            }
            finally
            {
                Marshal.ReleaseComObject(enumMoniker);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(devEnum);
        }

        return Disambiguate(raw);
    }

    /// <summary>
    /// Two cameras can share a FriendlyName. ffmpeg's dshow input can't open "video=Name" for a
    /// duplicate, but it also matches the device path (the "alternative name"), which is unique. So for
    /// duplicated names we use the DevicePath as the capture Id and number the display name.
    /// </summary>
    private static IReadOnlyList<CameraDevice> Disambiguate(List<(string Name, string? Path)> raw)
    {
        var counts = raw.GroupBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var devices = new List<CameraDevice>();
        foreach (var (name, path) in raw)
        {
            string id, display;
            if (counts[name] > 1)
            {
                var n = index.TryGetValue(name, out var v) ? v + 1 : 1;
                index[name] = n;
                id = string.IsNullOrWhiteSpace(path) ? name : path!; // device path is unique & ffmpeg-openable
                display = $"{name} (#{n})";
            }
            else
            {
                id = name;
                display = name;
            }

            if (seenIds.Add(id))
            {
                devices.Add(new CameraDevice(id, display)
                {
                    ProviderName = UsbCameraProvider.Type,
                    SourceType = UsbCameraProvider.Type,
                });
            }
        }

        return devices;
    }

    private static string? ReadProperty(IMoniker moniker, string property)
    {
        var bagId = typeof(IPropertyBag).GUID;
        moniker.BindToStorage(null!, null!, ref bagId, out var bagObj);
        var bag = (IPropertyBag)bagObj;
        try
        {
            object? value = null;
            return bag.Read(property, ref value, IntPtr.Zero) == 0 ? value as string : null;
        }
        finally
        {
            Marshal.ReleaseComObject(bag);
        }
    }

    [ComImport, Guid("29840822-5B84-11D0-BD3B-00A0C911CE86"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ICreateDevEnum
    {
        [PreserveSig]
        int CreateClassEnumerator(ref Guid pType, out IEnumMoniker? ppEnumMoniker, int dwFlags);
    }

    [ComImport, Guid("55272A00-42CB-11CE-8135-00AA004BB851"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyBag
    {
        [PreserveSig]
        int Read([MarshalAs(UnmanagedType.LPWStr)] string pszPropName, ref object? pVar, IntPtr pErrorLog);

        [PreserveSig]
        int Write([MarshalAs(UnmanagedType.LPWStr)] string pszPropName, ref object pVar);
    }
}
