using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FinishReplay.Models;
using FinishReplay.Services.Camera;
using FinishReplay.Services.Camera.Providers;
using FinishReplay.Services.Camera.Providers.Usb;

namespace FinishReplay.ViewModels;

// name + suffix are edited here (moved out of the camera row).

/// <summary>
/// Per-camera configuration, type-aware like Kinovea's dialog:
/// - USB: capture format / resolution / frame rate (wired into the ffmpeg capture), plus a
///   "Driver properties" button for exposure/gain/focus (the device's own DirectShow pages).
/// - Network: host / port / format / user / password composed into the final URL, with a Test button.
/// Edits are written back to the wrapped <see cref="CameraProfile"/> on <see cref="Apply"/>.
/// </summary>
public partial class CameraConfigViewModel : ObservableObject
{
    private const string Auto = "Auto";
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(3);

    private readonly CameraProfile _profile;

    // Capture modes the device actually advertises (empty when unknown/unsupported → generic fallback).
    private readonly IReadOnlyList<UsbVideoMode> _modes;
    private readonly bool _hasModes;

    public CameraConfigViewModel(CameraProfile profile)
    {
        _profile = profile;
        IsUsb = profile.SourceType == UsbCameraProvider.Type;
        _name = profile.DisplayName;
        _suffix = profile.Suffix;
        _latencyMs = profile.ManualOffsetMs ?? 0;

        _modes = IsUsb ? UsbCameraCapabilities.Query(profile.Id) : Array.Empty<UsbVideoMode>();
        _hasModes = _modes.Count > 0;

        if (IsUsb)
            LoadUsb();
        else
            LoadNetwork();
    }

    public string DeviceId => _profile.Id;

    public bool IsUsb { get; }
    public bool IsNetwork => !IsUsb;
    public bool CanShowDriverProperties => IsUsb && OperatingSystem.IsWindows();

