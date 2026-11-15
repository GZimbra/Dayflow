using Android.App;
using Android.Content;
using Android.OS;
using Android.Runtime;
using Android.Widget;
using Android.Views;
using Process = Android.OS.Process;

namespace Dayflow.App.Platforms.Android;

/// <summary>
/// Captura de falha 100% local. Instalado antes do MAUI (MainApplication).
/// Grava o erro em arquivo (Android/data/com.dayflow.app/files/dayflow-erro.txt) e abre uma tela
/// nativa simples (sem MAUI) com o texto e o botão Compartilhar. Nada sai do aparelho sem o usuário tocar.
/// </summary>
public static class Diagnostico
{
    public const string Arquivo = "dayflow-erro.txt";
    private static Context? _ctx;
    private static int _tratando;

    public static void Instalar(Context ctx)
    {
        _ctx = ctx;
        AndroidEnvironment.UnhandledExceptionRaiser += (_, e) =>
        {
            Registrar(e.Exception, "android");
            e.Handled = AbrirTela();
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Registrar(e.ExceptionObject as Exception, "appdomain");
            AbrirTela();
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Registrar(e.Exception, "task");
            e.SetObserved();
        };
    }

    public static string? Caminho(Context ctx) =>
        (ctx.GetExternalFilesDir(null) ?? ctx.FilesDir)?.AbsolutePath is { } d ? Path.Combine(d, Arquivo) : null;

    public static void Registrar(Exception? ex, string origem)
    {
        if (ex is null || _ctx is null) return;
        try
        {
            var texto = $"""
                Dayflow {Versao(_ctx)}
                Aparelho: {Build.Manufacturer} {Build.Model} · Android {Build.VERSION.Release} (API {(int)Build.VERSION.SdkInt}) · ABI {string.Join(",", Build.SupportedAbis ?? [])}
                Quando (UTC): {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}
                Origem: {origem}

                {ex}

                """;
            if (Caminho(_ctx) is { } c) File.AppendAllText(c, texto);
            File.AppendAllText(Path.Combine(_ctx.FilesDir!.AbsolutePath, Arquivo), texto);
            global::Android.Util.Log.Error("Dayflow", texto);
        }
        catch (Exception falha) when (falha is IOException or UnauthorizedAccessException or Java.Lang.Exception)
        {
            // sem onde gravar: nada a fazer
        }
    }

#pragma warning disable CA1422, CA1416 // GetPackageInfo(string,int): obsoleto na API 33, mas é o único disponível na 26
    private static string Versao(Context c)
    {
        try { return c.PackageManager?.GetPackageInfo(c.PackageName!, 0)?.VersionName ?? "?"; }
        catch (Java.Lang.Exception) { return "?"; }
    }
#pragma warning restore CA1422, CA1416

    private static bool AbrirTela()
    {
        if (_ctx is null || Interlocked.Exchange(ref _tratando, 1) == 1) return false;
        try
        {
            var i = new Intent(_ctx, typeof(ErroActivity));
            i.AddFlags(ActivityFlags.NewTask | ActivityFlags.ClearTask);
            _ctx.StartActivity(i);
            return true;
        }
        catch (Java.Lang.Exception)
        {
            return false;
        }
    }
}

/// <summary>Tela de erro nativa (Views do Android, sem MAUI), para funcionar mesmo se o MAUI falhar ao iniciar.</summary>
[Activity(Label = "Dayflow · erro", Exported = false, Theme = "@android:style/Theme.Material.Light.NoActionBar",
    ExcludeFromRecents = true)]
public sealed class ErroActivity : Activity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        var caminho = Diagnostico.Caminho(this);
        var texto = caminho is not null && File.Exists(caminho) ? File.ReadAllText(caminho) : "Sem detalhes gravados.";
        if (texto.Length > 60_000) texto = texto[^60_000..];

        var dp = Resources!.DisplayMetrics!.Density;
        var raiz = new LinearLayout(this) { Orientation = Orientation.Vertical };
        raiz.SetPadding((int)(24 * dp), (int)(32 * dp), (int)(24 * dp), (int)(16 * dp));
        raiz.SetBackgroundColor(global::Android.Graphics.Color.White);

        var titulo = new TextView(this) { Text = "O Dayflow encontrou um erro" };
        titulo.SetTextSize(global::Android.Util.ComplexUnitType.Sp, 22);
        titulo.SetTextColor(global::Android.Graphics.Color.ParseColor("#111111"));
        raiz.AddView(titulo);

        var sub = new TextView(this)
        {
            Text = "Toque em Compartilhar e envie o texto para o desenvolvedor. " +
                   $"Uma cópia fica em {caminho ?? "(indisponível)"}. Nada é enviado automaticamente.",
        };
        sub.SetTextColor(global::Android.Graphics.Color.ParseColor("#6B6B6B"));
        sub.SetPadding(0, (int)(8 * dp), 0, (int)(12 * dp));
        raiz.AddView(sub);

        var botoes = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        var compartilhar = new global::Android.Widget.Button(this) { Text = "Compartilhar" };
        compartilhar.SetBackgroundColor(global::Android.Graphics.Color.ParseColor("#F26A1B"));
        compartilhar.SetTextColor(global::Android.Graphics.Color.White);
        compartilhar.Click += (_, _) =>
        {
            var envio = new Intent(Intent.ActionSend);
            envio.SetType("text/plain");
            envio.PutExtra(Intent.ExtraSubject, "Dayflow · relatório de erro");
            envio.PutExtra(Intent.ExtraText, texto);
            StartActivity(Intent.CreateChooser(envio, "Compartilhar relatório de erro"));
        };
        var limpar = new global::Android.Widget.Button(this) { Text = "Apagar e fechar" };
        limpar.Click += (_, _) =>
        {
            foreach (var f in new[] { caminho, Path.Combine(FilesDir!.AbsolutePath, Diagnostico.Arquivo) })
                if (f is not null && File.Exists(f)) File.Delete(f);
            FinishAndRemoveTask();
            Process.KillProcess(Process.MyPid());
        };
        var reabrir = new global::Android.Widget.Button(this) { Text = "Reabrir o app" };
        reabrir.Click += (_, _) =>
        {
            var i = new Intent(this, typeof(MainActivity));
            i.AddFlags(ActivityFlags.NewTask | ActivityFlags.ClearTask);
            StartActivity(i);
            Finish();
        };
        botoes.AddView(compartilhar);
        botoes.AddView(reabrir);
        botoes.AddView(limpar);
        raiz.AddView(botoes);

        var rolagem = new global::Android.Widget.ScrollView(this);
        var detalhe = new TextView(this) { Text = texto };
        detalhe.SetTextIsSelectable(true);
        detalhe.SetTypeface(global::Android.Graphics.Typeface.Monospace, global::Android.Graphics.TypefaceStyle.Normal);
        detalhe.SetTextSize(global::Android.Util.ComplexUnitType.Sp, 11);
        detalhe.SetTextColor(global::Android.Graphics.Color.ParseColor("#111111"));
        detalhe.SetPadding(0, (int)(16 * dp), 0, 0);
        rolagem.AddView(detalhe);
        raiz.AddView(rolagem, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1f));

        SetContentView(raiz);
    }
}
