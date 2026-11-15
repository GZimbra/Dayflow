using System.Globalization;

namespace Dayflow.App.Infra;

public sealed class NegarConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
}

/// <summary>Reorganiza os filhos de um Grid em N colunas (ordem de declaração). Usado ao girar a tela.</summary>
public static class Responsivo
{
    public const double LarguraLarga = 900;

    public static void Reorganizar(Grid g, int colunas)
    {
        ArgumentNullException.ThrowIfNull(g);
        var filhos = g.Children.OfType<View>().ToList();
        g.ColumnDefinitions.Clear();
        g.RowDefinitions.Clear();
        for (var c = 0; c < colunas; c++) g.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        for (var r = 0; r < (filhos.Count + colunas - 1) / colunas; r++) g.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        for (var i = 0; i < filhos.Count; i++)
        {
            Grid.SetColumn(filhos[i], i % colunas);
            Grid.SetRow(filhos[i], i / colunas);
        }
    }
}
