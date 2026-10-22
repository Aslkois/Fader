using System.Collections.ObjectModel;
using System.Windows.Input;
using Fader.App.Services;
using Fader.Core.Apps;
using Fader.Core.Hotkeys;
using Fader.Core.LockIn;
using Fader.Core.Profiles;
using Fader.Core.Settings;
using Fader.Native;

namespace Fader.App.ViewModels;

public sealed class SettingsViewModel : ObservableObject
{
    private readonly AppServices _s;
    private readonly MainViewModel _main;
    private ProfileViewModel? _selectedProfile;
    private bool _pendingDelete;
    private bool _editingEntries;

    public SettingsViewModel(AppServices services, MainViewModel main)
    {
        _s = services;
        _main = main;

        Hotkeys = new ObservableCollection<HotkeyRowViewModel>(
            Enum.GetValues<HotkeyAction>().Select(a => new HotkeyRowViewModel(services, a)));

        NewProfileCommand = new RelayCommand(NewProfile);
        DuplicateProfileCommand = new RelayCommand(DuplicateProfile, () => SelectedProfile is not null);
        DeleteProfileCommand = new RelayCommand(DeleteProfile, () => SelectedProfile is { IsBuiltIn: false });
        ResetProfileCommand = new RelayCommand(ResetProfile, () => SelectedProfile is { IsBuiltIn: true });
        ApplyProfileCommand = new RelayCommand(ApplyProfile, () => SelectedProfile is not null);

        RebuildProfiles(selectId: _s.Config.ActiveProfileId);

        _s.HotkeyStatusChanged += () => { foreach (var h in Hotkeys) h.RefreshStatus(); };
        _s.Profiles.Changed += OnProfilesChanged;
        _main.Rows.CollectionChanged += (_, _) => Raise(nameof(AddTargets));
    }

    private AppSettings Config => _s.Config;

    // ---- General ----

    public bool ThemeSystem { get => Config.Theme == ThemePreference.System; set { if (value) SetTheme(ThemePreference.System); } }
    public bool ThemeLight { get => Config.Theme == ThemePreference.Light; set { if (value) SetTheme(ThemePreference.Light); } }
    public bool ThemeDark { get => Config.Theme == ThemePreference.Dark; set { if (value) SetTheme(ThemePreference.Dark); } }

    public void SetTheme(ThemePreference mode)
    {
        Config.Theme = mode;
        _s.Theme.Apply(mode);
        _s.Settings.MarkDirty();
        Raise(nameof(ThemeSystem));
        Raise(nameof(ThemeLight));
        Raise(nameof(ThemeDark));
        _main.RaiseThemeChanged();
    }

    public bool CloseToTray
    {
        get => Config.CloseToTray;
        set { Config.CloseToTray = value; _s.Settings.MarkDirty(); Raise(); }
    }

    public bool StartWithWindows
    {
        get => StartupRegistration.IsEnabled();
        set
        {
            if (!_s.SetStartWithWindows(value))
                _main.ShowBanner("Windows would not let FADER change its startup entry.", isError: true);
            Raise();
        }
    }

    public bool CompactRows
    {
        get => Config.CompactRows;
        set
        {
            Config.CompactRows = value;
            _s.Settings.MarkDirty();
            Raise();
            _main.RaiseCompactChanged();
        }
    }

    public bool ShowHotkeyFeedback
    {
        get => Config.ShowHotkeyFeedback;
        set { Config.ShowHotkeyFeedback = value; _s.Settings.MarkDirty(); Raise(); }
    }

    public bool ShowAppsOnOtherDevices
    {
        get => Config.ShowAppsOnOtherDevices;
        set
        {
            Config.ShowAppsOnOtherDevices = value;
            _s.Settings.MarkDirty();
            _s.Engine.SetIncludeOtherDevices(value);
            Raise();
        }
    }

    public double VolumeStep
    {
        get => Config.VolumeStepPercent;
        set
        {
            Config.VolumeStepPercent = (int)Math.Clamp(Math.Round(value), 1, 25);
            _s.Settings.MarkDirty();
            Raise();
            Raise(nameof(VolumeStepText));
        }
    }

    public string VolumeStepText => $"{Config.VolumeStepPercent}%";

