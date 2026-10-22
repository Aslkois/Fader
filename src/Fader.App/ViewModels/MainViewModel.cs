using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Threading;
using Fader.App.Services;
using Fader.Core.Apps;
using Fader.Core.Audio;
using Fader.Core.Hotkeys;
using Fader.Core.LockIn;
using Fader.Core.Profiles;
using Fader.Core.Settings;
using Fader.Native;

namespace Fader.App.ViewModels;

public sealed record DeviceChoice(OutputMode Mode, string? DeviceId, string Title, string? Subtitle)
{
    public bool IsSameTarget(OutputMode mode, string? deviceId) =>
        Mode == mode && (mode != OutputMode.Specific || string.Equals(DeviceId, deviceId, StringComparison.Ordinal));
}

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly AppServices _services;
    private readonly Dictionary<string, AppRowViewModel> _rows = new(StringComparer.Ordinal);
    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(4) };
    private bool _rebuildingDevices;
    private DeviceChoice? _selectedDevice;
    private bool _isSettingsOpen;
    private bool _showRestorePrompt;
    private string? _toast;
    private bool _toastIsError;
    private string? _banner;
    private bool _bannerIsError;

    public MainViewModel(AppServices services)
    {
        _services = services;
        Master = new MasterViewModel(services);
        Settings = new SettingsViewModel(services, this);

        ToggleLockInCommand = new RelayCommand(ToggleLockIn);
        LockInAlternateCommand = new RelayCommand(LockInAlternate);
        RestoreCommand = new RelayCommand(() => FinishPrompt(DeactivateMode.RestorePrevious));
        KeepCommand = new RelayCommand(() => FinishPrompt(DeactivateMode.KeepCurrent));
        CancelPromptCommand = new RelayCommand(() => ShowRestorePrompt = false);
        OpenSettingsCommand = new RelayCommand(() => IsSettingsOpen = !IsSettingsOpen);
        CloseSettingsCommand = new RelayCommand(() => IsSettingsOpen = false);
        CycleThemeCommand = new RelayCommand(CycleTheme);
        ToggleCompactCommand = new RelayCommand(() => Settings.CompactRows = !Settings.CompactRows);
        DismissBannerCommand = new RelayCommand(() => Banner = null);
        RetryCommand = new RelayCommand(() => _services.Engine.Refresh());

        _toastTimer.Tick += (_, _) => { _toastTimer.Stop(); Toast = null; };

        Mixer.GroupAdded += OnGroupAdded;
        Mixer.GroupRemoved += OnGroupRemoved;
        Mixer.GroupChanged += OnGroupChanged;
        Mixer.MetersUpdated += OnMeters;
        Mixer.DevicesChanged += OnDevicesChanged;
        Mixer.EndpointChanged += OnEndpointChanged;
        Mixer.StatusChanged += OnStatusChanged;
        _services.LockIn.Changed += RaiseLockIn;
        _services.Feedback += ShowToast;
        _services.HotkeyStatusChanged += OnHotkeyStatusChanged;
        _services.Profiles.Changed += RaiseLockIn;

        foreach (var pinned in _services.Config.PinnedApps)
            CreateRow(pinned.App, pinned.DisplayName, pinned: true, pinned.LastVolumePercent, pinned.LastMuted ?? false);
        foreach (var group in Mixer.Groups) OnGroupAdded(group);

        RebuildDevices();
        Resort();
        RefreshStatusFlags();

        if (_services.Settings.LoadNotice is { } notice) ShowBanner(notice, isError: false);
        OnHotkeyStatusChanged();
    }

    public MixerService Mixer => _services.Mixer;
    public IconService Icons => _services.Icons;
    public AppServices Services => _services;
    public MasterViewModel Master { get; }
    public SettingsViewModel Settings { get; }
    public ObservableCollection<AppRowViewModel> Rows { get; } = [];
    public ObservableCollection<DeviceChoice> Devices { get; } = [];

    public ICommand ToggleLockInCommand { get; }
    public ICommand LockInAlternateCommand { get; }
    public ICommand RestoreCommand { get; }
    public ICommand KeepCommand { get; }
    public ICommand CancelPromptCommand { get; }
    public ICommand OpenSettingsCommand { get; }
    public ICommand CloseSettingsCommand { get; }
    public ICommand CycleThemeCommand { get; }
    public ICommand ToggleCompactCommand { get; }
    public ICommand DismissBannerCommand { get; }
    public ICommand RetryCommand { get; }

    // ---- page and density ----

    public bool IsSettingsOpen
    {
        get => _isSettingsOpen;
        set
        {
            if (!Set(ref _isSettingsOpen, value)) return;
            Raise(nameof(IsMixerOpen));
            Raise(nameof(SettingsButtonTip));
        }
    }

    public bool IsMixerOpen => !_isSettingsOpen;
    public string SettingsButtonTip => _isSettingsOpen ? "Back to the mixer" : "Settings";
    public bool CompactRows => _services.Config.CompactRows;
    public string CompactTip => CompactRows ? "Switch to comfortable rows" : "Switch to compact rows";

    public void RaiseCompactChanged()
    {
        Raise(nameof(CompactRows));
        Raise(nameof(CompactTip));
    }

    // ---- theme ----

    public string ThemeGlyph => _services.Config.Theme switch
    {
        ThemePreference.Dark => "",
        ThemePreference.Light => "",
        _ => "",
    };

    public string ThemeTip => _services.Config.Theme switch
    {
        ThemePreference.Dark => "Theme: dark. Click for follow Windows",
        ThemePreference.Light => "Theme: light. Click for dark",
        _ => "Theme: follow Windows. Click for light",
    };

    private void CycleTheme()
    {
        Settings.SetTheme(_services.Theme.NextMode());
    }

    public void RaiseThemeChanged()
    {
        Raise(nameof(ThemeGlyph));
        Raise(nameof(ThemeTip));
    }

    // ---- LOCK IN ----

    public bool LockInActive => _services.LockIn.IsActive;

    public string LockInSubtitle
    {
        get
        {
            if (!_services.LockIn.IsActive)
            {
                var key = _services.HotkeyText(HotkeyAction.ToggleLockIn);
                return key.Length > 0 ? $"Off. {key} works from any app" : "Off. One click sets your chat, music and browser levels";
            }
            var profile = _services.Profiles.Get(_services.Config.LockIn.ProfileId);
            var count = profile is null
                ? 0
                : ProfileResolver.Resolve(profile, Mixer.Groups.Select(_services.LockIn.InputFor).ToList()).Count;
            var name = profile?.Name ?? "LOCK IN";
            return count == 0 ? $"On. {name} has no running apps to set yet" : $"On. {name} set {count} {(count == 1 ? "app" : "apps")}";
        }
    }

    public string LockInHint
    {
        get
        {
            if (!_services.LockIn.IsActive) return string.Empty;
            return _services.Config.LockIn.RestorePolicy switch
            {
                RestorePolicy.KeepCurrent => "Turning LOCK IN off keeps the levels as they are now.",
                RestorePolicy.AskEachTime => "Turning LOCK IN off will ask whether to restore your previous levels.",
                _ => "Turning LOCK IN off puts every app back where it was.",
            };
        }
    }

    public string LockInAlternateLabel => _services.Config.LockIn.RestorePolicy switch
    {
        RestorePolicy.KeepCurrent => "Turn off and restore previous",
        _ => "Turn off and keep current",
    };

    public bool ShowLockInAlternate =>
        _services.LockIn.IsActive && !_showRestorePrompt && _services.Config.LockIn.RestorePolicy != RestorePolicy.AskEachTime;

    public bool ShowLockInStrip => _services.LockIn.IsActive;

    public bool ShowRestorePrompt
    {
        get => _showRestorePrompt;
        set
        {
            if (!Set(ref _showRestorePrompt, value)) return;
            Raise(nameof(ShowLockInAlternate));
        }
    }

    private void RaiseLockIn()
    {
        Raise(nameof(LockInActive));
        Raise(nameof(LockInSubtitle));
        Raise(nameof(LockInHint));
        Raise(nameof(LockInAlternateLabel));
        Raise(nameof(ShowLockInAlternate));
        Raise(nameof(ShowLockInStrip));
        if (!_services.LockIn.IsActive && _showRestorePrompt) ShowRestorePrompt = false;
    }

    private void ToggleLockIn()
    {
        var outcome = _services.ToggleLockIn(interactive: true);
        if (outcome.NeedsDecision)
        {
            ShowRestorePrompt = true;
            return;
        }
        ShowToast(outcome.Message, outcome.Success);
    }

    private void LockInAlternate()
    {
        var mode = _services.Config.LockIn.RestorePolicy == RestorePolicy.KeepCurrent
            ? DeactivateMode.RestorePrevious
            : DeactivateMode.KeepCurrent;
        FinishPrompt(mode);
    }

    private void FinishPrompt(DeactivateMode mode)
    {
        ShowRestorePrompt = false;
        var outcome = _services.LockIn.Deactivate(mode);
        _services.UpdateTray();
        ShowToast(outcome.Message, outcome.Success);
    }

    // ---- rows ----

    private AppRowViewModel CreateRow(AppIdentity identity, string? name, bool pinned, int? lastVolume, bool lastMuted)
    {
        var row = new AppRowViewModel(this, identity, name, pinned, lastVolume, lastMuted);
        _rows[identity.Key] = row;
        Rows.Add(row);
        return row;
    }

    private AppRowViewModel? FindRowFor(AppGroup group)
    {
        if (_rows.TryGetValue(group.Key, out var exact)) return exact;

        foreach (var row in _rows.Values)
        {
            if (row.Group is not null || !row.IsPinned || !row.Identity.Matches(group.Identity)) continue;

            // The pinned app moved (updated install folder): follow it and remember the new location.
            _rows.Remove(row.Key);
            row.Identity = group.Identity;
            _rows[group.Key] = row;
            var pinned = _services.Config.PinnedApps.FirstOrDefault(p => p.App.Matches(group.Identity));
            if (pinned is not null)
            {
                pinned.App = group.Identity;
                _services.Settings.MarkDirty();
            }
            return row;
        }
        return null;
    }

    private void OnGroupAdded(AppGroup group)
    {
        var row = FindRowFor(group) ?? CreateRow(group.Identity, null, pinned: false, null, false);
        row.Attach(group);
        Resort();
        RefreshStatusFlags();
        Raise(nameof(LockInSubtitle));
    }

    private void OnGroupRemoved(AppGroup group)
    {
        if (!_rows.TryGetValue(group.Key, out var row)) return;
        row.Detach();
        if (!row.IsPinned)
        {
            _rows.Remove(group.Key);
            Rows.Remove(row);
        }
        Resort();
        RefreshStatusFlags();
        Raise(nameof(LockInSubtitle));
    }

    private void OnGroupChanged(AppGroup group)
    {
        if (_rows.TryGetValue(group.Key, out var row)) row.UpdateFrom(group);
    }

    private void OnMeters()
    {
        foreach (var row in Rows) row.SetPeak(row.Group?.Peak ?? 0f);
        Master.SetPeak(Mixer.MasterPeak);
    }

    public void OnRowNameChanged() => Resort();

    /// <summary>Pinned apps first, then by name, so rows never jump around while audio starts and stops.</summary>
    private void Resort()
    {
        var desired = Rows
            .OrderBy(r => r.IsPinned ? 0 : 1)
            .ThenBy(r => r.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(r => r.Key, StringComparer.Ordinal)
            .ToList();
        for (var i = 0; i < desired.Count; i++)
        {
            if (ReferenceEquals(Rows[i], desired[i])) continue;
            Rows.Move(Rows.IndexOf(desired[i]), i);
        }
    }

    public string? DeviceNoteFor(AppGroup group)
    {
        var endpoint = Mixer.Endpoint?.DeviceId;
        foreach (var session in group.Sessions)
        {
            if (session.DeviceId == endpoint) continue;
            var device = Mixer.Devices.FirstOrDefault(d => d.Id == session.DeviceId);
            return device?.Name ?? "another device";
        }
        return null;
    }

    // ---- row actions (context menu) ----

    public void TogglePin(AppRowViewModel row)
    {
        var pinned = !row.IsPinned;
        row.IsPinned = pinned;
        var list = _services.Config.PinnedApps;
        if (pinned)
        {
            list.RemoveAll(p => p.App.Key == row.Key);
            list.Add(new PinnedApp
            {
                App = row.Identity,
                DisplayName = row.DisplayName,
                LastVolumePercent = (int)row.VolumePercent,
                LastMuted = row.IsMuted,
            });
        }
        else
        {
            list.RemoveAll(p => p.App.Key == row.Key);
            if (row.Group is null)
            {
                _rows.Remove(row.Key);
                Rows.Remove(row);
            }
        }
        _services.Settings.MarkDirty();
        Resort();
        RefreshStatusFlags();
    }

    public void RememberPinnedLevel(AppRowViewModel row)
    {
        var pinned = _services.Config.PinnedApps.FirstOrDefault(p => p.App.Key == row.Key);
        if (pinned is null) return;
        var volume = (int)row.VolumePercent;
        if (pinned.LastVolumePercent == volume && pinned.LastMuted == row.IsMuted && pinned.DisplayName == row.DisplayName) return;
        pinned.LastVolumePercent = volume;
        pinned.LastMuted = row.IsMuted;
        pinned.DisplayName = row.DisplayName;
        _services.Settings.MarkDirty();
    }

    public AppCategory CategoryOf(AppRowViewModel row) =>
        row.Group is { } group ? _services.LockIn.EffectiveCategory(group) : _services.Catalog.Categorize(row.Identity);

    public void SetCategory(AppRowViewModel row, AppCategory category)
    {
        _services.Catalog.SetCategory(row.Identity, category);
        ShowToast($"{row.DisplayName} counts as {KnownApps.CategoryLabel(category).ToLowerInvariant()} in profiles", true);
    }

    public void SaveLevelToProfile(AppRowViewModel row, Profile profile)
    {
        var volume = (int)row.VolumePercent;
        _services.Profiles.SetAppEntry(profile.Id, row.Identity, row.DisplayName, volume, row.IsMuted);
        ShowToast($"{profile.Name}: {row.DisplayName} set to {volume}%", true);
    }

    public void RemoveFromProfile(AppRowViewModel row, Profile profile)
    {
        if (_services.Profiles.RemoveAppEntry(profile.Id, row.Identity))
            ShowToast($"{row.DisplayName} removed from {profile.Name}", true);
    }

    // ---- devices ----

    public DeviceChoice? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            if (value is null || ReferenceEquals(value, _selectedDevice)) return;
            _selectedDevice = value;
            Raise();
            if (_rebuildingDevices) return;
            _services.Config.OutputMode = value.Mode;
            _services.Config.OutputDeviceId = value.Mode == OutputMode.Specific ? value.DeviceId : null;
            _services.Settings.MarkDirty();
            _services.ApplyOutputPreference();
        }
    }

    private void OnDevicesChanged() => RebuildDevices();

    private void OnEndpointChanged() => RebuildDevices();

    private void RebuildDevices()
    {
        var devices = Mixer.Devices;
        var cfg = _services.Config;
        var multimedia = devices.FirstOrDefault(d => d.IsDefaultMultimedia);
        var comms = devices.FirstOrDefault(d => d.IsDefaultCommunications);

        var choices = new List<DeviceChoice>
        {
            new(OutputMode.FollowMultimedia, null, "Default output", multimedia?.Name),
        };
        if (comms is not null && multimedia is not null && comms.Id != multimedia.Id)
            choices.Add(new(OutputMode.FollowCommunications, null, "Default communications", comms.Name));
        foreach (var d in devices)
        {
            var note = d.IsDefaultMultimedia && d.IsDefaultCommunications ? "Default" :
                d.IsDefaultMultimedia ? "Default output" :
                d.IsDefaultCommunications ? "Default communications" : null;
            choices.Add(new(OutputMode.Specific, d.Id, d.Name, note));
        }
        if (cfg.OutputMode == OutputMode.Specific && !string.IsNullOrEmpty(cfg.OutputDeviceId) && devices.All(d => d.Id != cfg.OutputDeviceId))
            choices.Add(new(OutputMode.Specific, cfg.OutputDeviceId, "Selected device unavailable", "Showing the Windows default until it returns"));

        _rebuildingDevices = true;
        try
        {
            // Rebuild in place only when something actually changed, so an open drop-down is not disturbed.
            if (!choices.SequenceEqual(Devices))
            {
                Devices.Clear();
                foreach (var c in choices) Devices.Add(c);
            }
            var match = Devices.FirstOrDefault(c => c.IsSameTarget(cfg.OutputMode, cfg.OutputDeviceId)) ?? Devices.FirstOrDefault();
            _selectedDevice = null;
            SelectedDevice = match;
        }
        finally
        {
            _rebuildingDevices = false;
        }
        Settings.RaiseDevicesChanged();
        foreach (var row in Rows) row.RefreshDetail();
    }

    // ---- state shown when there is nothing to show ----

    public bool IsLoading => !Mixer.IsReady && Mixer.Status == EngineStatus.Starting;
    public bool ShowError => Mixer.Status == EngineStatus.Error;
    public bool ShowNoDevice => Mixer.Status == EngineStatus.NoDevice;
    public string ErrorText => Mixer.StatusMessage ?? "Windows audio is not responding.";
    public bool ShowEmpty => Mixer.IsReady && Mixer.Status == EngineStatus.Running && Rows.Count == 0;
    public bool ShowList => Rows.Count > 0;
    public int AppCount => Rows.Count(r => r.IsRunning);

    private void OnStatusChanged() => RefreshStatusFlags();

    private void RefreshStatusFlags()
    {
        Raise(nameof(IsLoading));
        Raise(nameof(ShowError));
        Raise(nameof(ShowNoDevice));
        Raise(nameof(ErrorText));
        Raise(nameof(ShowEmpty));
        Raise(nameof(ShowList));
        Raise(nameof(AppCount));
    }

    // ---- toast and banner ----

    public string? Toast
    {
        get => _toast;
        private set
        {
            if (Set(ref _toast, value)) Raise(nameof(HasToast));
        }
    }

    public bool HasToast => !string.IsNullOrEmpty(_toast);

    public bool ToastIsError
    {
        get => _toastIsError;
        private set => Set(ref _toastIsError, value);
    }

    public void ShowToast(string message, bool success)
    {
        ToastIsError = !success;
        Toast = message;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    public string? Banner
    {
        get => _banner;
        private set
        {
            if (Set(ref _banner, value)) Raise(nameof(HasBanner));
        }
    }

    public bool HasBanner => !string.IsNullOrEmpty(_banner);

    public bool BannerIsError
    {
        get => _bannerIsError;
        private set => Set(ref _bannerIsError, value);
    }

    public void ShowBanner(string message, bool isError)
    {
        BannerIsError = isError;
        Banner = message;
    }

    private void OnHotkeyStatusChanged()
    {
        var failed = _services.HotkeyResults.Where(r => !r.IsOk).ToList();
        if (failed.Count == 0)
        {
            if (Banner is not null && Banner.StartsWith("Shortcut", StringComparison.Ordinal)) Banner = null;
            return;
        }
        var names = string.Join(", ", failed.Select(f => HotkeyDefaults.Describe(f.Action, "chat")));
        ShowBanner($"Shortcut problem: {names}. Open Settings > Shortcuts to choose a different key.", isError: true);
    }

    public void Dispose()
    {
        _toastTimer.Stop();
        Mixer.GroupAdded -= OnGroupAdded;
        Mixer.GroupRemoved -= OnGroupRemoved;
        Mixer.GroupChanged -= OnGroupChanged;
        Mixer.MetersUpdated -= OnMeters;
        Mixer.DevicesChanged -= OnDevicesChanged;
        Mixer.EndpointChanged -= OnEndpointChanged;
        Mixer.StatusChanged -= OnStatusChanged;
        _services.LockIn.Changed -= RaiseLockIn;
        _services.Feedback -= ShowToast;
        _services.HotkeyStatusChanged -= OnHotkeyStatusChanged;
        _services.Profiles.Changed -= RaiseLockIn;
    }
}
