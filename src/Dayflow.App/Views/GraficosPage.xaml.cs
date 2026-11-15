using Dayflow.App.ViewModels;

namespace Dayflow.App.Views;

public partial class GraficosPage : ContentPage
{
    private readonly GraficosViewModel _vm;

    public GraficosPage(GraficosViewModel vm)
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
        // Tabela de economia: ocupa a largura útil (página − margens − cartão), mínimo 900 (rola em pé).
        var util = Math.Min(width, 1280) - 56 - 42;
        var alvo = Math.Max(900, util);
        if (Math.Abs(TabelaEconomia.WidthRequest - alvo) > 1) Dispatcher.Dispatch(() => TabelaEconomia.WidthRequest = alvo);
        var largo = width >= Infra.Responsivo.LarguraLarga;
        if (_largo == largo) return;
        _largo = largo;
        Dispatcher.Dispatch(() =>
        {
            Infra.Responsivo.Reorganizar(Grade, largo ? 2 : 1);
        });
    }
}
