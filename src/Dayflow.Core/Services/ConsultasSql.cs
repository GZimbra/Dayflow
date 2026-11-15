namespace Dayflow.Core.Services;

/// <summary>
/// SQL compartilhado. Datas são DayNumber (INTEGER).
/// Parâmetros NUMERADOS (?1, ?2): o sqlite-net faz bind posicional 1..N, e o SQLite permite reutilizar ?N.
/// Regras de carry-over espelham Tarefa.VisivelEm / Tarefa.PendenteAoFimDe (teste de equivalência cobre).
/// </summary>
internal static class ConsultasSql
{
    /// <summary>?1 = dia</summary>
    public const string FiltroVisivelNoDia =
        "t.DataInicio <= ?1 AND (t.Status IN (0, 1) OR t.DataConclusao >= ?1) AND (t.RotinaId IS NULL OR t.DataInicio = ?1)";

    /// <summary>Tarefas relevantes em [?1, ?2]. Instância de rotina só no próprio dia.</summary>
    public const string FiltroRelevanteNoPeriodo =
        "t.DataInicio <= ?2 AND (t.Status IN (0, 1) OR t.DataConclusao >= ?1) AND (t.RotinaId IS NULL OR t.DataInicio >= ?1)";

    /// <summary>Série contínua de dias [?1, ?2].</summary>
    public const string CteDias = "WITH RECURSIVE dias(d) AS (SELECT ?1 UNION ALL SELECT d + 1 FROM dias WHERE d < ?2)";

    /// <summary>?1 = dia</summary>
    public const string TarefasDoDia = $"""
        SELECT t.Id, t.ProjetoId, t.RotinaId, t.Titulo, t.Descricao, t.DataInicio, t.DataFimPrevista, t.Status,
               t.DataConclusao, t.CriadoEm, t.AtualizadoEm,
               p.Nome AS ProjetoNome, p.Cor AS ProjetoCor,
               COALESCE(r.TempoGastoMin, 0) AS MinutosNoDia,
               CASE WHEN c.TarefaId IS NULL THEN 0 ELSE 1 END AS Rodando,
               h.InicioMin AS InicioMin, h.DuracaoMin AS DuracaoMin
        FROM Tarefa t
        LEFT JOIN Projeto p ON p.Id = t.ProjetoId
        LEFT JOIN RegistroDiario r ON r.TarefaId = t.Id AND r.Data = ?1
        LEFT JOIN OrdemDia od ON od.TarefaId = t.Id AND od.Data = ?1
        LEFT JOIN CronometroAtivo c ON c.TarefaId = t.Id
        LEFT JOIN HorarioDia h ON h.TarefaId = t.Id AND h.Data = ?1
        WHERE {FiltroVisivelNoDia}
        ORDER BY
            CASE WHEN od.Posicao IS NULL THEN 1 ELSE 0 END,
            od.Posicao,
            -- sem ordem no dia: herda a posição do último dia ordenado antes dele
            COALESCE((SELECT o2.Posicao FROM OrdemDia o2
                      WHERE o2.TarefaId = t.Id AND o2.Data < ?1
                      ORDER BY o2.Data DESC LIMIT 1), 2147483647),
            -- rotinas sem posição vêm depois das avulsas
            CASE WHEN t.RotinaId IS NULL THEN 0 ELSE 1 END,
            t.CriadoEm, t.Id
        """;
}
