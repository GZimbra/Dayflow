using Dayflow.Core.Domain;
using Dayflow.Core.Services;
using static Dayflow.Core.Tests.Ambiente;

namespace Dayflow.Core.Tests;

public sealed class ConclusoesTests
{
    private static async Task<(Ambiente a, int proj, int tarefa)> Preparar()
    {
        var a = new Ambiente();
        a.IrPara(2026, 3, 2, 10);
        var proj = await a.Projetos.CriarAsync("Automação", "#F26A1B");
        var t = await a.NovaTarefa("Script de conferência", D(2026, 3, 2), proj);
        return (a, proj, t);
    }

    [Fact]
    public async Task Rejeita_tarefa_nao_concluida_ou_sem_projeto()
    {
        var (a, _, t) = await Preparar();
        await using var _a = a;
        var ex = await Assert.ThrowsAsync<RegraDeNegocioException>(() =>
            a.Conclusoes.SalvarAsync(t, "x", null, 10, FrequenciaEconomia.PorDia, 2));
        Assert.Equal("conclusao.inelegivel", ex.Codigo);

        var solta = await a.NovaTarefa("Sem projeto", D(2026, 3, 2));
        await a.Tarefas.ConcluirAsync(solta);
        ex = await Assert.ThrowsAsync<RegraDeNegocioException>(() =>
            a.Conclusoes.SalvarAsync(solta, "x", null, 10, FrequenciaEconomia.PorDia, 2));
        Assert.Equal("conclusao.inelegivel", ex.Codigo);
    }

    [Theory]
    [InlineData(null, null, 0, 1, "conclusao.vazio")]
    [InlineData("ok", null, -1, 1, "conclusao.minutos")]
    [InlineData("ok", null, 10, 0, "conclusao.pessoas")]
    [InlineData("ok", null, 100001, 1, "conclusao.minutos")]
    public void Validacao_do_dominio(string? resumo, string? benef, int min, int pessoas, string codigo)
    {
        var ex = Assert.Throws<RegraDeNegocioException>(() =>
            RelatorioConclusao.Criar(1, resumo, benef, min, FrequenciaEconomia.PorDia, pessoas, DateTime.UtcNow, DateTime.UtcNow));
        Assert.Equal(codigo, ex.Codigo);
    }

    [Fact]
    public void Frequencia_invalida_e_rejeitada() =>
        Assert.Equal("conclusao.frequencia", Assert.Throws<RegraDeNegocioException>(() =>
            RelatorioConclusao.Criar(1, "x", null, 1, (FrequenciaEconomia)9, 1, DateTime.UtcNow, DateTime.UtcNow)).Codigo);

    [Fact]
    public async Task Upsert_preserva_CriadoEm_e_atualiza_campos()
    {
        var (a, _, t) = await Preparar();
        await using var _a = a;
        await a.Tarefas.ConcluirAsync(t);
        await a.Conclusoes.SalvarAsync(t, "  Primeira versão ", "Expedição", 10, FrequenciaEconomia.PorDia, 3);
        var r1 = await a.Conclusoes.ObterAsync(t);
        Assert.NotNull(r1);
        Assert.Equal("Primeira versão", r1.Resumo);
        Assert.Equal(30, r1.MinutosPorOcorrencia);
        Assert.Equal(30 * 21, r1.MinutosPorMes!.Value, 6);

        a.IrPara(2026, 3, 3, 9);
        await a.Conclusoes.SalvarAsync(t, "Segunda", null, 60, FrequenciaEconomia.UnicaVez, 1);
        var r2 = await a.Conclusoes.ObterAsync(t);
        Assert.NotNull(r2);
        Assert.Equal(r1.CriadoEm, r2.CriadoEm);
        Assert.True(r2.AtualizadoEm > r1.AtualizadoEm);
        Assert.Null(r2.Beneficiados);
        Assert.Null(r2.MinutosPorMes);

        await a.Conclusoes.RemoverAsync(t);
        Assert.Null(await a.Conclusoes.ObterAsync(t));
    }

