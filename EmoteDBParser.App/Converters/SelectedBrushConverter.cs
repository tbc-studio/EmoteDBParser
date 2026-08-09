using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace EmoteDBParser.App.Converters;

public sealed class SelectedBrushConverter : IValueConverter
{
    public static readonly SelectedBrushConverter Instance = new();

    private static readonly IBrush SelectedBrush = new SolidColorBrush(Color.Parse("#4FC3F7"));
    private static readonly IBrush UnselectedBrush = Brushes.Transparent;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? SelectedBrush : UnselectedBrush;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
