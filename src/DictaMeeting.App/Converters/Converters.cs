using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace DictaMeeting.App.Converters;

public class HexToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string hex && !string.IsNullOrWhiteSpace(hex))
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(hex);
                return new SolidColorBrush(color);
            }
            catch
            {
                // Ignorar y retornar default
            }
        }
        return new SolidColorBrush(Color.FromRgb(99, 102, 241));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class BooleanToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool val = value is bool b && b;
        if (Invert) val = !val;
        return val ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is Visibility v)
        {
            bool val = v == Visibility.Visible;
            return Invert ? !val : val;
        }
        return false;
    }
}

public class StringToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool hasText = value is string str && !string.IsNullOrWhiteSpace(str);
        if (Invert) hasText = !hasText;
        return hasText ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class EqualityToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool equals = string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.OrdinalIgnoreCase);
        if (Invert) equals = !equals;
        return equals ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class NullToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool isNull = value == null;
        if (Invert) isNull = !isNull;
        return isNull ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}

public static class ActaDetailLevelExtensions
{
    public static string ToLocalizedDisplay(this DictaMeeting.Meetings.Enums.ActaDetailLevel level, DictaMeeting.App.Services.LocalizationManager? loc = null)
    {
        var lm = loc ?? DictaMeeting.App.Services.LocalizationManager.Instance;
        return level switch
        {
            DictaMeeting.Meetings.Enums.ActaDetailLevel.Breve => lm.GetString("Acta_Detail_Breve"),
            DictaMeeting.Meetings.Enums.ActaDetailLevel.Normal => lm.GetString("Acta_Detail_Normal"),
            DictaMeeting.Meetings.Enums.ActaDetailLevel.Detallada => lm.GetString("Acta_Detail_Detallada"),
            DictaMeeting.Meetings.Enums.ActaDetailLevel.Exhaustiva => lm.GetString("Acta_Detail_Exhaustiva"),
            _ => level.ToString()
        };
    }
}

public class ActaDetailLevelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is DictaMeeting.Meetings.Enums.ActaDetailLevel level)
        {
            return level.ToLocalizedDisplay();
        }
        return value?.ToString() ?? string.Empty;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}
