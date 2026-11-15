using Dayflow.Core.Domain;
using static Dayflow.Core.Tests.Ambiente;

namespace Dayflow.Core.Tests;

public class TarefasMultiDiaTests
{
    [Fact]
    public async Task Tarefa_aberta_aparece_do_inicio_em_diante_e_nao_antes()
    {
        await using var a = new Ambiente();
        a.IrPara(2026, 3, 2);
        var id = await a.NovaTarefa("Inventário", D(2026, 3, 2));

        Assert.DoesNotContain(id, await a.IdsDoDia(D(2026, 3, 1)));
        foreach (var d in Enumerable.Range(2, 20))
            Assert.Contains(id, await a.IdsDoDia(D(2026, 3, d)));
    }

    [Fact]
    public async Task Concluida_aparece_ate_o_dia_da_conclusao_inclusive()
    {
        await using var a = new Ambiente();
        a.IrPara(2026, 3, 2);
        var id = await a.NovaTarefa("Migração WMS", D(2026, 3, 2));
        a.IrPara(2026, 3, 5, 17);
        await a.Tarefas.ConcluirAsync(id);

        Assert.Contains(id, await a.IdsDoDia(D(2026, 3, 2)));
        Assert.Contains(id, await a.IdsDoDia(D(2026, 3, 5)));
        Assert.DoesNotContain(id, await a.IdsDoDia(D(2026, 3, 6)));
        var t = await a.Tarefas.ObterAsync(id);
        Assert.Equal(D(2026, 3, 5), t.DataConclusao);
    }

    [Fact]
    public async Task Cancelada_some_depois_do_dia_do_cancelamento_e_reabrir_restaura()
    {
        await using var a = new Ambiente();
        a.IrPara(2026, 3, 2);
        var id = await a.NovaTarefa("X", D(2026, 3, 2));
        a.IrPara(2026, 3, 3);
        await a.Tarefas.CancelarAsync(id);
        Assert.DoesNotContain(id, await a.IdsDoDia(D(2026, 3, 4)));

        await a.Tarefas.ReabrirAsync(id);
        Assert.Contains(id, await a.IdsDoDia(D(2026, 3, 10)));
        Assert.Null((await a.Tarefas.ObterAsync(id)).DataConclusao);
    }

    [Fact]
    public async Task Trabalho_em_varios_dias_gera_um_registro_por_dia_sem_duplicar_tarefa()
    {
        await using var a = new Ambiente();
        a.IrPara(2026, 3, 2, 9);
        var id = await a.NovaTarefa("Integração MES", D(2026, 3, 2));

        for (var dia = 2; dia <= 4; dia++)
        {
            a.IrPara(2026, 3, dia, 9);
            await a.Cronometro.IniciarAsync(id);
            a.IrPara(2026, 3, dia, 10, 30);
            await a.Cronometro.PausarAsync();
            // segunda sessão no mesmo dia soma no mesmo registro
            a.IrPara(2026, 3, dia, 14);
            await a.Cronometro.IniciarAsync(id);
            a.IrPara(2026, 3, dia, 14, 15);
            await a.Cronometro.PausarAsync();
        }

        var regs = await a.Registros.DaTarefaAsync(id);
        Assert.Equal(3, regs.Count);
        Assert.All(regs, r => Assert.Equal(105, r.TempoGastoMin));
        Assert.Equal(1, await a.Banco.LerAsync(c => c.ExecuteScalar<int>("SELECT COUNT(*) FROM Tarefa")));
        Assert.Equal(StatusTarefa.EmAndamento, (await a.Tarefas.ObterAsync(id)).Status);
    }

    [Fact]
    public async Task Sessoes_curtas_acumulam_segundos_residuais_sem_perda()
    {
        await using var a = new Ambiente();
        a.IrPara(2026, 3, 2, 9);
        var id = await a.NovaTarefa("Ajuste", D(2026, 3, 2));
        for (var i = 0; i < 3; i++)
        {
            a.IrPara(2026, 3, 2, 9, i * 5, 0);
            await a.Cronometro.IniciarAsync(id);
            a.IrPara(2026, 3, 2, 9, i * 5, 40);
            await a.Cronometro.PausarAsync();
        }
        var r = Assert.Single(await a.Registros.DaTarefaAsync(id));
        Assert.Equal(2, r.TempoGastoMin);     // 120 s
        Assert.Equal(0, r.SegundosResiduais);
    }

