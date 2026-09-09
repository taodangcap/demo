using System;
using System.Globalization;
using System.Windows.Data;

namespace ShowCuePlayer.Helpers
{
    public class EqualityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value?.ToString() == parameter?.ToString();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b && b)
                return parameter;
            return System.Windows.Data.Binding.DoNothing;
        }
    }

    public class InverseBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b) return !b;
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b) return !b;
            return false;
        }
    }

    public class ScreenActiveBrushConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length >= 3 && values[1] is Models.VideoScreenInfo current)
            {
                var selected = values[0] as Models.VideoScreenInfo;
                bool isSelected = selected != null && selected.DeviceName == current.DeviceName;
                bool isOutputEnabled = values[2] is bool b && b;

                if (isSelected)
                {
                    if (isOutputEnabled)
                        return new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x24, 0x3F, 0x60)); // Active Blue
                    else
                        return new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x4B, 0x4B, 0x4B)); // Selected Grey
                }
            }
            return new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2A, 0x2A, 0x2A)); // Default Dark
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
