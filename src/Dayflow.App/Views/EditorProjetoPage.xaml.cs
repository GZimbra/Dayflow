using Dayflow.App.ViewModels;

namespace Dayflow.App.Views;

public partial class EditorProjetoPage : ContentPage
{
    public EditorProjetoPage(EditorProjetoViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
