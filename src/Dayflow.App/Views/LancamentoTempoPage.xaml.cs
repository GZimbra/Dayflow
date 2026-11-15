using Dayflow.App.ViewModels;

namespace Dayflow.App.Views;

public partial class LancamentoTempoPage : ContentPage
{
    public LancamentoTempoPage(LancamentoTempoViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
