using Dayflow.App.ViewModels;

namespace Dayflow.App.Views;

public partial class RelatoriosPage : ContentPage
{
    private readonly RelatoriosViewModel _vm;

    public RelatoriosPage(RelatoriosViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        // Só recarrega se algo mudou desde a última vez (trocar de aba não refaz consultas nem gráficos).
        if (_vm.AoAparecer()) await _vm.CarregarAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _vm.AoSumir();
    }

    private bool? _largo;

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        if (width <= 0) return;
        // página (2×16..20) + cartão (2×20) de margem; mínimo legível 420.
        var util = Math.Min(width, 1200) - 80;
        if (Math.Abs(TabelaEconomia.WidthRequest - Math.Max(420, util)) > 1)
            Dispatcher.Dispatch(() => TabelaEconomia.WidthRequest = Math.Max(420, util));
        var largo = width >= Infra.Responsivo.LarguraLarga;
        if (_largo == largo) return;
        _largo = largo;
        Dispatcher.Dispatch(() =>
        {
            Infra.Responsivo.Reorganizar(Kpis, largo ? 4 : 2);
            Infra.Responsivo.Reorganizar(Listas, largo ? 2 : 1);
        });
    }
}
