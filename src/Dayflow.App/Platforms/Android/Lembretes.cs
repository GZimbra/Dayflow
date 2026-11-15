using Android.App;
using Android.Content;
using AndroidX.Core.App;
using CommunityToolkit.Mvvm.Messaging;
using Dayflow.App.Infra;
using Dayflow.Core.Domain;
using Dayflow.Core.Services;
using Microsoft.Extensions.Logging;

namespace Dayflow.App.Platforms.Android;

/// <summary>
/// Agenda UM disparo por vez (o próximo da janela). Cada disparo reagenda o seguinte.
/// setAndAllowWhileIdle: inexato, dispara mesmo em Doze, não exige SCHEDULE_EXACT_ALARM.
/// </summary>
public sealed class AgendadorLembretesAndroid(ILogger<AgendadorLembretesAndroid> log) : IAgendadorLembretes
{
    public async Task<bool> AplicarAsync(ConfiguracaoLembretes cfg, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(cfg);
        var ctx = global::Android.App.Application.Context;
        Cancelar(ctx);
        if (!cfg.Ativo) return true;

        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            var st = await Permissions.CheckStatusAsync<Permissions.PostNotifications>();
            if (st != PermissionStatus.Granted)
                st = await MainThread.InvokeOnMainThreadAsync(Permissions.RequestAsync<Permissions.PostNotifications>);
            if (st != PermissionStatus.Granted) return false;
        }
        Agendar(ctx, cfg, log);
        return true;
    }

    internal static void Agendar(Context ctx, ConfiguracaoLembretes cfg, ILogger? log)
    {
        Cancelar(ctx);
        var prox = AgendaLembretes.Proximo(cfg, DateTime.UtcNow, TimeZoneInfo.Local);
        if (prox is null) return;
        var ms = new DateTimeOffset(prox.Value, TimeSpan.Zero).ToUnixTimeMilliseconds();
        var am = (AlarmManager)ctx.GetSystemService(Context.AlarmService)!;
        am.SetAndAllowWhileIdle(AlarmType.RtcWakeup, ms, IntentDisparo(ctx));
        log?.LogInformation("Lembrete agendado para {Utc:O}", prox.Value);
    }

    internal static void Cancelar(Context ctx)
    {
        var am = (AlarmManager)ctx.GetSystemService(Context.AlarmService)!;
        am.Cancel(IntentDisparo(ctx));
    }

    private static PendingIntent IntentDisparo(Context ctx) => PendingIntent.GetBroadcast(ctx, 100,
        new Intent(ctx, typeof(LembreteReceiver)).SetAction(LembreteReceiver.AcaoDisparo),
        PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent)!;
}

