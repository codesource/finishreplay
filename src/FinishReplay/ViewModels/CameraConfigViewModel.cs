using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FinishReplay.Models;
using FinishReplay.Services.Camera;
using FinishReplay.Services.Camera.Providers;

namespace FinishReplay.ViewModels;

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
    private readonly HttpClient _http = new();

    public CameraConfigViewModel(CameraProfile profile)
    {
        _profile = profile;
        IsUsb = profile.SourceType == UsbCameraProvider.Type;

        if (IsUsb)
            LoadUsb();
        else
            LoadNetwork();
    }

    public string Title => _profile.DisplayName;
    public string DeviceId => _profile.Id;

    public bool IsUsb { get; }
    public bool IsNetwork => !IsUsb;
    public bool CanShowDriverProperties => IsUsb && OperatingSystem.IsWindows();

    // ---- USB ----
    public IReadOnlyList<string> Formats { get; } = new[] { Auto, "mjpeg", "yuyv422", "rgb24", "nv12" };
    public IReadOnlyList<string> Resolutions { get; } = new[] { Auto, "640×480", "800×600", "1280×720", "1920×1080" };
    public IReadOnlyList<string> FrameRates { get; } = new[] { Auto, "15", "25", "30", "50", "60" };

    [ObservableProperty] private string _format = Auto;
    [ObservableProperty] private string _resolution = Auto;
    [ObservableProperty] private string _frameRate = Auto;

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
        var url = FinalUrl;
        bool ok;
        if (NetworkFormat == RtspCameraProvider.Type)
        {
            var (host, port) = CameraReachability.ParseRtspEndpoint(url);
            ok = await CameraReachability.CheckTcpAsync(host, port, TestTimeout);
        }
        else
        {
            ok = await CameraReachability.CheckHttpAsync(url, _http, TestTimeout);
        }
        TestStatus = ok ? "✓ Reachable" : "✗ Not reachable";
    }

    /// <summary>Persist the edits into the profile.</summary>
    public void Apply()
    {
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
        Format = string.IsNullOrWhiteSpace(_profile.PixelFormat) ? Auto : _profile.PixelFormat!;
        Resolution = _profile is { Width: > 0, Height: > 0 } ? $"{_profile.Width}×{_profile.Height}" : Auto;
        FrameRate = _profile.FrameRate is > 0 ? ((int)_profile.FrameRate).ToString() : Auto;
    }

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