    public string ChatTargetsText
    {
        get => string.Join(", ", Config.ChatTargets);
        set
        {
            var parsed = (value ?? string.Empty)
                .Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(t => t.ToLowerInvariant())
                .Select(t => t.EndsWith(".exe", StringComparison.Ordinal) ? t : t + ".exe")
                .Distinct()
                .ToList();
            Config.ChatTargets = parsed.Count > 0 ? parsed : [.. KnownApps.DefaultChatTargets];
            _s.Settings.MarkDirty();
            Raise();
            foreach (var h in Hotkeys) h.RefreshStatus();
        }
    }

    // ---- Output device (shared with the header drop-down) ----

    public ObservableCollection<DeviceChoice> Devices => _main.Devices;

    public DeviceChoice? SelectedDevice
    {
        get => _main.SelectedDevice;
        set => _main.SelectedDevice = value;
    }

    public void RaiseDevicesChanged() => Raise(nameof(SelectedDevice));

    // ---- Shortcuts ----

    public ObservableCollection<HotkeyRowViewModel> Hotkeys { get; }

    // ---- Profiles ----

    public ObservableCollection<ProfileViewModel> ProfileChoices { get; } = [];
    public ObservableCollection<ProfileEntryViewModel> Entries { get; } = [];

    public ICommand NewProfileCommand { get; }
    public ICommand DuplicateProfileCommand { get; }
    public ICommand DeleteProfileCommand { get; }
    public ICommand ResetProfileCommand { get; }
    public ICommand ApplyProfileCommand { get; }

