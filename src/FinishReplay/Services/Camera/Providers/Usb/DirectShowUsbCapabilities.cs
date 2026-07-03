using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.Versioning;

namespace FinishReplay.Services.Camera.Providers.Usb;

/// <summary>
/// Reads a Windows USB camera's advertised capture modes (format + resolution + frame rates) via
/// DirectShow, the same source Kinovea uses. It binds the device's capture filter and enumerates its
/// output-pin stream capabilities (<c>IAMStreamConfig::GetStreamCaps</c>) — this queries the driver's
/// supported media types without starting the graph, so it does not begin streaming.
///
/// Returns an empty list if anything goes wrong (unknown device, driver quirk, non-Windows); callers
/// fall back to a generic set of options in that case.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class DirectShowUsbCapabilities
{
    private static readonly Guid SystemDeviceEnumClsid = new("62BE5D10-60EB-11d0-BD3B-00A0C911CE86");
    private static readonly Guid VideoInputCategory = new("860BB310-5D01-11d0-BD3B-00A0C911CE86");
    private static readonly Guid IID_IBaseFilter = new("56a86895-0ad4-11ce-b03a-0020af0ba770");
    private static readonly Guid FormatVideoInfo = new("05589f80-c356-11ce-bf01-00aa0055595a");   // FORMAT_VideoInfo
    private static readonly Guid FormatVideoInfo2 = new("f72a76A0-eb0a-11d0-ace4-0000c0cc16ba");  // FORMAT_VideoInfo2

    // Standard frame rates offered when a driver advertises a *range* rather than discrete values.
    private static readonly double[] StandardFps = { 120, 100, 90, 60, 50, 30, 25, 24, 20, 15, 10, 5 };

    /// <summary>
    /// Query the modes for the device identified by <paramref name="deviceId"/> (its DirectShow
    /// FriendlyName, or DevicePath for duplicate names — matching <see cref="DirectShowUsbEnumerator"/>).
    /// </summary>
    public static IReadOnlyList<UsbVideoMode> Query(string deviceId)
    {
        IReadOnlyList<UsbVideoMode> result = Array.Empty<UsbVideoMode>();

        // DirectShow COM prefers an STA; use a short-lived STA thread (as the enumerator does).
        var thread = new Thread(() =>
        {
            try { result = QueryCore(deviceId); }
            catch { result = Array.Empty<UsbVideoMode>(); }
        })
        { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        return result;
    }

    private static IReadOnlyList<UsbVideoMode> QueryCore(string deviceId)
    {
        var filter = BindFilter(deviceId);
        if (filter is null)
            return Array.Empty<UsbVideoMode>();

        try
        {
            var config = FindStreamConfig(filter);
            if (config is null)
                return Array.Empty<UsbVideoMode>();

            try
            {
                return ReadCaps(config);
            }
            finally
            {
                Marshal.ReleaseComObject(config);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(filter);
        }
    }

    /// <summary>Bind the capture filter for the matching device moniker.</summary>
    private static IBaseFilter? BindFilter(string deviceId)
    {
        var comType = Type.GetTypeFromCLSID(SystemDeviceEnumClsid);
        if (comType is null)
            return null;

        var devEnum = (ICreateDevEnum)Activator.CreateInstance(comType)!;
        try
        {
            var category = VideoInputCategory;
            if (devEnum.CreateClassEnumerator(ref category, out var enumMoniker, 0) != 0 || enumMoniker is null)
                return null;

            try
            {
                var monikers = new IMoniker[1];
                while (enumMoniker.Next(1, monikers, IntPtr.Zero) == 0)
                {
                    var moniker = monikers[0];
                    try
                    {
                        var name = ReadProperty(moniker, "FriendlyName");
                        var path = ReadProperty(moniker, "DevicePath");
                        var matches = string.Equals(name, deviceId, StringComparison.OrdinalIgnoreCase)
                                   || string.Equals(path, deviceId, StringComparison.OrdinalIgnoreCase);
                        if (!matches)
                            continue;

                        var iid = IID_IBaseFilter;
                        moniker.BindToObject(null!, null!, ref iid, out var filterObj);
                        return filterObj as IBaseFilter;
                    }
                    finally
                    {
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

        return null;
    }

    /// <summary>Find the output pin that exposes IAMStreamConfig (the capture pin).</summary>
    private static IAMStreamConfig? FindStreamConfig(IBaseFilter filter)
    {
        if (filter.EnumPins(out var enumPins) != 0 || enumPins is null)
            return null;

        try
        {
            var pins = new IPin[1];
            while (enumPins.Next(1, pins, out var fetched) == 0 && fetched == 1)
            {
                var pin = pins[0];
                if (pin is IAMStreamConfig config)
                    return config; // keep the RCW alive; caller releases it
                Marshal.ReleaseComObject(pin);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(enumPins);
        }

        return null;
    }

    private static IReadOnlyList<UsbVideoMode> ReadCaps(IAMStreamConfig config)
    {
        if (config.GetNumberOfCapabilities(out var count, out var size) != 0 || count <= 0 || size <= 0)
            return Array.Empty<UsbVideoMode>();

        // Aggregate by (format, width, height); union the frame rates across caps entries.
        var byMode = new Dictionary<(string Fmt, int W, int H), SortedSet<double>>();
        var scc = Marshal.AllocHGlobal(size);
        try
        {
            for (var i = 0; i < count; i++)
            {
                if (config.GetStreamCaps(i, out var pmt, scc) != 0 || pmt == IntPtr.Zero)
                    continue;

                try
                {
                    if (!TryReadMediaType(pmt, out var fmt, out var w, out var h, out var avgFps))
                        continue;

                    var (minFps, maxFps) = ReadFrameIntervalRange(scc, size);
                    var key = (fmt, w, h);
                    if (!byMode.TryGetValue(key, out var rates))
                        byMode[key] = rates = new SortedSet<double>();

                    // Discrete driver entry (Min==Max) → the one avg rate; otherwise offer standard
                    // rates that fall inside the advertised [min,max] range, plus the avg for good measure.
                    if (avgFps > 0)
                        rates.Add(Math.Round(avgFps, 2));
                    if (minFps > 0 && maxFps > 0 && Math.Abs(maxFps - minFps) > 0.01)
                    {
                        foreach (var std in StandardFps)
                            if (std >= minFps - 0.5 && std <= maxFps + 0.5)
                                rates.Add(std);
                    }
                }
                finally
                {
                    FreeMediaType(pmt);
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(scc);
        }

        return byMode
            .Select(kv => new UsbVideoMode(
                kv.Key.Fmt, kv.Key.W, kv.Key.H,
                kv.Value.Reverse().ToList())) // highest fps first
            .OrderBy(m => m.Format, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(m => m.Width * m.Height)
            .ToList();
    }

    /// <summary>Parse an AM_MEDIA_TYPE's VIDEOINFOHEADER(2): format name, size, and default fps.</summary>
    private static bool TryReadMediaType(IntPtr pmt, out string format, out int width, out int height, out double avgFps)
    {
        format = "";
        width = height = 0;
        avgFps = 0;

        var mt = Marshal.PtrToStructure<AM_MEDIA_TYPE>(pmt);
        if (mt.pbFormat == IntPtr.Zero || mt.cbFormat < 88)
            return false;

        // VIDEOINFOHEADER and VIDEOINFOHEADER2 both start with two RECTs, then bitrate/errorrate (8),
        // then AvgTimePerFrame (8). VIH's BITMAPINFOHEADER is at offset 48; VIH2 adds interlace/aspect
        // fields, putting its BITMAPINFOHEADER at offset 72.
        var isVih2 = mt.formattype == FormatVideoInfo2;
        var isVih = mt.formattype == FormatVideoInfo;
        if (!isVih && !isVih2)
            return false;

        var buf = new byte[mt.cbFormat];
        Marshal.Copy(mt.pbFormat, buf, 0, mt.cbFormat);

        long avgTimePerFrame = BitConverter.ToInt64(buf, 32); // 100ns units
        if (avgTimePerFrame > 0)
            avgFps = 10_000_000.0 / avgTimePerFrame;

        var bih = isVih2 ? 72 : 48;
        if (buf.Length < bih + 20)
            return false;

        width = Math.Abs(BitConverter.ToInt32(buf, bih + 4));   // biWidth
        height = Math.Abs(BitConverter.ToInt32(buf, bih + 8));  // biHeight
        var compression = BitConverter.ToUInt32(buf, bih + 16); // biCompression (fourcc, or 0 = BI_RGB)
        var bitCount = BitConverter.ToInt16(buf, bih + 14);     // biBitCount

        if (width <= 0 || height <= 0)
            return false;

        format = MapFormat(compression, bitCount);
        return format.Length > 0;
    }

    private static (double Min, double Max) ReadFrameIntervalRange(IntPtr scc, int size)
    {
        // VIDEO_STREAM_CONFIG_CAPS: MinFrameInterval @100, MaxFrameInterval @108 (100ns units).
        if (size < 116)
            return (0, 0);
        var buf = new byte[size];
        Marshal.Copy(scc, buf, 0, size);
        long minInterval = BitConverter.ToInt64(buf, 100); // shortest interval → highest fps
        long maxInterval = BitConverter.ToInt64(buf, 108); // longest interval → lowest fps
        double maxFps = minInterval > 0 ? 10_000_000.0 / minInterval : 0;
        double minFps = maxInterval > 0 ? 10_000_000.0 / maxInterval : 0;
        return (minFps, maxFps);
    }

    /// <summary>Map a BITMAPINFOHEADER fourcc/bit-count to the libav capture format name.</summary>
    private static string MapFormat(uint fourcc, int bitCount)
    {
        // BI_RGB (0) → raw RGB; Windows lays it out BGR, which ffmpeg's dshow names bgr24/bgra.
        if (fourcc == 0)
            return bitCount >= 32 ? "bgra" : "bgr24";

        var tag = new string(new[]
        {
            (char)(fourcc & 0xFF),
            (char)((fourcc >> 8) & 0xFF),
            (char)((fourcc >> 16) & 0xFF),
            (char)((fourcc >> 24) & 0xFF),
        }).TrimEnd('\0', ' ').ToUpperInvariant();

        return tag switch
        {
            "MJPG" => "mjpeg",
            "YUY2" or "YUYV" => "yuyv422",
            "UYVY" => "uyvy422",
            "NV12" => "nv12",
            "I420" or "IYUV" => "yuv420p",
            "YV12" => "yuv420p",
            "RGB2" or "RGB3" or "RGB4" => bitCount >= 32 ? "bgra" : "bgr24",
            _ => "", // unknown / compressed (e.g. H264) — don't offer it as a raw pixel format
        };
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

    /// <summary>Free an AM_MEDIA_TYPE returned by GetStreamCaps (its format block, pUnk, then itself).</summary>
    private static void FreeMediaType(IntPtr pmt)
    {
        var mt = Marshal.PtrToStructure<AM_MEDIA_TYPE>(pmt);
        if (mt.cbFormat != 0 && mt.pbFormat != IntPtr.Zero)
            Marshal.FreeCoTaskMem(mt.pbFormat);
        if (mt.pUnk != IntPtr.Zero)
            Marshal.Release(mt.pUnk);
        Marshal.FreeCoTaskMem(pmt);
    }

    // ---- COM interop ----

    [StructLayout(LayoutKind.Sequential)]
    private struct AM_MEDIA_TYPE
    {
        public Guid majortype;
        public Guid subtype;
        [MarshalAs(UnmanagedType.Bool)] public bool bFixedSizeSamples;
        [MarshalAs(UnmanagedType.Bool)] public bool bTemporalCompression;
        public int lSampleSize;
        public Guid formattype;
        public IntPtr pUnk;
        public int cbFormat;
        public IntPtr pbFormat;
    }

    [ComImport, Guid("29840822-5B84-11D0-BD3B-00A0C911CE86"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ICreateDevEnum
    {
        [PreserveSig] int CreateClassEnumerator(ref Guid pType, out IEnumMoniker? ppEnumMoniker, int dwFlags);
    }

    [ComImport, Guid("55272A00-42CB-11CE-8135-00AA004BB851"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyBag
    {
        [PreserveSig] int Read([MarshalAs(UnmanagedType.LPWStr)] string pszPropName, ref object? pVar, IntPtr pErrorLog);
        [PreserveSig] int Write([MarshalAs(UnmanagedType.LPWStr)] string pszPropName, ref object pVar);
    }

    // Only the vtable slots up to EnumPins are declared; the rest are never called.
    [ComImport, Guid("56a86895-0ad4-11ce-b03a-0020af0ba770"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IBaseFilter
    {
        [PreserveSig] int GetClassID(out Guid pClassID);        // IPersist
        [PreserveSig] int Stop();                                // IMediaFilter
        [PreserveSig] int Pause();
        [PreserveSig] int Run(long tStart);
        [PreserveSig] int GetState(int dwMilliSecsTimeout, out int filterState);
        [PreserveSig] int SetSyncSource(IntPtr pClock);
        [PreserveSig] int GetSyncSource(out IntPtr pClock);
        [PreserveSig] int EnumPins(out IEnumPins? ppEnum);       // IBaseFilter
    }

    [ComImport, Guid("56a86892-0ad4-11ce-b03a-0020af0ba770"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IEnumPins
    {
        [PreserveSig] int Next(int cPins,
            [Out, MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.Interface, SizeParamIndex = 0)] IPin[] ppPins,
            out int pcFetched);
        [PreserveSig] int Skip(int cPins);
        [PreserveSig] int Reset();
        [PreserveSig] int Clone(out IEnumPins? ppEnum);
    }

    // Opaque handle — we only QueryInterface it for IAMStreamConfig, never call its methods.
    [ComImport, Guid("56a86891-0ad4-11ce-b03a-0020af0ba770"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPin
    {
    }

    [ComImport, Guid("C6E13340-30AC-11d0-A18C-00A0C9118956"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAMStreamConfig
    {
        [PreserveSig] int SetFormat(IntPtr pmt);
        [PreserveSig] int GetFormat(out IntPtr ppmt);
        [PreserveSig] int GetNumberOfCapabilities(out int piCount, out int piSize);
        [PreserveSig] int GetStreamCaps(int iIndex, out IntPtr ppmt, IntPtr pSCC);
    }
}