    [Fact]
    public async Task Agregacao_por_projeto_usa_fatores_mensais_no_SQL()
    {
        var (a, proj, t1) = await Preparar();
        await using var _a = a;
        var proj2 = await a.Projetos.CriarAsync("Qualidade", "#111111");
        var t2 = await a.NovaTarefa("Checklist", D(2026, 3, 2), proj);
        var t3 = await a.NovaTarefa("Migração", D(2026, 3, 2), proj);
        var t4 = await a.NovaTarefa("Painel", D(2026, 3, 2), proj2);
        foreach (var id in new[] { t1, t2, t3, t4 }) await a.Tarefas.ConcluirAsync(id);

        await a.Conclusoes.SalvarAsync(t1, "a", null, 10, FrequenciaEconomia.PorDia, 2);     // 20 × 21 = 420/mês
        await a.Conclusoes.SalvarAsync(t2, "b", null, 30, FrequenciaEconomia.PorSemana, 1);  // 30 × 52/12 = 130/mês
        await a.Conclusoes.SalvarAsync(t3, "c", null, 120, FrequenciaEconomia.UnicaVez, 4);  // 480 únicos
        await a.Conclusoes.SalvarAsync(t4, "d", null, 15, FrequenciaEconomia.PorMes, 10);    // 150/mês

        var e = await a.Conclusoes.EconomiaAsync(Periodo.Mes(2026, 3));
        Assert.Equal(4, e.Itens.Count);
        var p1 = Assert.Single(e.PorProjeto, p => p.ProjetoId == proj);
        Assert.Equal(3, p1.Relatorios);
        Assert.Equal(2 + 1 + 4, p1.Pessoas);
        Assert.Equal(17, e.PessoasTotal);
        Assert.Equal(4, e.RelatoriosTotal);
        Assert.Equal(480, p1.MinutosUnicos);
        Assert.Equal(420 + 130, p1.MinutosPorMes, 6);
        var p2 = Assert.Single(e.PorProjeto, p => p.ProjetoId == proj2);
        Assert.Equal(150, p2.MinutosPorMes, 6);
        Assert.Equal(700, e.MinutosPorMesTotal, 6);
        Assert.Equal(480, e.MinutosUnicosTotal);

        // SQL e domínio concordam
        Assert.Equal(e.Itens.Sum(i => i.Relatorio.MinutosPorMes ?? 0), e.MinutosPorMesTotal, 6);

        // Fora do período: nada
        Assert.Empty((await a.Conclusoes.EconomiaAsync(Periodo.Mes(2026, 4))).PorProjeto);
    }

    [Fact]
    public async Task Tarefa_reaberta_sai_da_agregacao_e_exclusao_faz_cascade()
    {
        var (a, _, t) = await Preparar();
        await using var _a = a;
        await a.Tarefas.ConcluirAsync(t);
        await a.Conclusoes.SalvarAsync(t, "x", null, 10, FrequenciaEconomia.PorDia, 1);
        await a.Tarefas.ReabrirAsync(t);
        Assert.Empty((await a.Conclusoes.EconomiaAsync(Periodo.Mes(2026, 3))).Itens);
        Assert.NotNull(await a.Conclusoes.ObterAsync(t)); // preservado para quando concluir de novo

        await a.Tarefas.ExcluirAsync(t);
        Assert.Equal(0, await a.Banco.LerAsync(c => c.ExecuteScalar<int>("SELECT COUNT(*) FROM RelatorioConclusao")));
    }
}

public sealed class ExportEconomiaTests
{
    [Fact]
    public async Task Csv_e_texto_da_economia()
    {
        await using var a = new Ambiente();
        a.IrPara(2026, 3, 2, 10);
        var proj = await a.Projetos.CriarAsync("Automação", "#F26A1B");
        var t = await a.NovaTarefa("=Robô", Ambiente.D(2026, 3, 2), proj);
        await a.Tarefas.ConcluirAsync(t);
        await a.Conclusoes.SalvarAsync(t, "Linha 1\nLinha 2", "Expedição; turno B", 10, FrequenciaEconomia.PorDia, 2);
        var e = await a.Conclusoes.EconomiaAsync(Periodo.Mes(2026, 3));

        var csv = ExportadorRelatorio.CsvEconomia(e);
        Assert.StartsWith("﻿", csv, StringComparison.Ordinal);
        Assert.Contains("'=Robô", csv, StringComparison.Ordinal);
        Assert.Contains("\"Expedição; turno B\"", csv, StringComparison.Ordinal);
        Assert.Contains(";420;", csv, StringComparison.Ordinal);

        var txt = ExportadorRelatorio.TextoEconomia(e);
        Assert.Contains("7h00 por mês", txt, StringComparison.Ordinal);
        Assert.Contains("10 min × 2 pessoa(s) por dia útil", txt, StringComparison.Ordinal);
        Assert.Equal("dayflow-economia-2026-03-01_2026-03-31.csv", ExportadorRelatorio.NomeArquivoEconomia(e, "csv"));
    }
}
