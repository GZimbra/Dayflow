using Dayflow.Core.Data;
using Dayflow.Core.Data.Linhas;
using Dayflow.Core.Domain;
using Microsoft.Extensions.Logging;

namespace Dayflow.Core.Services;

public sealed class ServicoRegistros(BancoDayflow banco, IRelogioCivil relogio, ILogger<ServicoRegistros> log)
{
    public Task<IReadOnlyList<RegistroDiario>> DaTarefaAsync(int tarefaId, CancellationToken ct = default) =>
        banco.LerAsync<IReadOnlyList<RegistroDiario>>(c => c.Query<RegistroDiarioLinha>(
            "SELECT * FROM RegistroDiario WHERE TarefaId = ? ORDER BY Data", tarefaId).Select(l => l.ParaDominio()).ToList(), ct);

    /// <summary>Lançamento manual: soma ao registro do dia. Dia deve estar dentro da vida da tarefa e não no futuro.</summary>
    public Task LancarManualAsync(int tarefaId, DateOnly dia, int minutos, string? observacao, CancellationToken ct = default) =>
        banco.EscreverAsync(c =>
        {
            if (minutos is <= 0 or > RegistroDiario.MinutosMaxPorDia)
                throw new RegraDeNegocioException("registro.minutos", "Minutos devem estar entre 1 e 1440.");
            var hoje = relogio.Hoje;
            if (dia > hoje) throw new RegraDeNegocioException("registro.futuro", "Não é possível lançar tempo em dia futuro.");

            var t = NucleoEscrita.ObterTarefa(c, tarefaId).ParaDominio();
            if (dia < t.DataInicio)
                throw new RegraDeNegocioException("registro.antes_inicio", "Dia anterior ao início da tarefa.");
            if (t.DataConclusao is { } fim && dia > fim)
                throw new RegraDeNegocioException("registro.apos_fim", "Dia posterior ao encerramento da tarefa.");

            NucleoEscrita.SomarSegundos(c, tarefaId, dia, minutos * 60L, observacao);
            if (t.Status == StatusTarefa.Pendente)
            {
                t.Iniciar(hoje, relogio.AgoraUtc);
                NucleoEscrita.Salvar(c, t);
            }
            log.LogInformation("Lançamento manual de {Minutos} min na tarefa {Id}", minutos, tarefaId);
        }, ct);

    /// <summary>Define o total exato do registro (correção). 0 sem observação remove o registro.</summary>
    public Task AjustarAsync(int registroId, int minutos, string? observacao, CancellationToken ct = default) =>
        banco.EscreverAsync(c =>
        {
            if (minutos is < 0 or > RegistroDiario.MinutosMaxPorDia)
                throw new RegraDeNegocioException("registro.minutos", "Minutos devem estar entre 0 e 1440.");
            var r = c.Find<RegistroDiarioLinha>(registroId)
                ?? throw new RegraDeNegocioException("registro.inexistente", "Registro não encontrado.");
            var obs = string.IsNullOrWhiteSpace(observacao) ? null : observacao.Trim();
            if (obs is { Length: > RegistroDiario.ObservacaoMax })
                throw new RegraDeNegocioException("registro.observacao", "Observação muito longa.");
            if (minutos == 0 && obs is null)
            {
                c.Delete<RegistroDiarioLinha>(registroId);
                return;
            }
            r.TempoGastoMin = minutos;
            r.SegundosResiduais = 0;
            r.Observacao = obs;
            c.Update(r);
        }, ct);

    public Task RemoverAsync(int registroId, CancellationToken ct = default) =>
        banco.EscreverAsync(c =>
        {
            if (c.Delete<RegistroDiarioLinha>(registroId) == 0)
                throw new RegraDeNegocioException("registro.inexistente", "Registro não encontrado.");
        }, ct);
}
