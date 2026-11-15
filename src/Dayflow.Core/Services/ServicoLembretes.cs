using Dayflow.Core.Domain;

namespace Dayflow.Core.Services;

public sealed record ConteudoLembrete(string Titulo, string Texto, bool CronometroRodando, int? ProximaTarefaId);

/// <summary>Contrato do agendador nativo (Android: AlarmManager + BroadcastReceiver).</summary>
public interface IAgendadorLembretes
{
    /// <summary>Agenda (ou cancela, se desativado) o próximo disparo. False se POST_NOTIFICATIONS negada.</summary>
    Task<bool> AplicarAsync(ConfiguracaoLembretes cfg, CancellationToken ct = default);
}

/// <summary>
/// Monta o texto do lembrete horário. Null = não notificar (sem cronômetro e sem pendências).
/// Texto contém títulos de tarefas: só vai para a notificação local, nunca para o log.
/// </summary>
public sealed class ServicoLembretes(ServicoTarefas tarefas, ServicoCronometro cronometro, IRelogioCivil relogio)
{
    public async Task<ConteudoLembrete?> MontarAsync(CancellationToken ct = default)
    {
        var hoje = relogio.Hoje;
        var itens = await tarefas.DoDiaAsync(hoje, ct).ConfigureAwait(false);
        var estado = await cronometro.EstadoAsync(ct).ConfigureAwait(false);
        var abertas = itens.Where(i => i.Tarefa.EstaAberta).ToList();
        var rotinas = abertas.Count(i => i.Tarefa.EhRotina);
        var avulsas = abertas.Count - rotinas;
        var proxima = abertas.FirstOrDefault(i => !i.CronometroRodando)?.Tarefa;

        if (estado is not null)
        {
            var atual = itens.FirstOrDefault(i => i.Tarefa.Id == estado.TarefaId)?.Tarefa.Titulo ?? "tarefa";
            return new ConteudoLembrete(
                $"Em andamento · {FormatarDuracao(estado.Decorrido)}",
                $"{atual}" + (abertas.Count > 1 ? $" · mais {abertas.Count - 1} aberta(s) hoje" : ""),
                true, null);
        }
        if (abertas.Count == 0) return null;

        var partes = new List<string>();
        if (avulsas > 0) partes.Add($"{avulsas} pendente{(avulsas > 1 ? "s" : "")}");
        if (rotinas > 0) partes.Add($"{rotinas} rotina{(rotinas > 1 ? "s" : "")} por fazer");
        return new ConteudoLembrete(
            string.Join(", ", partes),
            proxima is null ? "Nenhuma atividade em andamento." : $"Próxima: {proxima.Titulo}",
            false, proxima?.Id);
    }

    /// <summary>Ação "Iniciar próxima" da notificação.</summary>
    public async Task<bool> IniciarProximaAsync(CancellationToken ct = default)
    {
        var c = await MontarAsync(ct).ConfigureAwait(false);
        if (c?.ProximaTarefaId is not { } id) return false;
        await cronometro.IniciarAsync(id, ct).ConfigureAwait(false);
        return true;
    }

    public static string FormatarDuracao(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}h{t.Minutes:00}" : $"{t.Minutes} min";
}