/// <summary>Recebe o alarme e as ações da notificação. Não exportado: só o próprio app dispara.</summary>
[BroadcastReceiver(Enabled = true, Exported = false)]
public sealed class LembreteReceiver : BroadcastReceiver
{
    public const string AcaoDisparo = "com.dayflow.app.LEMBRETE";
    public const string AcaoIniciarProxima = "com.dayflow.app.INICIAR_PROXIMA";
    public const string AcaoSilenciarHoje = "com.dayflow.app.SILENCIAR_HOJE";
    private const string Canal = "lembretes";
    private const int IdNotificacao = 4201;

    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent?.Action is not { } acao) return;
        var pendente = GoAsync();
        _ = Task.Run(async () =>
        {
            var sp = IPlatformApplication.Current?.Services;
            var log = sp?.GetService<ILogger<LembreteReceiver>>();
            try
            {
                if (sp is not null) await Processar(context, acao, sp, log);
            }
            catch (Exception ex)
            {
                log?.LogError(ex, "Falha ao processar lembrete ({Acao})", acao);
            }
            finally
            {
                pendente?.Finish();
            }
        });
    }

    private static async Task Processar(Context ctx, string acao, IServiceProvider sp, ILogger? log)
    {
        var cfgs = sp.GetRequiredService<IConfiguracoes>();
        var lembretes = sp.GetRequiredService<ServicoLembretes>();
        var nm = NotificationManagerCompat.From(ctx);

        switch (acao)
        {
            case AcaoDisparo:
                var c = await lembretes.MontarAsync();
                if (c is not null && nm.AreNotificationsEnabled()) Notificar(ctx, nm, c);
                break;
            case AcaoIniciarProxima:
                nm.Cancel(IdNotificacao);
                if (await lembretes.IniciarProximaAsync()) AvisarUi();
                break;
            case AcaoSilenciarHoje:
                nm.Cancel(IdNotificacao);
                var hoje = sp.GetRequiredService<IRelogioCivil>().Hoje;
                cfgs.Lembretes = cfgs.Lembretes with { SilenciadoAte = hoje };
                break;
        }
        // Sempre agenda o próximo: a cadeia nunca se perde.
        AgendadorLembretesAndroid.Agendar(ctx, cfgs.Lembretes, log);
    }

    private static void AvisarUi() =>
        MainThread.BeginInvokeOnMainThread(() => WeakReferenceMessenger.Default.Send(new DadosAlterados("notificacao")));

    private static void Notificar(Context ctx, NotificationManagerCompat nm, ConteudoLembrete c)
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            var canal = new NotificationChannel(Canal, "Lembretes de hora em hora", NotificationImportance.Default)
            {
                Description = "Atividade em andamento e pendências do dia",
            };
            ((NotificationManager)ctx.GetSystemService(Context.NotificationService)!).CreateNotificationChannel(canal);
        }

        var abrir = PendingIntent.GetActivity(ctx, 200,
            new Intent(ctx, typeof(MainActivity)).SetFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop),
            PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
        var icone = ctx.Resources!.GetIdentifier("ic_relogio", "drawable", ctx.PackageName);

        var b = new NotificationCompat.Builder(ctx, Canal)
            .SetSmallIcon(icone != 0 ? icone : global::Android.Resource.Drawable.IcDialogInfo)
            .SetColor(unchecked((int)0xFFF26A1B))
            .SetContentTitle(c.Titulo)
            .SetContentText(c.Texto)
            .SetStyle(new NotificationCompat.BigTextStyle().BigText(c.Texto))
            .SetContentIntent(abrir)
            .SetAutoCancel(true)
            .SetOnlyAlertOnce(true)
            .SetCategory(NotificationCompat.CategoryReminder)
            .SetPriority(NotificationCompat.PriorityDefault);

        if (c.ProximaTarefaId is not null)
            b.AddAction(0, "Iniciar próxima", Acao(ctx, AcaoIniciarProxima, 301));
        b.AddAction(0, "Pausar lembretes hoje", Acao(ctx, AcaoSilenciarHoje, 302));

        nm.Notify(IdNotificacao, b.Build());
    }

    private static PendingIntent Acao(Context ctx, string acao, int codigo) => PendingIntent.GetBroadcast(ctx, codigo,
        new Intent(ctx, typeof(LembreteReceiver)).SetAction(acao),
        PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent)!;
}

/// <summary>
/// Reagenda após reboot, troca de fuso/hora e atualização do app.
/// Exportado porque recebe broadcasts do sistema; as ações são protegidas (só o sistema envia) e filtradas.
/// </summary>
[BroadcastReceiver(Enabled = true, Exported = true)]
[IntentFilter([Intent.ActionBootCompleted, Intent.ActionTimezoneChanged, Intent.ActionTimeChanged, Intent.ActionMyPackageReplaced])]
public sealed class ReagendarReceiver : BroadcastReceiver
{
    private static readonly HashSet<string> Permitidas =
        [Intent.ActionBootCompleted!, Intent.ActionTimezoneChanged!, Intent.ActionTimeChanged!, Intent.ActionMyPackageReplaced!];

    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent?.Action is not { } a || !Permitidas.Contains(a)) return;
        var sp = IPlatformApplication.Current?.Services;
        if (sp is null) return;
        // Troca de fuso: TimeZoneInfo.Local é cacheado pelo runtime.
        TimeZoneInfo.ClearCachedData();
        AgendadorLembretesAndroid.Agendar(context, sp.GetRequiredService<IConfiguracoes>().Lembretes,
            sp.GetService<ILogger<ReagendarReceiver>>());
    }
}