    [Fact]
    public async Task Iniciar_outro_cronometro_pausa_o_atual_e_grava()
    {
        await using var a = new Ambiente();
        a.IrPara(2026, 3, 2, 9);
        var t1 = await a.NovaTarefa("A", D(2026, 3, 2));
        var t2 = await a.NovaTarefa("B", D(2026, 3, 2));
        await a.Cronometro.IniciarAsync(t1);
        a.IrPara(2026, 3, 2, 9, 20);
        var gerados = await a.Cronometro.IniciarAsync(t2);

        Assert.Equal(20 * 60, Assert.Single(gerados).SegundosAdicionados);
        Assert.Equal(t2, (await a.Cronometro.EstadoAsync())!.TarefaId);
    }

    [Fact]
    public async Task Concluir_com_cronometro_rodando_grava_tempo_antes()
    {
        await using var a = new Ambiente();
        a.IrPara(2026, 3, 2, 9);
        var id = await a.NovaTarefa("A", D(2026, 3, 2));
        await a.Cronometro.IniciarAsync(id);
        a.IrPara(2026, 3, 2, 10);
        await a.Tarefas.ConcluirAsync(id);

        Assert.Null(await a.Cronometro.EstadoAsync());
        Assert.Equal(60, Assert.Single(await a.Registros.DaTarefaAsync(id)).TempoGastoMin);
    }

    [Fact]
    public async Task Cronometro_sobrevive_a_reabertura_do_banco()
    {
        await using var a = new Ambiente();
        a.IrPara(2026, 3, 2, 9);
        var id = await a.NovaTarefa("A", D(2026, 3, 2));
        await a.Cronometro.IniciarAsync(id);
        a.Reabrir();
        a.IrPara(2026, 3, 2, 9, 45);
        await a.Cronometro.PausarAsync();
        Assert.Equal(45, Assert.Single(await a.Registros.DaTarefaAsync(id)).TempoGastoMin);
    }

    [Fact]
    public async Task Iniciar_tarefa_agendada_no_futuro_puxa_inicio_para_hoje()
    {
        await using var a = new Ambiente();
        a.IrPara(2026, 3, 2);
        var id = await a.NovaTarefa("Futura", D(2026, 3, 10));
        await a.Cronometro.IniciarAsync(id);
        Assert.Equal(D(2026, 3, 2), (await a.Tarefas.ObterAsync(id)).DataInicio);
    }

    [Fact]
    public async Task Atrasada_quando_passa_da_data_fim_prevista()
    {
        await using var a = new Ambiente();
        a.IrPara(2026, 3, 2);
        var id = await a.NovaTarefa("Prazo", D(2026, 3, 2), fimPrevisto: D(2026, 3, 4));
        var sem = await a.NovaTarefa("Sem prazo", D(2026, 3, 2));

        var dia4 = await a.Tarefas.DoDiaAsync(D(2026, 3, 4));
        Assert.False(dia4.Single(i => i.Tarefa.Id == id).Atrasada);
        var dia5 = await a.Tarefas.DoDiaAsync(D(2026, 3, 5));
        Assert.True(dia5.Single(i => i.Tarefa.Id == id).Atrasada);
        // sem DataFimPrevista: nunca atrasada, continua aparecendo
        Assert.False(dia5.Single(i => i.Tarefa.Id == sem).Atrasada);
        Assert.Contains(sem, await a.IdsDoDia(D(2027, 1, 1)));
    }

