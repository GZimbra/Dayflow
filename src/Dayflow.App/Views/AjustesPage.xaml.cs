using Dayflow.App.ViewModels;

namespace Dayflow.App.Views;

public partial class AjustesPage : ContentPage
{
    private readonly AjustesViewModel _vm;

    public AjustesPage(AjustesViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _vm.Carregar();
    }

    private bool? _estreito;

    /// <summary>Celular em pé: coluna dos seletores encolhe para o rótulo não quebrar em 3 linhas.</summary>
    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        if (width <= 0) return;
        var estreito = Infra.Tela.Estreita(width);
        if (_estreito == estreito) return;
        _estreito = estreito;
        Dispatcher.Dispatch(() => GradeAparencia.ColumnDefinitions[1].Width = new GridLength(estreito ? 170 : 240));
    }
}
