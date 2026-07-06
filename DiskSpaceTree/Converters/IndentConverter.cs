using System.Globalization;

namespace DiskSpaceTree.Converters;

public sealed class IndentConverter : IValueConverter
{
    public int IndentSize { get; set; } = 20;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int level)
        {
            return new Thickness(level * IndentSize, 0, 0, 0);
        }

        return new Thickness(0);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
