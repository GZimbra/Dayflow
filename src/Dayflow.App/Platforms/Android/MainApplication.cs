using Android.App;
using Android.Runtime;

namespace Dayflow.App.Platforms.Android;

[Application]
public class MainApplication(IntPtr handle, JniHandleOwnership ownership) : MauiApplication(handle, ownership)
{
    public override void OnCreate()
    {
        // Antes do MAUI: qualquer falha daqui em diante vira arquivo local + tela de erro nativa.
        Diagnostico.Instalar(this);
        base.OnCreate();
    }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
