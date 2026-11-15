using Dayflow.Core.Domain;
using Dayflow.Core.Services;
using static Dayflow.Core.Tests.Ambiente;

namespace Dayflow.Core.Tests;

public class RelatoriosTests
{
    /// <summary>
    /// Cenário fixo de março/2026:
    ///   P1 "Logística": A (02..04, 60/90/30 min, concluída 04), B (03, 45 min, aberta)
    ///   P2 "Redes":     C (02, 120 min, concluída 02), D (05, cancelada 05, 0 min)
    ///   Sem projeto:    E (02..03, 20/10 min, aberta)
    /// </summary>
    private static async Task<(Ambiente a, int p1, int p2, int A, int B, int C, int Dd, int E)> Cenario()
    {
        var a = new Ambiente();
        a.IrPara(2026, 3, 2, 8);
        var p1 = await a.Projetos.CriarAsync("Logística", "#1E88E5");
        var p2 = await a.Projetos.CriarAsync("Redes", "#43A047");
        var A = await a.NovaTarefa("A", D(2026, 3, 2), p1);
        var B = await a.NovaTarefa("B", D(2026, 3, 3), p1);
        var C = await a.NovaTarefa("C", D(2026, 3, 2), p2);
        var Dd = await a.NovaTarefa("D", D(2026, 3, 5), p2);
        var E = await a.NovaTarefa("E", D(2026, 3, 2));

        a.IrPara(2026, 3, 6, 18);
        await a.Registros.LancarManualAsync(A, D(2026, 3, 2), 60, null);
        await a.Registros.LancarManualAsync(A, D(2026, 3, 3), 90, null);
        await a.Registros.LancarManualAsync(A, D(2026, 3, 4), 30, null);
        await a.Registros.LancarManualAsync(B, D(2026, 3, 3), 45, null);
        await a.Registros.LancarManualAsync(C, D(2026, 3, 2), 120, null);
        await a.Registros.LancarManualAsync(E, D(2026, 3, 2), 20, null);
        await a.Registros.LancarManualAsync(E, D(2026, 3, 3), 10, null);

        a.IrPara(2026, 3, 2, 18); await a.Tarefas.ConcluirAsync(C);
        a.IrPara(2026, 3, 4, 18); await a.Tarefas.ConcluirAsync(A);
        a.IrPara(2026, 3, 5, 18); await a.Tarefas.CancelarAsync(Dd);
        a.IrPara(2026, 3, 6, 18);
        return (a, p1, p2, A, B, C, Dd, E);
    }

    [Fact]
    public async Task Horas_por_projeto_soma_no_SQL_e_agrupa_sem_projeto()
    {
        var (a, p1, p2, _, _, _, _, _) = await Cenario();
        await using var _a = a;
        var h = await a.Relatorios.HorasPorProjetoAsync(new Periodo(D(2026, 3, 1), D(2026, 3, 31)));
        Assert.Equal([("Logística", 225), ("Redes", 120), (Rotulos.SemProjeto, 30)], h.Select(x => (x.Nome, x.Minutos)));
        Assert.Equal(p1, h[0].ProjetoId);
        Assert.Null(h[2].ProjetoId);

        var dia3 = await a.Relatorios.HorasPorProjetoAsync(Periodo.DoDia(D(2026, 3, 3)));
        Assert.Equal([("Logística", 135), (Rotulos.SemProjeto, 10)], dia3.Select(x => (x.Nome, x.Minutos)));
    }

    [Fact]
    public async Task Concluidas_por_dia_preenche_dias_vazios_com_zero()
    {
        var (a, _, _, _, _, _, _, _) = await Cenario();
        await using var _a = a;
        var s = await a.Relatorios.ConcluidasPorDiaAsync(new Periodo(D(2026, 3, 1), D(2026, 3, 7)));
        Assert.Equal([0, 1, 0, 1, 0, 0, 0], s.Select(x => x.Quantidade));   // cancelada não conta
        Assert.Equal(D(2026, 3, 1), s[0].Dia);
    }

    [Fact]
    public async Task Tempo_por_tarefa_multidia_so_inclui_tarefas_com_2_ou_mais_dias()
    {
        var (a, _, _, A, _, _, _, E) = await Cenario();
        await using var _a = a;
        var s = await a.Relatorios.TempoTarefasMultiDiaAsync(new Periodo(D(2026, 3, 1), D(2026, 3, 31)));
        Assert.Equal([A, E], s.Select(x => x.TarefaId).Distinct().Order());
        Assert.Equal([60, 90, 30], s.Where(x => x.TarefaId == A).Select(x => x.Minutos));

        // Recorte de 1 dia: ninguém é multi-dia
        Assert.Empty(await a.Relatorios.TempoTarefasMultiDiaAsync(Periodo.DoDia(D(2026, 3, 3))));
    }

