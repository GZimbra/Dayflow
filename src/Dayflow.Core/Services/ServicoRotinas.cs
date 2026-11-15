using Dayflow.Core.Data;
using Dayflow.Core.Data.Linhas;
using Dayflow.Core.Domain;
using Microsoft.Extensions.Logging;
using SQLite;

namespace Dayflow.Core.Services;

public sealed record DadosRotina(string Titulo, string? Descricao, int? ProjetoId, DiasSemana Dias, DateOnly DataInicio);

public sealed class ServicoRotinas(BancoDayflow banco, IRelogioCivil relogio, ILogger<ServicoRotinas> log)
{
    public Task<IReadOnlyList<Rotina>> ListarAsync(bool incluirInativas = true, CancellationToken ct = default) =>
        banco.LerAsync<IReadOnlyList<Rotina>>(c => c.Query<RotinaLinha>(incluirInativas
                ? "SELECT * FROM Rotina ORDER BY Ativa DESC, Titulo COLLATE NOCASE"
                : "SELECT * FROM Rotina WHERE Ativa = 1 ORDER BY Titulo COLLATE NOCASE")
            .Select(l => l.ParaDominio()).ToList(), ct);

    /// <summary>Rotinas que ocorrerão num dia futuro (prévia, nada é gravado).</summary>
    public Task<IReadOnlyList<Rotina>> PrevistasAsync(DateOnly dia, CancellationToken ct = default) =>
        banco.LerAsync<IReadOnlyList<Rotina>>(c => dia <= relogio.Hoje
            ? []
            : AtivasNoDia(c, dia).Select(l => l.ParaDominio()).ToList(), ct);

    public Task<int> CriarAsync(DadosRotina d, CancellationToken ct = default) =>
        banco.EscreverAsync(c =>
        {
            ArgumentNullException.ThrowIfNull(d);
            var r = Rotina.Nova(d.Titulo, d.Descricao, d.ProjetoId, d.Dias, d.DataInicio, relogio.AgoraUtc);
            var l = r.ParaLinha();
            c.Insert(l);
            log.LogInformation("Rotina {Id} criada", l.Id);
            return l.Id;
        }, ct);

    /// <summary>Edita o modelo e propaga título/projeto para instâncias ABERTAS de hoje em diante.</summary>
    public Task EditarAsync(int id, DadosRotina d, CancellationToken ct = default) =>
        banco.EscreverAsync(c =>
        {
            ArgumentNullException.ThrowIfNull(d);
            var r = Obter(c, id);
            r.Editar(d.Titulo, d.Descricao, d.ProjetoId, d.Dias, d.DataInicio, relogio.AgoraUtc);
            c.Update(r.ParaLinha());
            c.Execute("""
                UPDATE Tarefa SET Titulo = ?1, Descricao = ?2, ProjetoId = ?3, AtualizadoEm = ?4
                WHERE RotinaId = ?5 AND Status IN (0, 1) AND DataInicio >= ?6
                """, r.Titulo, r.Descricao, r.ProjetoId, Mapeamento.Utc(relogio.AgoraUtc), id, Mapeamento.Dia(relogio.Hoje));
        }, ct);

    public Task DefinirAtivaAsync(int id, bool ativa, CancellationToken ct = default) =>
        banco.EscreverAsync(c =>
        {
            var r = Obter(c, id);
            r.DefinirAtiva(ativa, relogio.AgoraUtc);
            c.Update(r.ParaLinha());
            if (!ativa)
            {
                // Desativar remove a instância de hoje se ainda não foi tocada (sem tempo, pendente).
                c.Execute("""
                    DELETE FROM Tarefa WHERE RotinaId = ?1 AND DataInicio = ?2 AND Status = 0
                      AND NOT EXISTS (SELECT 1 FROM RegistroDiario r WHERE r.TarefaId = Tarefa.Id)
                    """, id, Mapeamento.Dia(relogio.Hoje));
            }
        }, ct);

    /// <summary>Exclusão física só sem instâncias. Com histórico, desative.</summary>
    public Task ExcluirAsync(int id, CancellationToken ct = default) =>
        banco.EscreverAsync(c =>
        {
            Obter(c, id);
            if (c.ExecuteScalar<int>("SELECT COUNT(*) FROM Tarefa WHERE RotinaId = ?", id) > 0)
                throw new RegraDeNegocioException("rotina.com_historico", "Rotina já tem histórico. Desative em vez de excluir.");
            c.Delete<RotinaLinha>(id);
        }, ct);

    /// <summary>
    /// Cria as instâncias do dia (idempotente via índice UNIQUE + INSERT OR IGNORE).
    /// Dias futuros não são materializados.
    /// </summary>
    internal static int Materializar(SQLiteConnection c, DateOnly dia, IRelogioCivil relogio)
    {
        if (dia > relogio.Hoje) return 0;
        var criadas = 0;
        var agora = relogio.AgoraUtc;
        foreach (var l in AtivasNoDia(c, dia))
        {
            var t = Tarefa.DeRotina(l.ParaDominio(), dia, agora).ParaLinha();
            criadas += c.Execute("""
                INSERT OR IGNORE INTO Tarefa
                    (ProjetoId, RotinaId, Titulo, Descricao, DataInicio, DataFimPrevista, Status, DataConclusao, CriadoEm, AtualizadoEm)
                VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, NULL, ?8, ?8)
                """, t.ProjetoId, t.RotinaId, t.Titulo, t.Descricao, t.DataInicio, t.DataFimPrevista, t.Status, t.CriadoEm);
        }
        return criadas;
    }

    private static List<RotinaLinha> AtivasNoDia(SQLiteConnection c, DateOnly dia) =>
        c.Query<RotinaLinha>(
            "SELECT * FROM Rotina WHERE Ativa = 1 AND DataInicio <= ?1 AND (DiasSemana & ?2) <> 0 ORDER BY Id",
            Mapeamento.Dia(dia), (int)dia.Bit());

    private static Rotina Obter(SQLiteConnection c, int id) =>
        (c.Find<RotinaLinha>(id) ?? throw new RegraDeNegocioException("rotina.inexistente", "Rotina não encontrada.")).ParaDominio();
}
