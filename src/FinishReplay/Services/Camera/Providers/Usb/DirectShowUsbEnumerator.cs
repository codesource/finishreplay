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
        var devices = new List<CameraDevice>();

        var comType = Type.GetTypeFromCLSID(SystemDeviceEnumClsid);
        if (comType is null)
            return devices;

        var devEnum = (ICreateDevEnum)Activator.CreateInstance(comType)!;
        try
        {
            var category = VideoInputCategory;
            var hr = devEnum.CreateClassEnumerator(ref category, out var enumMoniker, 0);
            if (hr != 0 || enumMoniker is null)
                return devices; // S_FALSE => no devices in this category

            try
            {
                var monikers = new IMoniker[1];
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                while (enumMoniker.Next(1, monikers, IntPtr.Zero) == 0)
                {
                    var moniker = monikers[0];
                    try
                    {
                        var name = ReadFriendlyName(moniker);
                        if (!string.IsNullOrWhiteSpace(name) && seen.Add(name))
                        {
                            devices.Add(new CameraDevice(name, name)
                            {
                                ProviderName = UsbCameraProvider.Type,
                                SourceType = UsbCameraProvider.Type,
                            });
                        }
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

        return devices;
    }

    private static string? ReadFriendlyName(IMoniker moniker)
    {
        var bagId = typeof(IPropertyBag).GUID;
        moniker.BindToStorage(null!, null!, ref bagId, out var bagObj);
        var bag = (IPropertyBag)bagObj;
        try
        {
            object? value = null;
            return bag.Read("FriendlyName", ref value, IntPtr.Zero) == 0 ? value as string : null;
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
