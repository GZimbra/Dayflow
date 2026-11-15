using Dayflow.Core.Domain;
using Dayflow.Core.Services;
using static Dayflow.Core.Tests.Ambiente;

namespace Dayflow.Core.Tests;

public class RotinasTests
{
    // 2026-03-02 é segunda-feira.
    private static Task<int> Rotina(Ambiente a, string titulo = "Checklist de doca", DiasSemana dias = DiasSemana.Uteis, DateOnly? inicio = null) =>
        a.Rotinas.CriarAsync(new DadosRotina(titulo, null, null, dias, inicio ?? D(2026, 3, 2)));

    [Fact]
    public async Task Rotina_gera_uma_instancia_por_dia_e_nao_duplica()
    {
        await using var a = new Ambiente();
        a.IrPara(2026, 3, 4, 9);
        var r = await Rotina(a);
        for (var i = 0; i < 3; i++) await a.Tarefas.DoDiaAsync(D(2026, 3, 4));
        var itens = await a.Tarefas.DoDiaAsync(D(2026, 3, 4));
        var inst = Assert.Single(itens);
        Assert.True(inst.Tarefa.EhRotina);
        Assert.Equal(r, inst.Tarefa.RotinaId);
        Assert.Equal(1, await a.Banco.LerAsync(c => c.ExecuteScalar<int>("SELECT COUNT(*) FROM Tarefa")));
    }

    [Fact]
    public async Task Instancia_nao_feita_nao_passa_para_o_dia_seguinte()
    {
        await using var a = new Ambiente();
        a.IrPara(2026, 3, 2, 9);
        await Rotina(a);
        await a.Tarefas.DoDiaAsync(D(2026, 3, 2));      // segunda: gerou, não fez
        a.IrPara(2026, 3, 3, 9);
        var terca = await a.Tarefas.DoDiaAsync(D(2026, 3, 3));
        var inst = Assert.Single(terca);                // só a de terça
        Assert.Equal(D(2026, 3, 3), inst.Tarefa.DataInicio);

        var cal = await a.Relatorios.CalendarioAsync(2026, 3);
        Assert.Equal(1, cal.Single(c => c.Dia == D(2026, 3, 2)).Pendentes);   // segunda ficou pendente
        Assert.Equal(1, cal.Single(c => c.Dia == D(2026, 3, 3)).Pendentes);   // não soma a de segunda
    }

    [Fact]
    public async Task Respeita_dias_da_semana_e_data_de_inicio()
    {
        await using var a = new Ambiente();
        a.IrPara(2026, 3, 9, 9);
        await Rotina(a, dias: DiasSemana.Segunda | DiasSemana.Quarta, inicio: D(2026, 3, 4));
        Assert.Empty(await a.Tarefas.DoDiaAsync(D(2026, 3, 2)));   // segunda, antes do início
        Assert.Single(await a.Tarefas.DoDiaAsync(D(2026, 3, 4)));  // quarta
        Assert.Empty(await a.Tarefas.DoDiaAsync(D(2026, 3, 5)));   // quinta
        Assert.Empty(await a.Tarefas.DoDiaAsync(D(2026, 3, 7)));   // sábado
        Assert.Single(await a.Tarefas.DoDiaAsync(D(2026, 3, 9)));  // segunda
    }

    [Fact]
    public async Task Dia_futuro_mostra_previa_sem_gravar()
    {
        await using var a = new Ambiente();
        a.IrPara(2026, 3, 2, 9);
        await Rotina(a);
        Assert.Empty(await a.Tarefas.DoDiaAsync(D(2026, 3, 5)));
        Assert.Single(await a.Rotinas.PrevistasAsync(D(2026, 3, 5)));
        Assert.Equal(0, await a.Banco.LerAsync(c => c.ExecuteScalar<int>("SELECT COUNT(*) FROM Tarefa")));
    }

    [Fact]
    public async Task Instancia_aceita_cronometro_e_entra_nos_relatorios_do_proprio_dia()
    {
        await using var a = new Ambiente();
        a.IrPara(2026, 3, 2, 9);
        await Rotina(a);
        var id = (await a.Tarefas.DoDiaAsync(D(2026, 3, 2))).Single().Tarefa.Id;
        await a.Cronometro.IniciarAsync(id);
        a.IrPara(2026, 3, 2, 9, 25);
        await a.Tarefas.ConcluirAsync(id);

        var r = await a.Relatorios.RelatorioDiarioAsync(D(2026, 3, 2));
        Assert.Equal(25, r.MinutosTotais);
        Assert.Single(r.Concluidas);

        // Instância aberta antiga não "vaza" como aberta em períodos posteriores
        a.IrPara(2026, 3, 3, 9);
        await a.Tarefas.DoDiaAsync(D(2026, 3, 3));     // gera e não faz
        a.IrPara(2026, 3, 10, 9);
        var semanaSeguinte = await a.Relatorios.RelatorioAsync(new Periodo(D(2026, 3, 9), D(2026, 3, 13)));
        Assert.Empty(semanaSeguinte.Tarefas);
    }

    [Fact]
    public async Task Instancia_de_rotina_nao_e_excluida_nem_muda_de_dia()
    {
        await using var a = new Ambiente();
        a.IrPara(2026, 3, 2, 9);
        await Rotina(a);
        var t = (await a.Tarefas.DoDiaAsync(D(2026, 3, 2))).Single().Tarefa;
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => a.Tarefas.ExcluirAsync(t.Id));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() =>
            a.Tarefas.EditarAsync(t.Id, new(t.Titulo, null, null, D(2026, 3, 3), null)));
    }

    [Fact]
    public async Task Desativar_remove_instancia_intocada_de_hoje_e_excluir_com_historico_falha()
    {
        await using var a = new Ambiente();
        a.IrPara(2026, 3, 2, 9);
        var r = await Rotina(a);
        await a.Tarefas.DoDiaAsync(D(2026, 3, 2));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => a.Rotinas.ExcluirAsync(r));
        await a.Rotinas.DefinirAtivaAsync(r, false);
        Assert.Empty(await a.Tarefas.DoDiaAsync(D(2026, 3, 2)));
        await a.Rotinas.ExcluirAsync(r);   // sem instâncias agora
    }

    [Fact]
    public async Task Editar_rotina_propaga_titulo_para_instancia_aberta_de_hoje_mas_nao_passado()
    {
        await using var a = new Ambiente();
        a.IrPara(2026, 3, 2, 9);
        var r = await Rotina(a, "Antigo");
        await a.Tarefas.DoDiaAsync(D(2026, 3, 2));
        a.IrPara(2026, 3, 3, 9);
        await a.Tarefas.DoDiaAsync(D(2026, 3, 3));
        await a.Rotinas.EditarAsync(r, new DadosRotina("Novo", null, null, DiasSemana.Uteis, D(2026, 3, 2)));
        Assert.Equal("Antigo", (await a.Tarefas.DoDiaAsync(D(2026, 3, 2))).Single().Tarefa.Titulo);
        Assert.Equal("Novo", (await a.Tarefas.DoDiaAsync(D(2026, 3, 3))).Single().Tarefa.Titulo);
    }
}
