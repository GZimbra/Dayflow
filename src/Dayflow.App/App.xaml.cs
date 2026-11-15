using Dayflow.App.Infra;
using Dayflow.Core.Services;
using Microsoft.Extensions.Logging;

namespace Dayflow.App;

public partial class App : Application
{
    private readonly IServiceProvider _sp;

    public App(IServiceProvider sp, IConfiguracoes cfg)
    {
        ArgumentNullException.ThrowIfNull(cfg);
        _sp = sp;
        InitializeComponent();
        // Fontes/alturas por classe de aparelho (celular x tablet), antes de qualquer tela existir.
        Tela.AplicarDensidade(Resources);
        UserAppTheme = cfg.Tema;
    }

    protected override Window CreateWindow(IActivationState? activationState) =>
        new(new AppShell()) { Title = "Dayflow" };

    protected override async void OnStart()
    {
        base.OnStart();
        // Reagenda o próximo lembrete (cobre atualização do app e mudança de fuso).
        // async void: exceção aqui derrubaria o app; falha de lembrete não pode impedir o uso.
        try
        {
            var cfg = _sp.GetRequiredService<IConfiguracoes>();
            await _sp.GetRequiredService<IAgendadorLembretes>().AplicarAsync(cfg.Lembretes);
        }
        catch (Exception ex)
        {
            _sp.GetService<ILogger<App>>()?.LogError(ex, "Falha ao reagendar lembretes no início");
#if ANDROID
            Platforms.Android.Diagnostico.Registrar(ex, "OnStart/lembretes");
#endif
        }
    }
}