    [Fact]
    public async Task Lancamento_manual_valida_limites_da_vida_da_tarefa()
    {
        await using var a = new Ambiente();
        a.IrPara(2026, 3, 5);
        var id = await a.NovaTarefa("A", D(2026, 3, 3));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => a.Registros.LancarManualAsync(id, D(2026, 3, 2), 30, null));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => a.Registros.LancarManualAsync(id, D(2026, 3, 6), 30, null));
        await a.Registros.LancarManualAsync(id, D(2026, 3, 3), 30, "reunião");
        await a.Registros.LancarManualAsync(id, D(2026, 3, 3), 15, "revisão");
        var r = Assert.Single(await a.Registros.DaTarefaAsync(id));
        Assert.Equal(45, r.TempoGastoMin);
        Assert.Equal("reunião | revisão", r.Observacao);
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => a.Registros.LancarManualAsync(id, D(2026, 3, 3), 1440, null));
    }

    [Fact]
    public async Task Editar_inicio_para_depois_de_dia_trabalhado_e_rejeitado()
    {
        await using var a = new Ambiente();
        a.IrPara(2026, 3, 5);
        var id = await a.NovaTarefa("A", D(2026, 3, 3));
        await a.Registros.LancarManualAsync(id, D(2026, 3, 4), 10, null);
        await Assert.ThrowsAsync<RegraDeNegocioException>(() =>
            a.Tarefas.EditarAsync(id, new("A", null, null, D(2026, 3, 5), null)));
        await a.Tarefas.EditarAsync(id, new("A2", null, null, D(2026, 3, 4), null));
    }

    [Fact]
    public async Task Reordenacao_e_persistida_por_dia_e_herdada_por_dias_sem_ordem()
    {
        await using var a = new Ambiente();
        a.IrPara(2026, 3, 2);
        var x = await a.NovaTarefa("X", D(2026, 3, 2));
        var y = await a.NovaTarefa("Y", D(2026, 3, 2));
        var z = await a.NovaTarefa("Z", D(2026, 3, 2));

        Assert.Equal([x, y, z], await a.IdsDoDia(D(2026, 3, 2)));
        await a.Tarefas.ReordenarAsync(D(2026, 3, 3), [z, x, y]);
        Assert.Equal([x, y, z], await a.IdsDoDia(D(2026, 3, 2)));      // dia anterior intacto
        Assert.Equal([z, x, y], await a.IdsDoDia(D(2026, 3, 3)));
        Assert.Equal([z, x, y], await a.IdsDoDia(D(2026, 3, 4)));      // herda do último dia ordenado
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => a.Tarefas.ReordenarAsync(D(2026, 3, 3), [x, y]));
    }

    [Fact]
    public async Task Calendario_marca_dias_com_pendencias_ao_fim_do_dia()
    {
        await using var a = new Ambiente();
        a.IrPara(2026, 3, 2);
        var id = await a.NovaTarefa("A", D(2026, 3, 10));
        a.IrPara(2026, 3, 12);
        await a.Tarefas.ConcluirAsync(id);

        var cal = await a.Relatorios.CalendarioAsync(2026, 3);
        Assert.Equal(31, cal.Count);
        Assert.Equal(0, cal.Single(c => c.Dia == D(2026, 3, 9)).Pendentes);
        Assert.Equal(1, cal.Single(c => c.Dia == D(2026, 3, 10)).Pendentes);
        Assert.Equal(1, cal.Single(c => c.Dia == D(2026, 3, 11)).Pendentes);
        Assert.Equal(0, cal.Single(c => c.Dia == D(2026, 3, 12)).Pendentes);   // concluída no dia
    }

    [Fact]
    public async Task Filtro_SQL_de_visibilidade_equivale_a_regra_do_dominio()
    {
        await using var a = new Ambiente();
        var rnd = new Random(42);
        a.IrPara(2026, 1, 1);
        for (var i = 0; i < 60; i++)
        {
            var ini = D(2026, 1, 1).AddDays(rnd.Next(0, 40));
            var id = await a.NovaTarefa($"t{i}", ini);
            var acao = rnd.Next(3);
            if (acao == 0) continue;
            var fim = ini.AddDays(rnd.Next(0, 15));
            a.IrPara(fim.Year, fim.Month, fim.Day);
            if (acao == 1) await a.Tarefas.ConcluirAsync(id); else await a.Tarefas.CancelarAsync(id);
            a.IrPara(2026, 1, 1);
        }
        var todas = await a.Banco.LerAsync(c => c.Query<Data.Linhas.TarefaLinha>("SELECT * FROM Tarefa"));
        for (var d = D(2025, 12, 30); d <= D(2026, 3, 1); d = d.AddDays(1))
        {
            var esperado = todas.Select(l => Data.Mapeamento.ParaDominio(l)).Where(t => t.VisivelEm(d)).Select(t => t.Id).Order();
            Assert.Equal(esperado, (await a.IdsDoDia(d)).Order());
        }
    }
}
