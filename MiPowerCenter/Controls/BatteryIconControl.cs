using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace MiPowerCenter.Controls;

public sealed class BatteryIconControl : UserControl
{
    public static readonly DependencyProperty LevelProperty =
        DependencyProperty.Register(nameof(Level), typeof(double), typeof(BatteryIconControl),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender, OnChanged));

    public static readonly DependencyProperty IsChargingProperty =
        DependencyProperty.Register(nameof(IsCharging), typeof(bool), typeof(BatteryIconControl),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender, OnChanged));

    public double Level { get => (double)GetValue(LevelProperty); set => SetValue(LevelProperty, value); }
    public bool IsCharging { get => (bool)GetValue(IsChargingProperty); set => SetValue(IsChargingProperty, value); }

    private readonly Viewbox _view;
    private readonly Rectangle _fill;
    private readonly Path _bolt;

    public BatteryIconControl()
    {
        var canvas = new Canvas { Width = 26, Height = 14 };
        var body = PathString.Parse(
            "M 3.45185 2.25 L 19.54815 2.25 A 2.20185 2.20185 0 0 1 21.75 4.45185 " +
            "L 21.75 9.54815 A 2.20185 2.20185 0 0 1 19.54815 11.75 L 3.45185 11.75 " +
            "A 2.20185 2.20185 0 0 1 1.25 9.54815 L 1.25 4.45185 " +
            "A 2.20185 2.20185 0 0 1 3.45185 2.25 Z",
            new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)), null, 1.5);
        canvas.Children.Add(body);

        var cap = PathString.Parse(
            "M 23 5.8 C 23 5.35817 23.3358 5 23.75 5 C 24.1642 5 24.5 5.35817 24.5 5.8 " +
            "L 24.5 8.2 C 24.5 8.64183 24.1642 9 23.75 9 C 23.3358 9 23 8.64183 23 8.2 Z",
            new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)), null, 0);
        canvas.Children.Add(cap);

        _fill = new Rectangle
        {
            Width = 15, Height = 6,
            RadiusX = 1, RadiusY = 1,
            Fill = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33))
        };
        Canvas.SetLeft(_fill, 3); Canvas.SetTop(_fill, 4);
        canvas.Children.Add(_fill);

        _bolt = PathString.Parse(
            "M12.2998 6.26667 H15 L9.10526 12.5 L10.7893 7.90713 L10.7002 7.73333 H8 " +
            "L13.8947 1.5 L12.2103 6.09287 Z",
            new SolidColorBrush(Colors.White), null, 0);
        _bolt.Opacity = 0.8;
        canvas.Children.Add(_bolt);

        _view = new Viewbox { Child = canvas, Stretch = Stretch.Fill };
        Content = _view;
    }

    protected override Size MeasureOverride(Size constraint)
    {
        double s = Math.Min(constraint.Width, constraint.Height * 26.0 / 14.0);
        if (double.IsInfinity(s)) s = 68;
        return new Size(s, s * 14.0 / 26.0);
    }

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var me = (BatteryIconControl)d;
        double level = Math.Max(0, Math.Min(100, me.Level));
        if (double.IsNaN(level)) level = 0;
        me._fill.Width = 15 * level / 100.0;
        me._fill.Fill = me.IsCharging
            ? new SolidColorBrush(Color.FromRgb(0x10, 0xC5, 0x50))
            : new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33));
        me._bolt.Visibility = me.IsCharging ? Visibility.Visible : Visibility.Collapsed;
    }
}

internal static class PathString
{
    public static Path Parse(string data, Brush fill, Brush stroke, double thickness)
    {
        var g = Geometry.Parse(data);
        g.Freeze();
        return new Path { Data = g, Fill = fill, Stroke = stroke, StrokeThickness = thickness };
    }
}