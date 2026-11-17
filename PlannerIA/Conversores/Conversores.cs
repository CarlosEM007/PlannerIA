using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PlannerIA.Conversores;

public class BoolParaVisibilidade : IValueConverter
{
    public bool Inverter { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var b = value switch
        {
            bool v => v,
            int i => i != 0,
            string s => !string.IsNullOrWhiteSpace(s),
            null => false,
            _ => true
        };
        if (Inverter) b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public class BoolInverso : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
}

/// <summary>Compara o valor com o parâmetro (para RadioButtons ligados a um enum/string).</summary>
public class IgualA : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.Ordinal);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? parameter : Binding.DoNothing;
}
