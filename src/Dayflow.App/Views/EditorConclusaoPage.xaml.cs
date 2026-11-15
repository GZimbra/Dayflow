using Dayflow.App.ViewModels;

namespace Dayflow.App.Views;

public partial class EditorConclusaoPage : ContentPage
{
    public EditorConclusaoPage(EditorConclusaoViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
