using System;
using System.Globalization;
using System.Windows.Media;
using System.Windows.Data;

namespace BotLauncher.Converters
{
    public class BoolToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isRunning)
            {
                // Return green for running, red for stopped
                return new SolidColorBrush(isRunning ? Colors.Green : Colors.Red);
            }
            
            return new SolidColorBrush(Colors.Gray);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
