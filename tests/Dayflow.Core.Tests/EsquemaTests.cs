using Dayflow.Core.Domain;
using Dayflow.Core.Services;
using SQLite;
using static Dayflow.Core.Tests.Ambiente;

namespace Dayflow.Core.Tests;

public class EsquemaTests
{
    [Fact]
    public async Task Migracao_cria_indices_exigidos_e_versiona()
    {
        await using var a = new Ambiente();
        var idx = await a.Banco.LerAsync(c => c.QueryScalars<string>("SELECT name FROM sqlite_master WHERE type = 'index'"));
        Assert.Contains("IX_Tarefa_ProjetoId", idx);
        Assert.Contains("IX_Tarefa_DataInicio", idx);
        Assert.Contains("UX_RegistroDiario_Data_TarefaId", idx);
        Assert.Equal(3, await a.Banco.LerAsync(c => c.ExecuteScalar<int>("PRAGMA user_version")));
        a.Reabrir(); // idempotente
        Assert.Equal(3, await a.Banco.LerAsync(c => c.ExecuteScalar<int>("PRAGMA user_version")));
    }

    [Fact]
    public async Task CHECK_impede_status_final_sem_data_de_conclusao()
    {
        await using var a = new Ambiente();
        var id = await a.NovaTarefa("A", D(2026, 3, 2));
        await Assert.ThrowsAsync<SQLiteException>(() =>
            a.Banco.EscreverAsync(c => c.Execute("UPDATE Tarefa SET Status = 2 WHERE Id = ?", id)));
    }

    [Fact]
    public async Task Escrita_que_falha_faz_rollback_completo()
    {
        await using var a = new Ambiente();
        await Assert.ThrowsAsync<SQLiteException>(() => a.Banco.EscreverAsync(c =>
        {
            c.Execute("INSERT INTO Projeto (Nome, Cor, Ativo) VALUES ('ok', '#000000', 1)");
            c.Execute("INSERT INTO Projeto (Nome, Cor, Ativo) VALUES ('ruim', 'azul', 1)");
        }));
        Assert.Equal(0, await a.Banco.LerAsync(c => c.ExecuteScalar<int>("SELECT COUNT(*) FROM Projeto")));
    }

    [Fact]
    public async Task Excluir_tarefa_remove_registros_em_cascata_e_projeto_com_tarefa_nao_exclui()
    {
        await using var a = new Ambiente();
        a.IrPara(2026, 3, 2);
        var p = await a.Projetos.CriarAsync("P", "#123456");
        var id = await a.NovaTarefa("A", D(2026, 3, 2), p);
        await a.Registros.LancarManualAsync(id, D(2026, 3, 2), 10, null);
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => a.Projetos.ExcluirAsync(p));
        await a.Tarefas.ExcluirAsync(id);
        Assert.Equal(0, await a.Banco.LerAsync(c => c.ExecuteScalar<int>("SELECT COUNT(*) FROM RegistroDiario")));
        await a.Projetos.ExcluirAsync(p);
    }

    [Fact]
    public async Task Projeto_arquivado_nao_recebe_tarefa_nova_e_nome_e_unico()
    {
        await using var a = new Ambiente();
        var p = await a.Projetos.CriarAsync("Redes", "#123456");
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => a.Projetos.CriarAsync("redes", "#654321"));
        await a.Projetos.DefinirArquivadoAsync(p, true);
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => a.NovaTarefa("A", D(2026, 3, 2), p));
        Assert.Empty(await a.Projetos.ListarAsync());
        Assert.Single(await a.Projetos.ListarAsync(incluirArquivados: true));
    }
}
