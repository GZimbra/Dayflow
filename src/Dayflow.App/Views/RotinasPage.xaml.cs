using Dayflow.App.ViewModels;

namespace Dayflow.App.Views;

public partial class RotinasPage : ContentPage
{
    private readonly RotinasViewModel _vm;

    public RotinasPage(RotinasViewModel vm)
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

    private int _colunas;

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        if (width <= 0) return;
        var colunas = Infra.Tela.Estreita(width) ? 1 : 2;
        if (_colunas == colunas) return;
        _colunas = colunas;
        Dispatcher.Dispatch(() =>
        {
            LayoutGrade.Span = colunas;
            BotaoNova.Text = colunas == 1 ? "+ Nova" : "+  Nova rotina";
        });
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _vm.AoSumir();
    }
}
