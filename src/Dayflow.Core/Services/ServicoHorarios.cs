using Dayflow.Core.Data;
using Dayflow.Core.Domain;
using Microsoft.Extensions.Logging;

namespace Dayflow.Core.Services;

/// <summary>
/// Horário planejado ("vou fazer às 14h") de uma tarefa num dia. Planejamento, não registro de tempo:
/// o tempo gasto continua vindo do cronômetro/lançamento manual (RegistroDiario).
/// </summary>
public sealed class ServicoHorarios(BancoDayflow banco, ILogger<ServicoHorarios> log)
{
    public const int DuracaoPadraoMin = 60;
    public const int PassoMin = 15;

    public Task DefinirAsync(int tarefaId, DateOnly dia, int inicioMin, int duracaoMin = DuracaoPadraoMin, CancellationToken ct = default) =>
        banco.EscreverAsync(c =>
        {
            if (inicioMin is < 0 or > 1439)
                throw new RegraDeNegocioException("horario.inicio", "Horário inválido.");
            if (duracaoMin < PassoMin || inicioMin + duracaoMin > 1440)
                throw new RegraDeNegocioException("horario.duracao", "A atividade precisa ter ao menos 15 min e terminar até 24:00.");
            var t = NucleoEscrita.ObterTarefa(c, tarefaId).ParaDominio();
            if (!t.VisivelEm(dia))
                throw new RegraDeNegocioException("horario.dia", "A tarefa não está na agenda deste dia.");
            c.Execute("""
                INSERT INTO HorarioDia (TarefaId, Data, InicioMin, DuracaoMin) VALUES (?1, ?2, ?3, ?4)
                ON CONFLICT (TarefaId, Data) DO UPDATE SET InicioMin = excluded.InicioMin, DuracaoMin = excluded.DuracaoMin
                """, tarefaId, Mapeamento.Dia(dia), inicioMin, duracaoMin);
            log.LogInformation("Horário planejado para tarefa {Id}", tarefaId);
        }, ct);

    public Task RemoverAsync(int tarefaId, DateOnly dia, CancellationToken ct = default) =>
        banco.EscreverAsync(c => { c.Execute("DELETE FROM HorarioDia WHERE TarefaId = ?1 AND Data = ?2", tarefaId, Mapeamento.Dia(dia)); }, ct);

    public static string Formatar(int minutos) => $"{minutos / 60:00}:{minutos % 60:00}";
}
