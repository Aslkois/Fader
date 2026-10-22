using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Fader.App.ViewModels;
using Fader.Core.Apps;
using Fader.Core.Profiles;

namespace Fader.App.Views;

public partial class AppRowView : UserControl
{
    public AppRowView()
    {
        InitializeComponent();
        ContextMenuOpening += OnContextMenuOpening;
        PreviewMouseWheel += OnWheel;
        PreviewMouseLeftButtonDown += OnMouseDown;
    }

    private AppRowViewModel? Row => DataContext as AppRowViewModel;

    // The wheel scrolls the list until a slider has focus; then it adjusts that slider.
    // This keeps a casual scroll from changing a volume.
    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if (Row is not { } row || Keyboard.FocusedElement is not Slider slider || !slider.IsMouseOver) return;
        var step = row.Owner.Services.Config.VolumeStepPercent;
        slider.Value = Math.Clamp(slider.Value + Math.Sign(e.Delta) * step, 0, 100);
        e.Handled = true;
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        for (var d = e.OriginalSource as DependencyObject; d is not null; d = VisualTreeHelper.GetParent(d))
        {
            if (d is Slider slider)
            {
                Keyboard.Focus(slider);
                return;
            }
            if (d == this) return;
        }
    }

    private void OnContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (Row is not { } row || ContextMenu is null)
        {
            e.Handled = true;
            return;
        }
        BuildMenu(ContextMenu, row);
    }

    private static void BuildMenu(ContextMenu menu, AppRowViewModel row)
    {
        var owner = row.Owner;
        var services = owner.Services;
        menu.Items.Clear();

        menu.Items.Add(Item(row.IsPinned ? "Unpin from list" : "Pin to list", () => owner.TogglePin(row)));
        menu.Items.Add(new Separator());

        if (!row.Identity.IsSystemSounds)
        {
            var current = owner.CategoryOf(row);
            var counts = new MenuItem { Header = "Counts as" };
            foreach (var (category, label) in new[]
            {
                (AppCategory.Game, "Game"), (AppCategory.Chat, "Chat app"), (AppCategory.Music, "Music"),
                (AppCategory.Browser, "Browser"), (AppCategory.Other, "Other"),
            })
            {
                var c = category;
                var item = Item(label, () => owner.SetCategory(row, c));
                item.IsChecked = current == c;
                counts.Items.Add(item);
            }
            menu.Items.Add(counts);
        }

        var save = new MenuItem { Header = "Use this level in profile" };
        var remove = new MenuItem { Header = "Remove from profile" };
        foreach (var profile in services.Profiles.Profiles)
        {
            var p = profile;
            var existing = services.Profiles.FindAppEntry(p.Id, row.Identity);
            var header = existing is null ? p.Name : $"{p.Name}   (now {existing.Volume}%)";
            var saveItem = Item(header, () => owner.SaveLevelToProfile(row, p));
            saveItem.IsEnabled = row.IsRunning;
            save.Items.Add(saveItem);
            if (existing is not null) remove.Items.Add(Item(p.Name, () => owner.RemoveFromProfile(row, p)));
        }
        menu.Items.Add(save);
        if (remove.Items.Count > 0) menu.Items.Add(remove);

        menu.Items.Add(new Separator());
        var reset = Item("Set to 100%", () => row.VolumePercent = 100);
        reset.IsEnabled = row.IsRunning;
        menu.Items.Add(reset);
        var mute = Item(row.IsMuted ? "Unmute" : "Mute", () => row.ToggleMuteCommand.Execute(null));
        mute.IsEnabled = row.IsRunning;
        menu.Items.Add(mute);
    }

    private static MenuItem Item(string header, Action click)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => click();
        return item;
    }
}