    // Name + suffix (shown for every camera type).
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanApply))] private string _name;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanApply))] private string _suffix;

    /// <summary>
    /// Manual latency compensation in ms — how much later this camera's video arrives than a fast
    /// reference. Used to sync a slower (e.g. WiFi/RTSP) camera against a low-latency one during replay.
    /// </summary>
    [ObservableProperty] private double _latencyMs;

    public bool CanApply => !string.IsNullOrWhiteSpace(Name) && !string.IsNullOrWhiteSpace(Suffix);

    // ---- USB ----
    // Populated from the device's advertised modes when available, otherwise a generic set. The
    // Resolutions/FrameRates lists are re-filtered as Format/Resolution change so only combinations the
    // camera actually supports are offered ("available couples").
    public ObservableCollection<string> Formats { get; } = new();
    public ObservableCollection<string> Resolutions { get; } = new();
    public ObservableCollection<string> FrameRates { get; } = new();

    [ObservableProperty] private string _format = Auto;
    [ObservableProperty] private string _resolution = Auto;
    [ObservableProperty] private string _frameRate = Auto;

    // Generic fallback options when the device's modes can't be read (non-Windows, driver quirk).
    private static readonly string[] FallbackFormats = { Auto, "mjpeg", "yuyv422", "bgr24", "nv12" };
    private static readonly string[] FallbackResolutions = { Auto, "640×480", "800×600", "1280×720", "1920×1080" };
    private static readonly string[] FallbackFrameRates = { Auto, "60", "50", "30", "25", "15" };

    // ---- Network ----
    public IReadOnlyList<string> NetworkFormats { get; } =
        new[] { MjpegCameraProvider.Type, RtspCameraProvider.Type };

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(FinalUrl))] private string _host = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(FinalUrl))] private string _port = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(FinalUrl))] private string _networkFormat = MjpegCameraProvider.Type;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(FinalUrl))] private string _user = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(FinalUrl))] private string _password = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(FinalUrl))] private string _path = "";

    [ObservableProperty] private string _testStatus = "";

    public string FinalUrl => BuildUrl();

    [RelayCommand]
    private async Task Test()
    {
        TestStatus = "Testing…";
        var (host, port) = CameraReachability.ParseEndpoint(FinalUrl);
        var ok = await CameraReachability.CheckTcpAsync(host, port, TestTimeout);
        TestStatus = ok ? "✓ Reachable" : "✗ Not reachable";
    }

    /// <summary>Persist the edits into the profile.</summary>
    public void Apply()
    {
        _profile.DisplayName = Name.Trim();
        _profile.Suffix = Suffix.Trim();
        _profile.ManualOffsetMs = LatencyMs == 0 ? null : LatencyMs;

        if (IsUsb)
        {
            _profile.PixelFormat = Format == Auto ? null : Format;
            _profile.FrameRate = double.TryParse(FrameRate, out var fps) ? fps : null;
            if (TryParseResolution(Resolution, out var w, out var h))
            {
                _profile.Width = w;
                _profile.Height = h;
            }
            else
            {
                _profile.Width = null;
                _profile.Height = null;
            }
        }
        else
        {
            // Network capture opens SourceUrl; Id stays as the original stable key.
            _profile.SourceType = NetworkFormat;
            _profile.SourceUrl = BuildUrl();
        }
    }

    private void LoadUsb()
    {
        // Formats first; setting Format rebuilds Resolutions, which rebuilds FrameRates.
        RebuildFormats();

        var wantFormat = string.IsNullOrWhiteSpace(_profile.PixelFormat) ? Auto : _profile.PixelFormat!;
        Format = Formats.Contains(wantFormat) ? wantFormat : Auto;

        var wantRes = _profile is { Width: > 0, Height: > 0 } ? $"{_profile.Width}×{_profile.Height}" : Auto;
        Resolution = Resolutions.Contains(wantRes) ? wantRes : Auto;

        var wantFps = _profile.FrameRate is > 0 ? FormatFps(_profile.FrameRate.Value) : Auto;
        FrameRate = FrameRates.Contains(wantFps) ? wantFps : Auto;
    }

    // Re-filter the dependent lists whenever a higher-level choice changes.
    partial void OnFormatChanged(string value) => RebuildResolutions();
    partial void OnResolutionChanged(string value) => RebuildFrameRates();

    private void RebuildFormats()
    {
        if (!_hasModes)
        {
            Replace(Formats, FallbackFormats);
            RebuildResolutions();
            return;
        }

        var formats = _modes.Select(m => m.Format).Distinct(StringComparer.OrdinalIgnoreCase)
                            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);
        Replace(Formats, new[] { Auto }.Concat(formats));
        RebuildResolutions();
    }

    private void RebuildResolutions()
    {
        if (!_hasModes)
        {
            Replace(Resolutions, FallbackResolutions);
            RebuildFrameRates();
            return;
        }

        var sizes = _modes
            .Where(m => Format == Auto || m.Format.Equals(Format, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(m => m.Width * m.Height)
            .Select(m => m.Resolution)
            .Distinct();
        Replace(Resolutions, new[] { Auto }.Concat(sizes));

        if (!Resolutions.Contains(Resolution))
            Resolution = Auto; // triggers RebuildFrameRates
        else
            RebuildFrameRates();
    }

    private void RebuildFrameRates()
    {
        if (!_hasModes)
        {
            Replace(FrameRates, FallbackFrameRates);
            return;
        }

        var rates = _modes
            .Where(m => Format == Auto || m.Format.Equals(Format, StringComparison.OrdinalIgnoreCase))
            .Where(m => Resolution == Auto || m.Resolution == Resolution)
            .SelectMany(m => m.FrameRates)
            .Distinct()
            .OrderByDescending(r => r)
            .Select(FormatFps);
        Replace(FrameRates, new[] { Auto }.Concat(rates));

        if (!FrameRates.Contains(FrameRate))
            FrameRate = Auto;
    }

    private static void Replace(ObservableCollection<string> target, IEnumerable<string> items)
    {
        var list = items.ToList();
        if (target.Count == list.Count && target.SequenceEqual(list))
            return; // unchanged — avoid clearing (which would reset the ComboBox selection)

        target.Clear();
        foreach (var item in list)
            target.Add(item);
    }

    private static string FormatFps(double fps) =>
        Math.Abs(fps - Math.Round(fps)) < 0.05 ? ((int)Math.Round(fps)).ToString() : fps.ToString("0.##");

    private void LoadNetwork()
    {
        NetworkFormat = _profile.SourceType == RtspCameraProvider.Type
            ? RtspCameraProvider.Type
            : MjpegCameraProvider.Type;

        if (Uri.TryCreate(_profile.SourceUrl, UriKind.Absolute, out var uri))
        {
            Host = uri.Host;
            Port = uri.Port > 0 ? uri.Port.ToString() : "";
            Path = uri.AbsolutePath.Trim('/');
            var userInfo = uri.UserInfo.Split(':', 2);
            User = userInfo.Length > 0 ? Uri.UnescapeDataString(userInfo[0]) : "";
            Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : "";
        }
    }

    private string BuildUrl()
    {
        var scheme = NetworkFormat == RtspCameraProvider.Type ? "rtsp" : "http";
        var auth = string.IsNullOrEmpty(User) && string.IsNullOrEmpty(Password) ? "" : $"{User}:{Password}@";
        var portPart = string.IsNullOrWhiteSpace(Port) ? "" : $":{Port.Trim()}";
        var path = string.IsNullOrWhiteSpace(Path) ? "" : "/" + Path.Trim().TrimStart('/');
        return $"{scheme}://{auth}{Host.Trim()}{portPart}{path}";
    }

    private static bool TryParseResolution(string resolution, out int width, out int height)
    {
        width = height = 0;
        if (string.IsNullOrWhiteSpace(resolution) || resolution == Auto)
            return false;

        var parts = resolution.Split('×', 'x');
        return parts.Length == 2
            && int.TryParse(parts[0].Trim(), out width)
            && int.TryParse(parts[1].Trim(), out height);
    }
}
