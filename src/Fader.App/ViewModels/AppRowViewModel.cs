using System.Windows.Input;
using System.Windows.Media;
using Fader.Core.Apps;
using Fader.Core.Audio;

namespace Fader.App.ViewModels;

public enum RowState { Playing, Idle, NotRunning }

/// <summary>One mixer row: a logical application, possibly backed by several audio sessions.</summary>
public sealed class AppRowViewModel : ObservableObject
{
    private readonly MainViewModel _owner;
    private bool _suppressPush;
    private double _volume;
    private double _meter;
    private bool _muted;
    private bool _pinned;
    private RowState _state = RowState.NotRunning;
    private string _displayName;
    private string _detail = "Not running";
    private ImageSource? _icon;

    public AppRowViewModel(MainViewModel owner, AppIdentity identity, string? knownName, bool pinned, int? lastVolume, bool lastMuted)
    {
        _owner = owner;
        Identity = identity;
        _pinned = pinned;
        _displayName = knownName ?? AppDisplayNames.Fallback(identity);
        _volume = lastVolume ?? 100;
        _muted = lastMuted;
        ToggleMuteCommand = new RelayCommand(ToggleMute, () => Group is not null);
        TogglePinCommand = new RelayCommand(() => _owner.TogglePin(this));
        _ = LoadNameAndIconAsync();
    }

    public AppIdentity Identity { get; internal set; }
    public string Key => Identity.Key;
    public AppGroup? Group { get; private set; }
    public MainViewModel Owner => _owner;

    public ICommand ToggleMuteCommand { get; }
    public ICommand TogglePinCommand { get; }

    public string DisplayName
    {
        get => _displayName;
        private set { if (Set(ref _displayName, value)) _owner.OnRowNameChanged(); }
    }

    public ImageSource? Icon
    {
        get => _icon;
        private set { if (Set(ref _icon, value)) Raise(nameof(HasIcon)); }
    }

    public bool HasIcon => _icon is not null;

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
            if (!_suppressPush) PushVolume();
        }
    }

    public string PercentText => $"{(int)_volume}%";

    public bool IsMuted
    {
        get => _muted;
        private set => Set(ref _muted, value);
    }

    /// <summary>Display level 0..1 for the meter, already mapped to a perceptual scale and smoothed.</summary>
    public double Meter
    {
        get => _meter;
        private set => Set(ref _meter, value);
    }

    public RowState State
    {
        get => _state;
        private set
        {
            if (!Set(ref _state, value)) return;
            Raise(nameof(IsRunning));
            Raise(nameof(IsNotRunning));
            Raise(nameof(IsPlaying));
        }
    }

    public bool IsRunning => _state != RowState.NotRunning;
    public bool IsNotRunning => _state == RowState.NotRunning;
    public bool IsPlaying => _state == RowState.Playing;

    public string Detail
    {
        get => _detail;
        private set => Set(ref _detail, value);
    }

    public bool IsPinned
    {
        get => _pinned;
        internal set
        {
            if (Set(ref _pinned, value)) Raise(nameof(PinTip));
        }
    }

    public string PinTip => _pinned ? "Pinned: stays in the list when the app is closed" : "Pin to keep this app in the list";

    // ---- state from the audio side ----

    public void Attach(AppGroup group)
    {
        Group = group;
        UpdateFrom(group);
        (ToggleMuteCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    public void Detach()
    {
        Group = null;
        State = RowState.NotRunning;
        Meter = 0;
        Detail = "Not running";
        (ToggleMuteCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    public void UpdateFrom(AppGroup group)
    {
        _suppressPush = true;
        try
        {
            VolumePercent = Math.Round(group.Volume * 100f);
            IsMuted = group.Muted;
            State = group.IsPlaying ? RowState.Playing : RowState.Idle;
        }
        finally
        {
            _suppressPush = false;
        }
        RefreshDetail();
        if (_pinned) _owner.RememberPinnedLevel(this);
    }

    public void RefreshDetail()
    {
        if (Group is null)
        {
            Detail = "Not running";
            return;
        }
        var text = _state == RowState.Playing ? "Playing" : "Idle";
        var note = _owner.DeviceNoteFor(Group);
        Detail = note is null ? text : $"{text} on {note}";
    }

    /// <summary>Called ~20 times a second while the window is visible. Cheap by design.</summary>
    public void SetPeak(float peak)
    {
        double target = peak <= 0.002f ? 0 : Math.Clamp((20.0 * Math.Log10(peak) + 54.0) / 54.0, 0, 1);
        // Instant attack, quick decay: readable without flicker.
        var next = target > _meter ? target : Math.Max(target, _meter * 0.8);
        if (next < 0.01) next = 0;
        if (Math.Abs(next - _meter) < 0.008 && next != 0 && _meter != 0) return;
        Meter = next;
    }

    // ---- commands ----

    private void PushVolume()
    {
        var group = Group;
        if (group is null) return;
        if (group.Muted && _volume > 0) _owner.Mixer.SetGroupMute(group, false);
        _owner.Mixer.SetGroupVolume(group, (float)(_volume / 100.0));
    }

    private void ToggleMute()
    {
        var group = Group;
        if (group is null) return;
        _owner.Mixer.SetGroupMute(group, !group.Muted);
    }

    private async Task LoadNameAndIconAsync()
    {
        var identity = Identity;
        var name = await Task.Run(() => AppDisplayNames.Resolve(identity));
        if (name != _displayName) DisplayName = name;
        Icon = await _owner.Icons.GetAsync(identity.Path, 64);
    }
}
