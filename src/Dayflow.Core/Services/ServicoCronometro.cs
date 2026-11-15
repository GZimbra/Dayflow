using Dayflow.Core.Data;
using Dayflow.Core.Data.Linhas;
using Dayflow.Core.Domain;
using Microsoft.Extensions.Logging;

namespace Dayflow.Core.Services;

/// <summary>
/// Um cronômetro global. Estado persistido em CronometroAtivo: sobrevive a kill do processo/reboot.
/// O tempo é calculado por diferença de timestamps UTC, não por tick em memória.
/// </summary>
public sealed class ServicoCronometro(BancoDayflow banco, IRelogioCivil relogio, ILogger<ServicoCronometro> log)
{
    public Task<EstadoCronometro?> EstadoAsync(CancellationToken ct = default) =>
        banco.LerAsync(c =>
        {
            var a = c.Find<CronometroAtivoLinha>(1);
            if (a is null) return null;
            var ini = Mapeamento.Utc(a.InicioUtc);
            var dec = relogio.AgoraUtc - ini;
            return new EstadoCronometro(a.TarefaId, ini, dec < TimeSpan.Zero ? TimeSpan.Zero : dec);
        }, ct);

    /// <summary>Inicia na tarefa. Se outra estiver rodando, pausa (gravando) antes.</summary>
    public Task<IReadOnlyList<RegistroGerado>> IniciarAsync(int tarefaId, CancellationToken ct = default) =>
        banco.EscreverAsync<IReadOnlyList<RegistroGerado>>(c =>
        {
            var ativo = c.Find<CronometroAtivoLinha>(1);
            if (ativo?.TarefaId == tarefaId) return [];
            var gerados = NucleoEscrita.PausarCronometro(c, relogio, log);

            var t = NucleoEscrita.ObterTarefa(c, tarefaId).ParaDominio();
            t.Iniciar(relogio.Hoje, relogio.AgoraUtc);
            NucleoEscrita.Salvar(c, t);
            c.Insert(new CronometroAtivoLinha { Id = 1, TarefaId = tarefaId, InicioUtc = Mapeamento.Utc(relogio.AgoraUtc) });
            log.LogInformation("Cronômetro iniciado na tarefa {Id}", tarefaId);
            return gerados;
        }, ct);

    public Task<IReadOnlyList<RegistroGerado>> PausarAsync(CancellationToken ct = default) =>
        banco.EscreverAsync(c => NucleoEscrita.PausarCronometro(c, relogio, log), ct);
}
