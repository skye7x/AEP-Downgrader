using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AEPDowngrader.Converters
{
    /// <summary>Converts a bool to Visibility.Visible/Collapsed. Used to show the
    /// "EXPERIMENTAL" badge in the target-version combo box, mirroring
    /// TargetVersionDelegate.paint's badge drawing in the Python app.</summary>
    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool flag = value is bool b && b;
            if (parameter is string s && s == "Invert") flag = !flag;
            return flag ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
