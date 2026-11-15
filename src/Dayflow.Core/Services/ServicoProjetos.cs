using Dayflow.Core.Data;
using Dayflow.Core.Data.Linhas;
using Dayflow.Core.Domain;
using Microsoft.Extensions.Logging;
using SQLite;

namespace Dayflow.Core.Services;

public sealed class ServicoProjetos(BancoDayflow banco, ILogger<ServicoProjetos> log)
{
    public Task<IReadOnlyList<Projeto>> ListarAsync(bool incluirArquivados = false, CancellationToken ct = default) =>
        banco.LerAsync<IReadOnlyList<Projeto>>(c => c.Query<ProjetoLinha>(
                incluirArquivados
                    ? "SELECT * FROM Projeto ORDER BY Ativo DESC, Nome COLLATE NOCASE"
                    : "SELECT * FROM Projeto WHERE Ativo = 1 ORDER BY Nome COLLATE NOCASE")
            .Select(l => l.ParaDominio()).ToList(), ct);

    public Task<int> CriarAsync(string nome, string cor, CancellationToken ct = default) =>
        banco.EscreverAsync(c =>
        {
            var p = new Projeto(0, nome, cor, true);
            GarantirNomeUnico(c, p.Nome, null);
            var linha = p.ParaLinha();
            c.Insert(linha);
            log.LogInformation("Projeto {Id} criado", linha.Id);
            return linha.Id;
        }, ct);

    public Task EditarAsync(int id, string nome, string cor, CancellationToken ct = default) =>
        banco.EscreverAsync(c =>
        {
            var p = Obter(c, id);
            p.Renomear(nome);
            p.DefinirCor(cor);
            GarantirNomeUnico(c, p.Nome, id);
            c.Update(p.ParaLinha());
        }, ct);

    public Task DefinirArquivadoAsync(int id, bool arquivado, CancellationToken ct = default) =>
        banco.EscreverAsync(c =>
        {
            var p = Obter(c, id);
            if (arquivado) p.Arquivar(); else p.Desarquivar();
            c.Update(p.ParaLinha());
            log.LogInformation("Projeto {Id} arquivado={Arquivado}", id, arquivado);
        }, ct);

    /// <summary>Exclusão física só sem tarefas vinculadas. Com histórico, arquive.</summary>
    public Task ExcluirAsync(int id, CancellationToken ct = default) =>
        banco.EscreverAsync(c =>
        {
            Obter(c, id);
            if (c.ExecuteScalar<int>("SELECT COUNT(*) FROM Tarefa WHERE ProjetoId = ?", id) > 0)
                throw new RegraDeNegocioException("projeto.com_tarefas", "Projeto possui tarefas. Arquive em vez de excluir.");
            c.Delete<ProjetoLinha>(id);
        }, ct);

    private static Projeto Obter(SQLiteConnection c, int id) =>
        (c.Find<ProjetoLinha>(id) ?? throw new RegraDeNegocioException("projeto.inexistente", "Projeto não encontrado.")).ParaDominio();

    private static void GarantirNomeUnico(SQLiteConnection c, string nome, int? ignorarId)
    {
        var existe = c.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM Projeto WHERE Nome = ? COLLATE NOCASE AND Id <> ?", nome, ignorarId ?? 0);
        if (existe > 0) throw new RegraDeNegocioException("projeto.nome_duplicado", "Já existe projeto com esse nome.");
    }
}
