using Dayflow.App.ViewModels;

namespace Dayflow.App.Views;

public partial class EditorRotinaPage : ContentPage
{
    public EditorRotinaPage(EditorRotinaViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
