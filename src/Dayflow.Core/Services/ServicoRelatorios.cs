using Dayflow.Core.Data;
using Dayflow.Core.Domain;
using SQLite;

namespace Dayflow.Core.Services;

/// <summary>
/// Agregações 100% no SQLite (GROUP BY). C# só mapeia linhas -> records.
/// Séries diárias usam CTE recursivo para preencher dias sem dado com zero.
/// </summary>
public sealed class ServicoRelatorios(BancoDayflow banco)
{
    // ---------- SQL (internal para teste/EXPLAIN QUERY PLAN) ----------

    internal const string SqlHorasPorProjeto = """
        SELECT t.ProjetoId AS ProjetoId, p.Nome AS Nome, p.Cor AS Cor, SUM(r.TempoGastoMin) AS Minutos
        FROM RegistroDiario r
        JOIN Tarefa t ON t.Id = r.TarefaId
        LEFT JOIN Projeto p ON p.Id = t.ProjetoId
        WHERE r.Data BETWEEN ?1 AND ?2
        GROUP BY t.ProjetoId
        HAVING SUM(r.TempoGastoMin) > 0
        ORDER BY Minutos DESC, Nome
        """;

    internal const string SqlMinutosPorDia = $"""
        {ConsultasSql.CteDias}
        SELECT dias.d AS Dia, COALESCE(SUM(r.TempoGastoMin), 0) AS Valor
        FROM dias
        LEFT JOIN RegistroDiario r ON r.Data = dias.d
        GROUP BY dias.d
        ORDER BY dias.d
        """;

    internal const string SqlConcluidasPorDia = $"""
        {ConsultasSql.CteDias}
        SELECT dias.d AS Dia, COUNT(t.Id) AS Valor
        FROM dias
        LEFT JOIN Tarefa t ON t.DataConclusao = dias.d AND t.Status = 2
        GROUP BY dias.d
        ORDER BY dias.d
        """;

    // Tarefas com tempo em >= 2 dias distintos no período; top N por tempo total.
    internal const string SqlTempoTarefasMultiDia = """
        SELECT r.TarefaId AS TarefaId, t.Titulo AS Titulo, r.Data AS Dia, SUM(r.TempoGastoMin) AS Minutos
        FROM RegistroDiario r
        JOIN Tarefa t ON t.Id = r.TarefaId
        WHERE r.Data BETWEEN ?1 AND ?2
          AND r.TarefaId IN (
              SELECT TarefaId FROM RegistroDiario
              WHERE Data BETWEEN ?1 AND ?2 AND TempoGastoMin > 0
              GROUP BY TarefaId
              HAVING COUNT(DISTINCT Data) >= 2
              ORDER BY SUM(TempoGastoMin) DESC
              LIMIT ?3)
          AND r.TempoGastoMin > 0
        GROUP BY r.TarefaId, r.Data
        ORDER BY r.TarefaId, r.Data
        """;

    internal const string SqlTaxaConclusao = $"""
        SELECT t.ProjetoId AS ProjetoId, p.Nome AS Nome, p.Cor AS Cor,
               SUM(CASE WHEN t.Status = 2 AND t.DataConclusao BETWEEN ?1 AND ?2 THEN 1 ELSE 0 END) AS Concluidas,
               SUM(CASE WHEN t.Status = 3 AND t.DataConclusao BETWEEN ?1 AND ?2 THEN 1 ELSE 0 END) AS Canceladas,
               SUM(CASE WHEN t.Status IN (0, 1) OR t.DataConclusao > ?2 THEN 1 ELSE 0 END) AS Abertas
        FROM Tarefa t
        LEFT JOIN Projeto p ON p.Id = t.ProjetoId
        WHERE {ConsultasSql.FiltroRelevanteNoPeriodo}
        GROUP BY t.ProjetoId
        ORDER BY Nome
        """;