    public ProfileViewModel? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (value is null || ReferenceEquals(value, _selectedProfile)) return;
            _selectedProfile = value;
            PendingDelete = false;
            Raise();
            RebuildEntries();
            RaiseProfileCommands();
            Raise(nameof(CanEditName));
            Raise(nameof(DeleteLabel));
        }
    }

    public bool CanEditName => _selectedProfile is not null;

    public bool PendingDelete
    {
        get => _pendingDelete;
        private set { if (Set(ref _pendingDelete, value)) Raise(nameof(DeleteLabel)); }
    }

    public string DeleteLabel => _pendingDelete ? "Click again to delete" : "Delete";

    public bool ShowDelete => _selectedProfile is { IsBuiltIn: false };
    public bool ShowReset => _selectedProfile is { IsBuiltIn: true };
    public bool HasEntries => Entries.Count > 0;
    public bool HasNoEntries => Entries.Count == 0;

    public IEnumerable<AddTargetChoice> AddTargets
    {
        get
        {
            var profile = _selectedProfile?.Model;
            if (profile is null) return [];
            var list = new List<AddTargetChoice>();
            foreach (var c in new[] { AppCategory.Game, AppCategory.Chat, AppCategory.Music, AppCategory.Browser, AppCategory.Other, AppCategory.System })
            {
                if (profile.Entries.Any(e => e.Kind == ProfileTargetKind.Category && e.Category == c)) continue;
                list.Add(new AddTargetChoice(KnownApps.CategoryLabel(c), "Group", c, null));
            }
            foreach (var row in _main.Rows)
            {
                if (profile.Entries.Any(e => e.Kind == ProfileTargetKind.App && e.App is not null && e.App.Matches(row.Identity))) continue;
                list.Add(new AddTargetChoice(row.DisplayName, "App", null, row.Identity));
            }
            return list;
        }
    }

    public AddTargetChoice? AddTarget
    {
        get => null;
        set
        {
            if (value is null || _selectedProfile is null) return;
            AddEntry(value);
            Raise();
        }
    }

    private void AddEntry(AddTargetChoice choice)
    {
        var id = _selectedProfile!.Id;
        _editingEntries = true;
        try
        {
            if (choice.Category is { } category)
            {
                _s.Profiles.SetCategoryEntry(id, category, 100);
            }
            else if (choice.App is { } app)
            {
                var row = _main.Rows.FirstOrDefault(r => r.Key == app.Key);
                _s.Profiles.SetAppEntry(id, app, choice.Label, row is null ? 100 : (int)row.VolumePercent);
            }
        }
        finally
        {
            _editingEntries = false;
        }
        RebuildEntries();
    }

    public void RenameProfile(ProfileViewModel profile, string name)
    {
        _editingEntries = true;
        try { _s.Profiles.Rename(profile.Id, name); }
        finally { _editingEntries = false; }
        Raise(nameof(ProfileChoices));
    }

    /// <summary>Called by an entry row after its volume or mute changed. Saves without rebuilding the list under the user's hand.</summary>
    public void EntryEdited()
    {
        _editingEntries = true;
        try { _s.Profiles.Touch(); }
        finally { _editingEntries = false; }
    }

    private void RemoveEntry(ProfileEntry entry)
    {
        if (_selectedProfile is null) return;
        _editingEntries = true;
        try { _s.Profiles.RemoveEntry(_selectedProfile.Id, entry); }
        finally { _editingEntries = false; }
        RebuildEntries();
    }

    private void NewProfile()
    {
        var created = _s.Profiles.Create("New profile");
        RebuildProfiles(created.Id);
    }

    private void DuplicateProfile()
    {
        if (_selectedProfile is null) return;
        var copy = _s.Profiles.Create(_selectedProfile.Name + " copy", _selectedProfile.Model);
        RebuildProfiles(copy.Id);
    }

    private void DeleteProfile()
    {
        if (_selectedProfile is not { IsBuiltIn: false }) return;
        if (!PendingDelete)
        {
            PendingDelete = true;
            return;
        }
        _s.Profiles.Delete(_selectedProfile.Id);
        RebuildProfiles(_s.Config.ActiveProfileId);
    }

    private void ResetProfile()
    {
        if (_selectedProfile is not { IsBuiltIn: true }) return;
        _s.Profiles.ResetBuiltIn(_selectedProfile.Id);
        RebuildEntries();
    }

    private void ApplyProfile()
    {
        if (_selectedProfile is null) return;
        _s.ApplyProfile(_selectedProfile.Id);
    }

    private void OnProfilesChanged()
    {
        if (_editingEntries) return;
        RebuildProfiles(_selectedProfile?.Id);
    }

    private void RebuildProfiles(string? selectId)
    {
        ProfileChoices.Clear();
        foreach (var p in _s.Profiles.Profiles) ProfileChoices.Add(new ProfileViewModel(this, p));
        _selectedProfile = null;
        SelectedProfile = ProfileChoices.FirstOrDefault(p => p.Id == selectId) ?? ProfileChoices.FirstOrDefault();
        Raise(nameof(LockInProfile));
        Raise(nameof(AddTargets));
    }

    private void RebuildEntries()
    {
        Entries.Clear();
        if (_selectedProfile is not null)
        {
            foreach (var e in _selectedProfile.Model.Entries.ToList())
            {
                var entry = e;
                Entries.Add(new ProfileEntryViewModel(this, entry, () => RemoveEntry(entry)));
            }
        }
        Raise(nameof(HasEntries));
        Raise(nameof(HasNoEntries));
        Raise(nameof(AddTargets));
        Raise(nameof(ShowDelete));
        Raise(nameof(ShowReset));
    }

    private void RaiseProfileCommands()
    {
        (DuplicateProfileCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (DeleteProfileCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ResetProfileCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ApplyProfileCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    // ---- LOCK IN ----

    public ProfileViewModel? LockInProfile
    {
        get => ProfileChoices.FirstOrDefault(p => p.Id == Config.LockIn.ProfileId);
        set
        {
            if (value is null) return;
            Config.LockIn.ProfileId = value.Id;
            _s.Settings.MarkDirty();
            Raise();
        }
    }

    public bool PolicyRestore { get => Policy == RestorePolicy.RestorePrevious; set { if (value) Policy = RestorePolicy.RestorePrevious; } }
    public bool PolicyKeep { get => Policy == RestorePolicy.KeepCurrent; set { if (value) Policy = RestorePolicy.KeepCurrent; } }
    public bool PolicyAsk { get => Policy == RestorePolicy.AskEachTime; set { if (value) Policy = RestorePolicy.AskEachTime; } }

    private RestorePolicy Policy
    {
        get => Config.LockIn.RestorePolicy;
        set
        {
            Config.LockIn.RestorePolicy = value;
            _s.Settings.MarkDirty();
            Raise(nameof(PolicyRestore));
            Raise(nameof(PolicyKeep));
            Raise(nameof(PolicyAsk));
        }
    }

    public bool EnforceProfile
    {
        get => Config.LockIn.EnforceProfile;
        set { Config.LockIn.EnforceProfile = value; _s.Settings.MarkDirty(); Raise(); }
    }

    // ---- About and data ----

    public string SettingsFilePath => _s.Settings.FilePath;

    public string VersionText
    {
        get
        {
            var version = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version;
            return version is null ? "FADER" : $"FADER {version.Major}.{version.Minor}.{version.Build}";
        }
    }

    public ICommand OpenDataFolderCommand => new RelayCommand(() =>
    {
        try
        {
            System.IO.Directory.CreateDirectory(_s.Settings.Directory);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{_s.Settings.Directory}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _main.ShowToast("Could not open the folder: " + ex.Message, false);
        }
    });
}
