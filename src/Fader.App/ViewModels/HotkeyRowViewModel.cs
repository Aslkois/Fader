using System.Windows.Input;
using Fader.App.Services;
using Fader.Core.Hotkeys;
using Fader.Native;

namespace Fader.App.ViewModels;

/// <summary>A captured key combination: modifiers plus a Windows virtual-key code.</summary>
public readonly record struct HotkeyGesture(HotkeyModifiers Modifiers, uint Key)
{
    public bool IsEmpty => Key == 0;
    public override string ToString() => HotkeyFormatter.Format(Modifiers, Key);
}

public sealed class HotkeyRowViewModel : ObservableObject
{
    private readonly AppServices _services;

    public HotkeyRowViewModel(AppServices services, HotkeyAction action)
    {
        _services = services;
        Action = action;
        ResetCommand = new RelayCommand(Reset);
        ClearCommand = new RelayCommand(() => Gesture = default);
    }

    public HotkeyAction Action { get; }
    public ICommand ResetCommand { get; }
    public ICommand ClearCommand { get; }

    public string Label => HotkeyDefaults.Describe(Action, _services.Actions.ChatName());

    private HotkeyBinding Binding => _services.Config.Hotkeys.First(b => b.Action == Action);

    public HotkeyGesture Gesture
    {
        get => new(Binding.Modifiers, Binding.Key);
        set
        {
            var b = Binding;
            if (b.Modifiers == value.Modifiers && b.Key == value.Key) return;
            b.Modifiers = value.Modifiers;
            b.Key = value.Key;
            Changed();
        }
    }

    public bool Enabled
    {
        get => Binding.Enabled;
        set
        {
            if (Binding.Enabled == value) return;
            Binding.Enabled = value;
            Changed();
        }
    }

    public string StatusText
    {
        get
        {
            var b = Binding;
            if (!b.IsAssigned) return "No shortcut";
            if (!b.Enabled) return "Off";
            var reg = _services.HotkeyResults.FirstOrDefault(r => r.Action == Action);
            return reg is null || reg.Status == HotkeyStatus.Registered ? "Active" : reg.Message ?? "Could not be registered";
        }
    }

    public bool HasProblem
    {
        get
        {
            var reg = _services.HotkeyResults.FirstOrDefault(r => r.Action == Action);
            return reg is not null && !reg.IsOk;
        }
    }

    public void RefreshStatus()
    {
        Raise(nameof(StatusText));
        Raise(nameof(HasProblem));
        Raise(nameof(Label));
    }

    private void Reset()
    {
        var d = HotkeyDefaults.For(Action);
        var b = Binding;
        b.Modifiers = d.Modifiers;
        b.Key = d.Key;
        b.Enabled = true;
        Changed();
    }

    private void Changed()
    {
        _services.Settings.MarkDirty();
        _services.ApplyHotkeys();
        Raise(nameof(Gesture));
        Raise(nameof(Enabled));
        RefreshStatus();
    }
}
