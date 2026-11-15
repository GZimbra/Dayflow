using Dayflow.Core.Domain;
using static Dayflow.Core.Tests.Ambiente;

namespace Dayflow.Core.Tests;

public sealed class HorariosTests
{
    [Fact]
    public async Task Horario_aparece_no_dia_e_e_por_dia_em_tarefa_multidia()
    {
        await using var a = new Ambiente();
        a.IrPara(2026, 3, 2, 9);
        var t = await a.NovaTarefa("Relatório OEE", D(2026, 3, 2), fimPrevisto: D(2026, 3, 4));
        await a.Horarios.DefinirAsync(t, D(2026, 3, 2), 14 * 60);
        await a.Horarios.DefinirAsync(t, D(2026, 3, 3), 9 * 60 + 30, 90);

        var d2 = Assert.Single(await a.Tarefas.DoDiaAsync(D(2026, 3, 2)));
        Assert.Equal(14 * 60, d2.InicioPlanejadoMin);
        Assert.Equal(60, d2.DuracaoPlanejadaMin);
        var d3 = Assert.Single(await a.Tarefas.DoDiaAsync(D(2026, 3, 3)));
        Assert.Equal(570, d3.InicioPlanejadoMin);
        Assert.Equal(90, d3.DuracaoPlanejadaMin);
        var d4 = Assert.Single(await a.Tarefas.DoDiaAsync(D(2026, 3, 4)));
        Assert.False(d4.TemHorario);

        // upsert: muda o horário do mesmo dia
        await a.Horarios.DefinirAsync(t, D(2026, 3, 2), 16 * 60);
        Assert.Equal(960, (await a.Tarefas.DoDiaAsync(D(2026, 3, 2)))[0].InicioPlanejadoMin);

        await a.Horarios.RemoverAsync(t, D(2026, 3, 2));
        Assert.False((await a.Tarefas.DoDiaAsync(D(2026, 3, 2)))[0].TemHorario);
    }

    [Theory]
    [InlineData(-1, 60, "horario.inicio")]
    [InlineData(1440, 60, "horario.inicio")]
    [InlineData(600, 10, "horario.duracao")]
    [InlineData(23 * 60 + 30, 60, "horario.duracao")]
    public async Task Valida_horario(int inicio, int duracao, string codigo)
    {
        await using var a = new Ambiente();
        a.IrPara(2026, 3, 2, 9);
        var t = await a.NovaTarefa("X", D(2026, 3, 2));
        var ex = await Assert.ThrowsAsync<RegraDeNegocioException>(() => a.Horarios.DefinirAsync(t, D(2026, 3, 2), inicio, duracao));
        Assert.Equal(codigo, ex.Codigo);
    }

    [Fact]
    public async Task Rejeita_dia_em_que_a_tarefa_nao_aparece_e_apaga_em_cascata()
    {
        await using var a = new Ambiente();
        a.IrPara(2026, 3, 2, 9);
        var t = await a.NovaTarefa("X", D(2026, 3, 5));
        var ex = await Assert.ThrowsAsync<RegraDeNegocioException>(() => a.Horarios.DefinirAsync(t, D(2026, 3, 2), 600));
        Assert.Equal("horario.dia", ex.Codigo);

        await a.Horarios.DefinirAsync(t, D(2026, 3, 5), 600);
        await a.Tarefas.ExcluirAsync(t);
        Assert.Equal(0, await a.Banco.LerAsync(c => c.ExecuteScalar<int>("SELECT COUNT(*) FROM HorarioDia")));
    }
}
