using Dayflow.Core.Domain;
using static Dayflow.Core.Tests.Ambiente;

namespace Dayflow.Core.Tests;

public class FusoEMeiaNoiteTests
{
    [Fact]
    public async Task Cronometro_atravessando_meia_noite_divide_em_dois_registros()
    {
        await using var a = new Ambiente();
        a.IrPara(2026, 3, 2, 8);
        var id = await a.NovaTarefa("Plantão", D(2026, 3, 2));
        a.IrPara(2026, 3, 2, 23, 30);
        await a.Cronometro.IniciarAsync(id);
        a.IrPara(2026, 3, 3, 0, 40);
        await a.Cronometro.PausarAsync();

        var regs = await a.Registros.DaTarefaAsync(id);
        Assert.Equal(2, regs.Count);
        Assert.Equal((D(2026, 3, 2), 30), (regs[0].Data, regs[0].TempoGastoMin));
        Assert.Equal((D(2026, 3, 3), 40), (regs[1].Data, regs[1].TempoGastoMin));
    }

    [Fact]
    public async Task Cronometro_esquecido_por_varios_dias_fatia_e_limita_24h_por_dia()
    {
        await using var a = new Ambiente();
        a.IrPara(2026, 3, 2, 8);
        var id = await a.NovaTarefa("Esquecida", D(2026, 3, 2));
        a.IrPara(2026, 3, 2, 22);
        await a.Cronometro.IniciarAsync(id);
        a.IrPara(2026, 3, 5, 1);
        await a.Cronometro.PausarAsync();

        var regs = await a.Registros.DaTarefaAsync(id);
        Assert.Equal([120, 1440, 1440, 60], regs.Select(r => r.TempoGastoMin));
    }

    [Fact]
    public void Hoje_usa_o_fuso_local_e_nao_a_data_UTC()
    {
        // 02:00Z de 01/01 = 23:00 de 31/12 em São Paulo (-03)
        var a = new Ambiente(agora: new DateTimeOffset(2026, 1, 1, 2, 0, 0, TimeSpan.Zero));
        Assert.Equal(D(2025, 12, 31), a.Relogio.Hoje);
        // Kiritimati (+14): 11:00Z de 31/12 já é 01/01
        var k = new Ambiente("Pacific/Kiritimati", new DateTimeOffset(2025, 12, 31, 11, 0, 0, TimeSpan.Zero));
        Assert.Equal(D(2026, 1, 1), k.Relogio.Hoje);
    }

    [Fact]
    public async Task Concluir_as_23h59_local_fica_no_dia_local_mesmo_ja_sendo_outro_dia_em_UTC()
    {
        await using var a = new Ambiente();
        a.IrPara(2026, 3, 2, 8);
        var id = await a.NovaTarefa("A", D(2026, 3, 2));
        a.IrPara(2026, 3, 2, 23, 59);   // 02:59Z de 03/03
        await a.Tarefas.ConcluirAsync(id);
        Assert.Equal(D(2026, 3, 2), (await a.Tarefas.ObterAsync(id)).DataConclusao);
        Assert.DoesNotContain(id, await a.IdsDoDia(D(2026, 3, 3)));
    }

    [Fact]
    public void Meia_noite_inexistente_no_inicio_do_horario_de_verao()
    {
        // São Paulo 2018-11-04: 00:00 -> 01:00 (-03 -> -02). Dia começa às 01:00 local = 03:00Z.
        var a = new Ambiente(agora: new DateTimeOffset(2018, 11, 1, 12, 0, 0, TimeSpan.Zero));
        Assert.Equal(new DateTime(2018, 11, 4, 3, 0, 0, DateTimeKind.Utc), a.Relogio.InicioDoDiaUtc(D(2018, 11, 4)));
        var fatias = DivisorDeTempo.PorDia(
            new DateTime(2018, 11, 4, 2, 30, 0, DateTimeKind.Utc),   // 23:30 de 03/11 (-03)
            new DateTime(2018, 11, 4, 3, 30, 0, DateTimeKind.Utc),   // 01:30 de 04/11 (-02)
            a.Relogio);
        Assert.Equal([(D(2018, 11, 3), TimeSpan.FromMinutes(30)), (D(2018, 11, 4), TimeSpan.FromMinutes(30))],
            fatias.Select(f => (f.Dia, f.Duracao)));
    }

    [Fact]
    public void Dia_de_25_horas_no_fim_do_horario_de_verao()
    {
        // São Paulo 2019-02-17 00:00 (-02) volta para 16/02 23:00 (-03): dia 16/02 tem 25 h.
        var a = new Ambiente(agora: new DateTimeOffset(2019, 2, 10, 12, 0, 0, TimeSpan.Zero));
        var ini16 = a.Relogio.InicioDoDiaUtc(D(2019, 2, 16));
        var ini17 = a.Relogio.InicioDoDiaUtc(D(2019, 2, 17));
        Assert.Equal(TimeSpan.FromHours(25), ini17 - ini16);
        var fatias = DivisorDeTempo.PorDia(ini16, ini17, a.Relogio);
        Assert.Equal(D(2019, 2, 16), Assert.Single(fatias).Dia);
    }

    [Fact]
    public async Task Relogio_retrocedendo_descarta_sessao_sem_gravar_negativo()
    {
        await using var a = new Ambiente();
        a.IrPara(2026, 3, 2, 10);
        var id = await a.NovaTarefa("A", D(2026, 3, 2));
        await a.Cronometro.IniciarAsync(id);
        a.IrPara(2026, 3, 2, 9);
        Assert.Empty(await a.Cronometro.PausarAsync());
        Assert.Empty(await a.Registros.DaTarefaAsync(id));
        Assert.Null(await a.Cronometro.EstadoAsync());
    }

    [Fact]
    public void Timestamps_nao_UTC_sao_rejeitados()
    {
        Assert.Throws<ArgumentException>(() =>
            Tarefa.Nova("x", null, null, D(2026, 1, 1), null, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Local)));
    }
}
