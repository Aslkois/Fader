using System.Windows;
using Fader.Core.Settings;
using Fader.Native;
using Microsoft.Win32;

namespace Fader.App.Services;

/// <summary>Swaps the colour dictionary at runtime and follows the Windows setting when the mode is "System".</summary>
public sealed class ThemeService : IDisposable
{
    private ThemePreference _mode = ThemePreference.System;
    private bool _disposed;

    public ThemeService()
    {
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    public bool IsDark { get; private set; }
    public ThemePreference Mode => _mode;

    public event Action<bool>? Changed;

    public void Apply(ThemePreference mode)
    {
        _mode = mode;
        var dark = mode switch
        {
            ThemePreference.Dark => true,
            ThemePreference.Light => false,
            _ => SystemTheme.AppsUseDarkTheme(),
        };

        var resources = Application.Current.Resources.MergedDictionaries;
        var source = new Uri(dark ? "Themes/Dark.xaml" : "Themes/Light.xaml", UriKind.Relative);
        if (resources.Count == 0) resources.Add(new ResourceDictionary { Source = source });
        else resources[0] = new ResourceDictionary { Source = source };

        var changed = dark != IsDark;
        IsDark = dark;
        if (changed) Changed?.Invoke(dark);
    }

    /// <summary>Light, then dark, then follow Windows.</summary>
    public ThemePreference NextMode() => _mode switch
    {
        ThemePreference.System => ThemePreference.Light,
        ThemePreference.Light => ThemePreference.Dark,
        _ => ThemePreference.System,
    };

    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (_mode != ThemePreference.System) return;
        if (e.Category is not (UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.VisualStyle)) return;
        Application.Current?.Dispatcher.BeginInvoke(() => Apply(ThemePreference.System));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
    }
}
