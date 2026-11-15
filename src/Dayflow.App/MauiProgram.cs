using Dayflow.App.Infra;
using Dayflow.App.ViewModels;
using Dayflow.App.Views;
using Dayflow.Core;
using Dayflow.Core.Infra;
using Dayflow.Core.Services;
using SkiaSharp.Views.Maui.Controls.Hosting;
using Microsoft.Extensions.Logging;

namespace Dayflow.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var b = MauiApp.CreateBuilder();
        b.UseMauiApp<App>()
         .UseSkiaSharp() // renderização local dos gráficos
         .ConfigureFonts(f =>
         {
             f.AddFont("Manrope-Regular.ttf", "ManropeRegular");
             f.AddFont("Manrope-SemiBold.ttf", "ManropeSemiBold");
             f.AddFont("Manrope-Bold.ttf", "ManropeBold");
             f.AddFont("IBMPlexMono-Medium.ttf", "PlexMono");
         });

        // Diagnóstico local: arquivo rotativo, exportável pelo usuário. Sem telemetria.
        var logs = new LogArquivoProvider(Path.Combine(FileSystem.AppDataDirectory, "logs"));
        b.Logging.ClearProviders();
        b.Logging.AddProvider(logs);
        b.Logging.SetMinimumLevel(LogLevel.Information);
        b.Services.AddSingleton(logs);

        b.Services.AddDayflowCore(Path.Combine(FileSystem.AppDataDirectory, "dayflow.db3"));

        b.Services.AddSingleton<IDialogos, Dialogos>();
        b.Services.AddSingleton<INavegacao, Navegacao>();
        b.Services.AddSingleton<IConfiguracoes, Configuracoes>();
        b.Services.AddSingleton<IExportador, Exportador>();
#if ANDROID
        b.Services.AddSingleton<IAgendadorLembretes, Dayflow.App.Platforms.Android.AgendadorLembretesAndroid>();
#else
        b.Services.AddSingleton<IAgendadorLembretes, AgendadorNulo>();
#endif

        // Telas principais: singletons (estado preservado ao trocar de aba).
        b.Services.AddSingleton<AgendaViewModel>().AddSingleton<AgendaPage>();
        b.Services.AddSingleton<RotinasViewModel>().AddSingleton<RotinasPage>();
        b.Services.AddSingleton<RelatoriosViewModel>().AddSingleton<RelatoriosPage>();
        b.Services.AddSingleton<GraficosViewModel>().AddSingleton<GraficosPage>();
        b.Services.AddSingleton<ProjetosViewModel>().AddSingleton<ProjetosPage>();
        b.Services.AddSingleton<AjustesViewModel>().AddSingleton<AjustesPage>();
        // Modais: transientes.
        b.Services.AddTransient<EditorTarefaViewModel>().AddTransient<EditorTarefaPage>();
        b.Services.AddTransient<LancamentoTempoViewModel>().AddTransient<LancamentoTempoPage>();
        b.Services.AddTransient<EditorRotinaViewModel>().AddTransient<EditorRotinaPage>();
        b.Services.AddTransient<EditorProjetoViewModel>().AddTransient<EditorProjetoPage>();
        b.Services.AddTransient<EditorConclusaoViewModel>().AddTransient<EditorConclusaoPage>();

        return b.Build();
    }
}

#if !ANDROID
/// <summary>Usado só no alvo net10.0 (compilação/CI). Android usa AlarmManager.</summary>
internal sealed class AgendadorNulo : IAgendadorLembretes
{
    public Task<bool> AplicarAsync(Dayflow.Core.Domain.ConfiguracaoLembretes cfg, CancellationToken ct = default) => Task.FromResult(true);
}
#endif
