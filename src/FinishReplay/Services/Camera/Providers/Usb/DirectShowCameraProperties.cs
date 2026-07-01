using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.Versioning;

namespace FinishReplay.Services.Camera.Providers.Usb;

/// <summary>
/// Opens a USB camera's native DirectShow property pages (exposure, gain, focus, white balance, …) —
/// the same dialog Kinovea shows via its "device property pages" button. Because we capture through
/// ffmpeg (not our own DirectShow graph), this is how advanced controls are exposed: the user adjusts
/// them here and the driver persists them, so the subsequent ffmpeg capture inherits the values.
/// Windows-only; best-effort (returns false and never throws to the caller on failure).
/// </summary>
[SupportedOSPlatform("windows")]
internal static class DirectShowCameraProperties
{
    private static readonly Guid SystemDeviceEnumClsid = new("62BE5D10-60EB-11d0-BD3B-00A0C911CE86");
    private static readonly Guid VideoInputCategory = new("860BB310-5D01-11d0-BD3B-00A0C911CE86");
    private static readonly Guid IID_IBaseFilter = new("56a86895-0ad4-11ce-b03a-0020af0ba770");

    /// <summary>Show the property pages for the device matching <paramref name="deviceId"/> (FriendlyName or DevicePath).</summary>
    public static bool ShowPropertyPages(string deviceId, IntPtr owner)
    {
        try
        {
            return ShowCore(deviceId, owner);
        }
        catch
        {
            return false;
        }
    }

    private static bool ShowCore(string deviceId, IntPtr owner)
    {
        var comType = Type.GetTypeFromCLSID(SystemDeviceEnumClsid);
        if (comType is null) return false;

        var devEnum = (ICreateDevEnum)Activator.CreateInstance(comType)!;
        try
        {
            var category = VideoInputCategory;
            if (devEnum.CreateClassEnumerator(ref category, out var enumMoniker, 0) != 0 || enumMoniker is null)
                return false;

            try
            {
                var monikers = new IMoniker[1];
                while (enumMoniker.Next(1, monikers, IntPtr.Zero) == 0)
                {
                    var moniker = monikers[0];
                    try
                    {
                        if (Matches(moniker, deviceId))
                            return ShowPagesForMoniker(moniker, owner);
                    }
                    finally
                    {
                        if (moniker is not null) Marshal.ReleaseComObject(moniker);
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

        return false;
    }

    private static bool Matches(IMoniker moniker, string deviceId)
    {
        var friendly = ReadProperty(moniker, "FriendlyName");
        if (string.Equals(friendly, deviceId, StringComparison.OrdinalIgnoreCase))
            return true;
        var path = ReadProperty(moniker, "DevicePath");
        return string.Equals(path, deviceId, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShowPagesForMoniker(IMoniker moniker, IntPtr owner)
    {
        var iid = IID_IBaseFilter;
        moniker.BindToObject(null!, null!, ref iid, out var filter);
        try
        {
            if (filter is not ISpecifyPropertyPages pages)
                return false;

            pages.GetPages(out var caGuid);
            try
            {
                if (caGuid.cElems == 0)
                    return false;

                var obj = filter;
                OleCreatePropertyFrame(owner, 0, 0, "Camera properties", 1, ref obj,
                    caGuid.cElems, caGuid.pElems, 0, 0, IntPtr.Zero);
                return true;
            }
            finally
            {
                if (caGuid.pElems != IntPtr.Zero)
                    Marshal.FreeCoTaskMem(caGuid.pElems);
            }
        }
        finally
        {
            if (filter is not null) Marshal.ReleaseComObject(filter);
        }
    }

    private static string? ReadProperty(IMoniker moniker, string property)
    {
        try
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
        catch
        {
            return null;
        }
    }

    [DllImport("oleaut32.dll")]
    private static extern int OleCreatePropertyFrame(
        IntPtr hwndOwner, int x, int y,
        [MarshalAs(UnmanagedType.LPWStr)] string? lpszCaption,
        int cObjects,
        [MarshalAs(UnmanagedType.Interface)] ref object ppUnk,
        int cPages, IntPtr pPageClsID, int lcid, int dwReserved, IntPtr pvReserved);

    [StructLayout(LayoutKind.Sequential)]
    private struct CAUUID
    {
        public int cElems;
        public IntPtr pElems;
    }

    [ComImport, Guid("29840822-5B84-11D0-BD3B-00A0C911CE86"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ICreateDevEnum
    {
        [PreserveSig] int CreateClassEnumerator(ref Guid pType, out IEnumMoniker? ppEnumMoniker, int dwFlags);
    }

    [ComImport, Guid("B196B28B-BAB4-101A-B69C-00AA00341D07"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISpecifyPropertyPages
    {
        [PreserveSig] int GetPages(out CAUUID pPages);
    }

    [ComImport, Guid("55272A00-42CB-11CE-8135-00AA004BB851"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyBag
    {
        [PreserveSig] int Read([MarshalAs(UnmanagedType.LPWStr)] string pszPropName, ref object? pVar, IntPtr pErrorLog);
        [PreserveSig] int Write([MarshalAs(UnmanagedType.LPWStr)] string pszPropName, ref object pVar);
    }
}
