using System;
using System.Globalization;
using System.Windows.Data;

namespace WpfApp1.Controls
{
    // Converts Progress (0..1) and container width to an indicator width (double)
    public class ProgressToWidthConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                if (values == null || values.Length < 2) return 0.0;

                double progress = 0.0;
                double containerWidth = 0.0;

                if (values[0] is double d0) progress = d0;
                if (values[1] is double d1) containerWidth = d1;

                // clamp
                progress = Math.Max(0.0, Math.Min(1.0, progress));
                containerWidth = Math.Max(0.0, containerWidth);

                // subtract a tiny padding so the rounded corners don't overflow
                double padding = 4.0;
                double available = Math.Max(0.0, containerWidth - padding);

                return progress * available;
            }
            catch
            {
                return 0.0;
            }
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
