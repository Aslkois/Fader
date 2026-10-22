using System.Windows.Input;
using Fader.App.Services;
using Fader.Core.Audio;

namespace Fader.App.ViewModels;

/// <summary>Master volume of the selected output device. Distinct from any application's volume.</summary>
public sealed class MasterViewModel : ObservableObject
{
    private readonly AppServices _services;
    private bool _suppressPush;
    private double _volume;
    private double _meter;
    private bool _muted;

    public MasterViewModel(AppServices services)
    {
        _services = services;
        ToggleMuteCommand = new RelayCommand(() => _services.Mixer.SetMasterMute(!_muted), () => HasEndpoint);
        _services.Mixer.EndpointChanged += Refresh;
        Refresh();
    }

    public ICommand ToggleMuteCommand { get; }

    public bool HasEndpoint => _services.Mixer.Endpoint is not null;

    public string DeviceName => _services.Mixer.Endpoint?.DeviceName ?? "No output device";

    public string Detail
    {
        get
        {
            var e = _services.Mixer.Endpoint;
            if (e is null) return "Connect or enable an output device in Windows sound settings.";
            if (e.IsFallback) return "The selected device is unavailable. Showing the Windows default output.";
            if (e.IsDefaultMultimedia && e.IsDefaultCommunications) return "Windows default output and communications device";
            if (e.IsDefaultMultimedia) return "Windows default output";
            if (e.IsDefaultCommunications) return "Windows default communications device";
            return "Selected output device";
        }
    }

    public bool IsFallback => _services.Mixer.Endpoint?.IsFallback == true;

    public double VolumePercent
    {
        get => _volume;
        set
        {
            value = Math.Clamp(Math.Round(value), 0, 100);
            if (Math.Abs(_volume - value) < 0.01) return;
            _volume = value;
            Raise();
            Raise(nameof(PercentText));
            if (_suppressPush) return;
            if (_muted && _volume > 0) _services.Mixer.SetMasterMute(false);
            _services.Mixer.SetMasterVolume((float)(_volume / 100.0));
        }
    }

    public string PercentText => $"{(int)_volume}%";

    public bool IsMuted
    {
        get => _muted;
        private set => Set(ref _muted, value);
    }

    public double Meter
    {
        get => _meter;
        private set => Set(ref _meter, value);
    }

    public void SetPeak(float peak)
    {
        double target = peak <= 0.002f ? 0 : Math.Clamp((20.0 * Math.Log10(peak) + 54.0) / 54.0, 0, 1);
        var next = target > _meter ? target : Math.Max(target, _meter * 0.8);
        if (next < 0.01) next = 0;
        if (Math.Abs(next - _meter) < 0.008 && next != 0 && _meter != 0) return;
        Meter = next;
    }

    private void Refresh()
    {
        var e = _services.Mixer.Endpoint;
        _suppressPush = true;
        try
        {
            VolumePercent = e is null ? 0 : Math.Round(e.Volume * 100f);
            IsMuted = e?.Muted ?? false;
        }
        finally
        {
            _suppressPush = false;
        }
        if (e is null) Meter = 0;
        Raise(nameof(HasEndpoint));
        Raise(nameof(DeviceName));
        Raise(nameof(Detail));
        Raise(nameof(IsFallback));
        (ToggleMuteCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }
}
