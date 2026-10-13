using Avalonia.Data.Converters;
using Avalonia.Input;
using System;
using System.Globalization;

namespace PhotoOrganizer.Viewer.Converters;

public class BusyCursorConverter : IValueConverter
{
    public static readonly BusyCursorConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? new Cursor(StandardCursorType.Wait) : Cursor.Default;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}