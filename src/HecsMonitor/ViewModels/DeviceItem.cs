using HecsMonitor.Services;

namespace HecsMonitor.ViewModels;

/// <summary>카메라 검색 결과 한 줄. 카메라마다 ONVIF 계정을 따로 가진다.</summary>
public sealed class DeviceItem : ObservableObject
{
    string _user, _password, _status, _statusKind = "", _streamUri;
    bool _busy;

    public DeviceItem(OnvifDevice device, string user, string password, bool saved)
    {
        Device = device;
        _user = user ?? "";
        _password = password ?? "";
        if (saved) { _status = "저장된 계정을 불러왔습니다"; _statusKind = "Info"; }
    }

    public OnvifDevice Device { get; }
    public string Name => string.IsNullOrWhiteSpace(Device.Name) ? Device.Ip : Device.Name;
    public string ModelLine => string.Join(" · ", new[] { Device.Model, Device.Manufacturer }.Where(s => !string.IsNullOrWhiteSpace(s)));
    public string Ip => Device.Ip;

    public string User { get => _user; set => Set(ref _user, value); }
    public string Password { get => _password; set => Set(ref _password, value); }

    /// <summary>마지막 주소 조회 결과 (계정 확인용).</summary>
    public string StreamUri { get => _streamUri; set => Set(ref _streamUri, value); }
    public string Status { get => _status; set => Set(ref _status, value); }
    /// <summary>Ok / Alarm / Info / "" (KindBrush 색상)</summary>
    public string StatusKind { get => _statusKind; set => Set(ref _statusKind, value); }
    public bool IsBusy { get => _busy; set => Set(ref _busy, value); }
}
