using Dayflow.Core.Data;
using Dayflow.Core.Data.Linhas;
using Dayflow.Core.Domain;
using Microsoft.Extensions.Logging;
using SQLite;

namespace Dayflow.Core.Services;

/// <summary>Operações síncronas usadas DENTRO de transações, compartilhadas entre serviços.</summary>
internal static class NucleoEscrita
{
    public static TarefaLinha ObterTarefa(SQLiteConnection c, int id) =>
        c.Find<TarefaLinha>(id) ?? throw new RegraDeNegocioException("tarefa.inexistente", "Tarefa não encontrada.");

    public static void Salvar(SQLiteConnection c, Tarefa t)
    {
        var linha = t.ParaLinha();
        if (c.Update(linha) != 1) throw new InvalidOperationException("Update de tarefa não afetou linha.");
    }

    public static (DateOnly? Primeiro, DateOnly? Ultimo) LimitesRegistro(SQLiteConnection c, int tarefaId)
    {
        var l = c.Query<MinMax>(
            "SELECT MIN(Data) AS Min, MAX(Data) AS Max FROM RegistroDiario WHERE TarefaId = ? AND (TempoGastoMin > 0 OR SegundosResiduais > 0)",
            tarefaId).Single();
        return (Mapeamento.Dia(l.Min), Mapeamento.Dia(l.Max));
    }

    /// <summary>Soma segundos ao registro (TarefaId, Dia), carregando o resto em SegundosResiduais.</summary>
    public static void SomarSegundos(SQLiteConnection c, int tarefaId, DateOnly dia, long segundos, string? observacao)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(segundos);
        var atual = c.Query<RegistroDiarioLinha>(
            "SELECT * FROM RegistroDiario WHERE Data = ? AND TarefaId = ?", Mapeamento.Dia(dia), tarefaId).SingleOrDefault();
        long totalSeg = (atual is null ? 0 : atual.TempoGastoMin * 60L + atual.SegundosResiduais) + segundos;
        if (totalSeg > RegistroDiario.MinutosMaxPorDia * 60L)
            throw new RegraDeNegocioException("registro.excede_dia", "Tempo no dia excede 24 h para esta tarefa.");

        var obs = JuntarObservacao(atual?.Observacao, observacao);
        // Upsert explícito no índice UNIQUE (Data, TarefaId).
        c.Execute("""
            INSERT INTO RegistroDiario (TarefaId, Data, TempoGastoMin, SegundosResiduais, Observacao)
            VALUES (?, ?, ?, ?, ?)
            ON CONFLICT (Data, TarefaId) DO UPDATE SET
                TempoGastoMin = excluded.TempoGastoMin,
                SegundosResiduais = excluded.SegundosResiduais,
                Observacao = excluded.Observacao
            """, tarefaId, Mapeamento.Dia(dia), (int)(totalSeg / 60), (int)(totalSeg % 60), obs);
    }

    internal static string? JuntarObservacao(string? atual, string? nova)
    {
        var n = string.IsNullOrWhiteSpace(nova) ? null : nova.Trim();
        var r = (atual, n) switch
        {
            (_, null) => atual,
            (null, _) => n,
            _ => $"{atual} | {n}",
        };
        if (r is { Length: > RegistroDiario.ObservacaoMax })
            throw new RegraDeNegocioException("registro.observacao", $"Observação excede {RegistroDiario.ObservacaoMax} caracteres.");
        return r;
    }

    /// <summary>
    /// Para o cronômetro ativo (se houver) e grava o tempo fatiado por dia civil.
    /// Relógio voltando no tempo (fuso/ajuste manual) descarta a sessão em vez de gravar negativo.
    /// </summary>
    public static IReadOnlyList<RegistroGerado> PausarCronometro(SQLiteConnection c, IRelogioCivil relogio, ILogger log)
    {
        var ativo = c.Find<CronometroAtivoLinha>(1);
        if (ativo is null) return [];
        var inicio = Mapeamento.Utc(ativo.InicioUtc);
        var fim = relogio.AgoraUtc;
        c.Delete<CronometroAtivoLinha>(1);

        if (fim <= inicio)
        {
            log.LogWarning("Cronômetro descartado: relógio retrocedeu ({Segundos}s)", (inicio - fim).TotalSeconds);
            return [];
        }

        var gerados = new List<RegistroGerado>();
        foreach (var fatia in DivisorDeTempo.PorDia(inicio, fim, relogio))
        {
            var seg = (long)Math.Floor(fatia.Duracao.TotalSeconds);
            if (seg <= 0) continue;
            // Sessão esquecida ligada: limita ao que cabe no dia em vez de falhar a pausa.
            var existente = c.ExecuteScalar<long>(
                "SELECT COALESCE(SUM(TempoGastoMin * 60 + SegundosResiduais), 0) FROM RegistroDiario WHERE Data = ? AND TarefaId = ?",
                Mapeamento.Dia(fatia.Dia), ativo.TarefaId);
            var cabe = Math.Min(seg, RegistroDiario.MinutosMaxPorDia * 60L - existente);
            if (cabe < seg) log.LogWarning("Cronômetro truncado em {Segundos}s no limite diário", seg - cabe);
            if (cabe <= 0) continue;
            SomarSegundos(c, ativo.TarefaId, fatia.Dia, cabe, null);
            gerados.Add(new RegistroGerado(ativo.TarefaId, fatia.Dia, (int)cabe));
        }
        log.LogInformation("Cronômetro pausado: {Fatias} registro(s)", gerados.Count);
        return gerados;
    }

    private sealed class MinMax
    {
        public int? Min { get; set; }
        public int? Max { get; set; }
    }
}
