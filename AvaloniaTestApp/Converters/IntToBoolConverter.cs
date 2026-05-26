using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace AvaloniaTestApp.Converters;

public class IntToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int selectedIndex && parameter is string targetIndexStr)
        {
            if (int.TryParse(targetIndexStr, out int targetIndex))
            {
                return selectedIndex == targetIndex;
            }
        }
        return false;
    }
    
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}