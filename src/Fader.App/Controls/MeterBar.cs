using System.Windows;
using System.Windows.Media;

namespace Fader.App.Controls;

/// <summary>
/// A thin level bar drawn directly (no template, no child elements) so a meter update invalidates only this
/// element's render, never the surrounding layout.
/// </summary>
public sealed class MeterBar : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(MeterBar),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(MeterBar),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty HotFillProperty = DependencyProperty.Register(
        nameof(HotFill), typeof(Brush), typeof(MeterBar),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackProperty = DependencyProperty.Register(
        nameof(Track), typeof(Brush), typeof(MeterBar),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Level from 0 to 1 on a perceptual scale.</summary>
    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }

    public Brush? Fill { get => (Brush?)GetValue(FillProperty); set => SetValue(FillProperty, value); }

    /// <summary>Used when the level is within about 2 dB of full scale.</summary>
    public Brush? HotFill { get => (Brush?)GetValue(HotFillProperty); set => SetValue(HotFillProperty, value); }

    public Brush? Track { get => (Brush?)GetValue(TrackProperty); set => SetValue(TrackProperty, value); }

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        var radius = h / 2;
        if (Track is { } track) dc.DrawRoundedRectangle(track, null, new Rect(0, 0, w, h), radius, radius);

        var v = Math.Clamp(Value, 0, 1);
        if (v <= 0) return;
        var brush = v >= 0.97 ? HotFill ?? Fill : Fill;
        if (brush is null) return;
        dc.DrawRoundedRectangle(brush, null, new Rect(0, 0, Math.Max(h, w * v), h), radius, radius);
    }
}