    internal const string SqlCalendario = $"""
        {ConsultasSql.CteDias},
        -- Pré-filtra as candidatas do mês (evita o produto dias × todas as tarefas do histórico).
        cand AS MATERIALIZED (
            SELECT Id, DataInicio, Status, DataConclusao, RotinaId FROM Tarefa
            WHERE DataInicio <= ?2 AND (Status IN (0, 1) OR DataConclusao > ?1)
              AND (RotinaId IS NULL OR DataInicio >= ?1))
        SELECT dias.d AS Dia, COUNT(t.Id) AS Valor
        FROM dias
        LEFT JOIN cand t ON t.DataInicio <= dias.d AND (t.Status IN (0, 1) OR t.DataConclusao > dias.d)
                         AND (t.RotinaId IS NULL OR t.DataInicio = dias.d)
        GROUP BY dias.d
        ORDER BY dias.d
        """;

    internal const string SqlTarefasDoPeriodo = $"""
        SELECT t.Id AS TarefaId, t.Titulo AS Titulo, p.Nome AS Projeto, t.Status AS Status, t.DataConclusao AS DataConclusao,
               CASE
                   WHEN t.Status = 2 AND t.DataConclusao <= ?2 THEN 2
                   WHEN t.Status = 3 AND t.DataConclusao <= ?2 THEN 3
                   ELSE 1
               END AS Situacao,
               COALESCE(SUM(r.TempoGastoMin), 0) AS Minutos
        FROM Tarefa t
        LEFT JOIN Projeto p ON p.Id = t.ProjetoId
        LEFT JOIN RegistroDiario r ON r.TarefaId = t.Id AND r.Data BETWEEN ?1 AND ?2
        WHERE {ConsultasSql.FiltroRelevanteNoPeriodo}
        GROUP BY t.Id
        ORDER BY Situacao DESC, Minutos DESC, t.Id
        """;

    internal const string SqlMinutosTotais =
        "SELECT COALESCE(SUM(TempoGastoMin), 0) FROM RegistroDiario WHERE Data BETWEEN ?1 AND ?2";

    // ---------- API ----------

    public Task<Relatorio> RelatorioDiarioAsync(DateOnly dia, CancellationToken ct = default) =>
        RelatorioAsync(Periodo.DoDia(dia), ct);

    public Task<Relatorio> RelatorioAsync(Periodo p, CancellationToken ct = default) =>
        banco.LerAsync(c =>
        {
            ArgumentNullException.ThrowIfNull(p);
            var (i, f) = (Mapeamento.Dia(p.Inicio), Mapeamento.Dia(p.Fim));
            var total = c.ExecuteScalar<int>(SqlMinutosTotais, i, f);
            var tarefas = c.Query<LinhaTarefaPeriodo>(SqlTarefasDoPeriodo, i, f)
                .Select(l => new LinhaTarefaRelatorio(l.TarefaId, l.Titulo, l.Projeto ?? Rotulos.SemProjeto,
                    (StatusTarefa)l.Status, (SituacaoNoPeriodo)l.Situacao, Mapeamento.Dia(l.DataConclusao), l.Minutos))
                .ToList();
            return new Relatorio(p, total, tarefas, HorasPorProjeto(c, i, f), MinutosPorDia(c, i, f));
        }, ct);

    public Task<IReadOnlyList<HorasProjeto>> HorasPorProjetoAsync(Periodo p, CancellationToken ct = default) =>
        banco.LerAsync<IReadOnlyList<HorasProjeto>>(c => HorasPorProjeto(c, Mapeamento.Dia(p.Inicio), Mapeamento.Dia(p.Fim)), ct);

    public Task<IReadOnlyList<ConcluidasDia>> ConcluidasPorDiaAsync(Periodo p, CancellationToken ct = default) =>
        banco.LerAsync<IReadOnlyList<ConcluidasDia>>(c =>
            SerieDiaria(c, SqlConcluidasPorDia, p).Select(x => new ConcluidasDia(Mapeamento.Dia(x.Dia), x.Valor)).ToList(), ct);

