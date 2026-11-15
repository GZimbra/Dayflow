using Android.App;
using Android.Content.PM;

namespace Dayflow.App.Platforms.Android;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode |
                           ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(global::Android.OS.Bundle? savedInstanceState)
    {
        // Orientação antes do layout: automática (deitado ou em pé) por padrão; fixa se escolhida em Ajustes.
        var o = (Dayflow.App.Infra.OrientacaoTela)Microsoft.Maui.Storage.Preferences.Default.Get(Dayflow.App.Infra.Orientacao.Chave, 0);
        RequestedOrientation = Dayflow.App.Infra.Orientacao.ParaAndroid(o);
        base.OnCreate(savedInstanceState);
    }
}
