using Dayflow.App.ViewModels;

namespace Dayflow.App.Views;

public partial class ProjetosPage : ContentPage
{
    private readonly ProjetosViewModel _vm;

    public ProjetosPage(ProjetosViewModel vm)
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

    /// <summary>Colunas por largura (1 no celular em pé, 2 médio, 3 tablet); cabeçalho quebra em 2 linhas no estreito.</summary>
    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        if (width <= 0) return;
        var colunas = Infra.Tela.Estreita(width) ? 1 : width < Infra.Responsivo.LarguraLarga ? 2 : 3;
        if (_colunas == colunas) return;
        _colunas = colunas;
        var estreito = colunas == 1;
        Dispatcher.Dispatch(() =>
        {
            LayoutGrade.Span = colunas;
            BotaoNovo.Text = estreito ? "+ Novo" : "+  Novo projeto";
            Grid.SetRow(LinhaArquivados, estreito ? 1 : 0);
            Grid.SetColumn(LinhaArquivados, estreito ? 0 : 1);
            Grid.SetColumnSpan(LinhaArquivados, estreito ? 3 : 1);
            LinhaArquivados.HorizontalOptions = estreito ? LayoutOptions.Start : LayoutOptions.Fill;
        });
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _vm.AoSumir();
    }
}
