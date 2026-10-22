using System.Windows.Input;
using Fader.Core.Apps;
using Fader.Core.Profiles;

namespace Fader.App.ViewModels;

public sealed class ProfileViewModel(SettingsViewModel owner, Profile model) : ObservableObject
{
    public Profile Model { get; } = model;
    public string Id => Model.Id;
    public bool IsBuiltIn => Model.BuiltIn;

    public string Name
    {
        get => Model.Name;
        set
        {
            if (!string.IsNullOrWhiteSpace(value) && value.Trim() != Model.Name) owner.RenameProfile(this, value);
            Raise();
        }
    }

    public override string ToString() => Model.Name;
}

public sealed class ProfileEntryViewModel : ObservableObject
{
    private readonly SettingsViewModel _owner;

    public ProfileEntryViewModel(SettingsViewModel owner, ProfileEntry model, Action remove)
    {
        _owner = owner;
        Model = model;
        RemoveCommand = new RelayCommand(remove);
    }

    public ProfileEntry Model { get; }
    public ICommand RemoveCommand { get; }

    public string Name => Model.DisplayName;

    public string KindText => Model.Kind == ProfileTargetKind.Category ? "Group" : "App";

    public double Volume
    {
        get => Model.Volume;
        set
        {
            var v = (int)Math.Clamp(Math.Round(value), 0, 100);
            if (v == Model.Volume) return;
            Model.Volume = v;
            Raise();
            Raise(nameof(VolumeText));
            _owner.EntryEdited();
        }
    }

    public string VolumeText => $"{Model.Volume}%";

    public bool Mute
    {
        get => Model.Mute;
        set
        {
            if (Model.Mute == value) return;
            Model.Mute = value;
            Raise();
            _owner.EntryEdited();
        }
    }
}

/// <summary>An entry that can be added to the open profile: a group of apps or one application.</summary>
public sealed record AddTargetChoice(string Label, string Kind, AppCategory? Category, AppIdentity? App)
{
    public override string ToString() => Label;
}