    [Fact]
    public async Task Taxa_de_conclusao_por_projeto_exclui_canceladas_do_denominador()
    {
        var (a, _, _, _, _, _, _, _) = await Cenario();
        await using var _a = a;
        var t = await a.Relatorios.TaxaConclusaoPorProjetoAsync(new Periodo(D(2026, 3, 1), D(2026, 3, 31)));
        var log = t.Single(x => x.Nome == "Logística");
        Assert.Equal((1, 0, 1), (log.Concluidas, log.Canceladas, log.Abertas));
        Assert.Equal(0.5, log.Taxa);
        var redes = t.Single(x => x.Nome == "Redes");
        Assert.Equal((1, 1, 0), (redes.Concluidas, redes.Canceladas, redes.Abertas));
        Assert.Equal(1.0, redes.Taxa);

        // Período que termina antes da conclusão de A: A conta como aberta
        var t3 = await a.Relatorios.TaxaConclusaoPorProjetoAsync(new Periodo(D(2026, 3, 1), D(2026, 3, 3)));
        Assert.Equal((0, 0, 2), t3.Single(x => x.Nome == "Logística") is var l3 ? (l3.Concluidas, l3.Canceladas, l3.Abertas) : default);
    }

    [Fact]
    public async Task Relatorio_diario_classifica_pela_situacao_naquele_dia()
    {
        var (a, _, _, A, B, C, _, E) = await Cenario();
        await using var _a = a;
        var r = await a.Relatorios.RelatorioDiarioAsync(D(2026, 3, 3));
        Assert.Equal(90 + 45 + 10, r.MinutosTotais);
        Assert.Empty(r.Concluidas);
        // C foi concluída dia 02: não é relevante no dia 03
        Assert.Equal([A, B, E], r.EmAberto.Select(x => x.TarefaId).Order());
        Assert.Equal(90, r.Tarefas.Single(x => x.TarefaId == A).MinutosNoPeriodo);
        // status atual de A é Concluida, mas no dia 03 estava em aberto
        Assert.Equal(StatusTarefa.Concluida, r.Tarefas.Single(x => x.TarefaId == A).StatusAtual);

        var r2 = await a.Relatorios.RelatorioDiarioAsync(D(2026, 3, 2));
        Assert.Equal([C], r2.Concluidas.Select(x => x.TarefaId));
        Assert.DoesNotContain(r2.Tarefas, x => x.TarefaId == B);   // B começa dia 03
    }

    [Fact]
    public async Task Relatorio_semanal_ISO_e_totais_por_dia()
    {
        var (a, _, _, _, _, _, Dd, _) = await Cenario();
        await using var _a = a;
        var sem = Periodo.Semana(D(2026, 3, 4));     // quarta
        Assert.Equal((D(2026, 3, 2), D(2026, 3, 8)), (sem.Inicio, sem.Fim));
        var r = await a.Relatorios.RelatorioAsync(sem);
        Assert.Equal(375, r.MinutosTotais);
        Assert.Equal(7, r.PorDia.Count);
        Assert.Equal([200, 145, 30, 0, 0, 0, 0], r.PorDia.Select(x => x.Minutos));
        Assert.Equal(r.MinutosTotais, r.PorProjeto.Sum(x => x.Minutos));
        Assert.Equal([Dd], r.Canceladas.Select(x => x.TarefaId));
    }

    [Fact]
    public async Task Exportacao_CSV_escapa_separador_aspas_e_neutraliza_formula()
    {
        await using var a = new Ambiente();
        a.IrPara(2026, 3, 2, 8);
        var id = await a.NovaTarefa("=HYPERLINK(\"x\");teste", D(2026, 3, 2));
        await a.Registros.LancarManualAsync(id, D(2026, 3, 2), 75, null);
        var r = await a.Relatorios.RelatorioDiarioAsync(D(2026, 3, 2));

        var csv = ExportadorRelatorio.Csv(r);
        Assert.StartsWith("﻿Tipo;Periodo;", csv);
        Assert.Contains("\"'=HYPERLINK(\"\"x\"\");teste\"", csv);
        Assert.Contains("TOTAL;2026-03-02/2026-03-02;;;;;;75\r\n", csv);

        var txt = ExportadorRelatorio.Texto(r);
        Assert.Contains("Tempo total: 1h15", txt);
        Assert.Equal("dayflow-2026-03-02.csv", ExportadorRelatorio.NomeArquivo(r, "csv"));
    }

    [Fact]
    public async Task Consultas_de_grafico_usam_indices()
    {
        await using var a = new Ambiente();
        var plano = await a.Banco.LerAsync(c => c.Query<Plano>(
            "EXPLAIN QUERY PLAN " + ServicoRelatorios.SqlHorasPorProjeto, 1, 2).Select(p => p.detail).ToList());
        Assert.Contains(plano, d => d!.Contains("UX_RegistroDiario_Data_TarefaId", StringComparison.Ordinal));
    }

    private sealed class Plano { public string? detail { get; set; } }
}