    public Task<IReadOnlyList<TempoTarefaDia>> TempoTarefasMultiDiaAsync(Periodo p, int maxTarefas = 10, CancellationToken ct = default) =>
        banco.LerAsync<IReadOnlyList<TempoTarefaDia>>(c =>
        {
            var (i, f) = (Mapeamento.Dia(p.Inicio), Mapeamento.Dia(p.Fim));
            return c.Query<LinhaTempoTarefaDia>(SqlTempoTarefasMultiDia, i, f, Math.Clamp(maxTarefas, 1, 50))
                .Select(l => new TempoTarefaDia(l.TarefaId, l.Titulo, Mapeamento.Dia(l.Dia), l.Minutos)).ToList();
        }, ct);

    public Task<IReadOnlyList<TaxaConclusaoProjeto>> TaxaConclusaoPorProjetoAsync(Periodo p, CancellationToken ct = default) =>
        banco.LerAsync<IReadOnlyList<TaxaConclusaoProjeto>>(c =>
        {
            var (i, f) = (Mapeamento.Dia(p.Inicio), Mapeamento.Dia(p.Fim));
            return c.Query<LinhaTaxa>(SqlTaxaConclusao, i, f)
                .Select(l => new TaxaConclusaoProjeto(l.ProjetoId, l.Nome ?? Rotulos.SemProjeto, l.Cor ?? Rotulos.CorSemProjeto,
                    l.Concluidas, l.Canceladas, l.Abertas)).ToList();
        }, ct);

    /// <summary>Pendentes ao fim de cada dia do mês (marcação do calendário).</summary>
    public Task<IReadOnlyList<DiaCalendario>> CalendarioAsync(int ano, int mes, CancellationToken ct = default) =>
        banco.LerAsync<IReadOnlyList<DiaCalendario>>(c =>
            SerieDiaria(c, SqlCalendario, Periodo.Mes(ano, mes))
                .Select(x => new DiaCalendario(Mapeamento.Dia(x.Dia), x.Valor)).ToList(), ct);

    // ---------- internos ----------

    private static List<HorasProjeto> HorasPorProjeto(SQLiteConnection c, int i, int f) =>
        c.Query<LinhaHorasProjeto>(SqlHorasPorProjeto, i, f)
            .Select(l => new HorasProjeto(l.ProjetoId, l.Nome ?? Rotulos.SemProjeto, l.Cor ?? Rotulos.CorSemProjeto, l.Minutos))
            .ToList();

    private static List<MinutosDia> MinutosPorDia(SQLiteConnection c, int i, int f) =>
        c.Query<LinhaSerie>(SqlMinutosPorDia, i, f).Select(x => new MinutosDia(Mapeamento.Dia(x.Dia), x.Valor)).ToList();

    private static List<LinhaSerie> SerieDiaria(SQLiteConnection c, string sql, Periodo p) =>
        c.Query<LinhaSerie>(sql, Mapeamento.Dia(p.Inicio), Mapeamento.Dia(p.Fim));

    private sealed class LinhaSerie { public int Dia { get; set; } public int Valor { get; set; } }

    private sealed class LinhaHorasProjeto
    {
        public int? ProjetoId { get; set; }
        public string? Nome { get; set; }
        public string? Cor { get; set; }
        public int Minutos { get; set; }
    }

    private sealed class LinhaTempoTarefaDia
    {
        public int TarefaId { get; set; }
        public string Titulo { get; set; } = "";
        public int Dia { get; set; }
        public int Minutos { get; set; }
    }

    private sealed class LinhaTaxa
    {
        public int? ProjetoId { get; set; }
        public string? Nome { get; set; }
        public string? Cor { get; set; }
        public int Concluidas { get; set; }
        public int Canceladas { get; set; }
        public int Abertas { get; set; }
    }

    private sealed class LinhaTarefaPeriodo
    {
        public int TarefaId { get; set; }
        public string Titulo { get; set; } = "";
        public string? Projeto { get; set; }
        public int Status { get; set; }
        public int? DataConclusao { get; set; }
        public int Situacao { get; set; }
        public int Minutos { get; set; }
    }
}
