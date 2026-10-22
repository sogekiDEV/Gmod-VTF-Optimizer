using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ContentOptimizer.App.Services
{
    public sealed class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string flags = parameter as string ?? string.Empty;
            bool visible = value is true;
            if (flags.Contains("invert"))
            {
                visible = !visible;
            }

            if (visible)
            {
                return Visibility.Visible;
            }

            return flags.Contains("hidden") ? Visibility.Hidden : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }

    public sealed class EqualsConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            Equals(value, parameter);

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is true ? parameter : Binding.DoNothing;
    }
}
