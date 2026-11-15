using Dayflow.App.ViewModels;

namespace Dayflow.App.Views;

public partial class EditorTarefaPage : ContentPage
{
    public EditorTarefaPage(EditorTarefaViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
