using System.Globalization;
using System.Text;

namespace Dayflow.Core.Services;

/// <summary>
/// Exportação local (string). O app grava em arquivo no cache e compartilha via Intent: única saída de dados.
/// CSV: separador ';' (Excel pt-BR), UTF-8 com BOM, RFC 4180 para aspas,
/// e neutralização de fórmula (CSV injection) em células iniciadas por = + - @ TAB CR.
/// </summary>
public static class ExportadorRelatorio
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
    public const char Separador = ';';

    public static string Texto(Relatorio r)
    {
        ArgumentNullException.ThrowIfNull(r);
        var sb = new StringBuilder();
        sb.AppendLine(r.Periodo.Inicio == r.Periodo.Fim
            ? $"Dayflow · Relatório de {r.Periodo.Inicio.ToString("dddd, dd/MM/yyyy", PtBr)}"
            : $"Dayflow · Relatório de {r.Periodo.Inicio:dd/MM/yyyy} a {r.Periodo.Fim:dd/MM/yyyy}");
        sb.AppendLine($"Tempo total: {Duracao(r.MinutosTotais)}");
        sb.AppendLine();

        Secao(sb, "Concluídas", r.Concluidas);
        Secao(sb, "Em aberto", r.EmAberto);
        Secao(sb, "Canceladas", r.Canceladas);

        sb.AppendLine("Por projeto");
        if (r.PorProjeto.Count == 0) sb.AppendLine("  (sem tempo registrado)");
        foreach (var p in r.PorProjeto)
            sb.AppendLine($"  {p.Nome}: {Duracao(p.Minutos)}{Percentual(p.Minutos, r.MinutosTotais)}");
        return sb.ToString();
    }

    public static string Csv(Relatorio r)
    {
        ArgumentNullException.ThrowIfNull(r);
        var sb = new StringBuilder();
        sb.Append('﻿');
        Linha(sb, "Tipo", "Periodo", "Projeto", "Tarefa", "Situacao", "StatusAtual", "DataConclusao", "Minutos");
        var per = $"{r.Periodo.Inicio:yyyy-MM-dd}/{r.Periodo.Fim:yyyy-MM-dd}";
        foreach (var t in r.Tarefas)
            Linha(sb, "TAREFA", per, t.Projeto, t.Titulo, t.Situacao.ToString(), t.StatusAtual.ToString(),
                t.DataConclusao?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "", t.MinutosNoPeriodo.ToString(CultureInfo.InvariantCulture));
        foreach (var p in r.PorProjeto)
            Linha(sb, "PROJETO", per, p.Nome, "", "", "", "", p.Minutos.ToString(CultureInfo.InvariantCulture));
        foreach (var d in r.PorDia)
            Linha(sb, "DIA", d.Dia.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), "", "", "", "", "", d.Minutos.ToString(CultureInfo.InvariantCulture));
        Linha(sb, "TOTAL", per, "", "", "", "", "", r.MinutosTotais.ToString(CultureInfo.InvariantCulture));
        return sb.ToString();
    }

    public static string NomeArquivo(Relatorio r, string extensao)
    {
        ArgumentNullException.ThrowIfNull(r);
        return r.Periodo.Inicio == r.Periodo.Fim
            ? $"dayflow-{r.Periodo.Inicio:yyyy-MM-dd}.{extensao}"
            : $"dayflow-{r.Periodo.Inicio:yyyy-MM-dd}_{r.Periodo.Fim:yyyy-MM-dd}.{extensao}";
    }

    public static string RotuloFrequencia(Domain.FrequenciaEconomia f) => f switch
    {
        Domain.FrequenciaEconomia.PorDia => "por dia útil",
        Domain.FrequenciaEconomia.PorSemana => "por semana",
        Domain.FrequenciaEconomia.PorMes => "por mês",
        _ => "uma única vez",
    };

    /// <summary>Economia gerada (valores informados pelo usuário; fatores mensais declarados no texto).</summary>
    public static string TextoEconomia(RelatorioEconomia e)
    {
        ArgumentNullException.ThrowIfNull(e);
        var sb = new StringBuilder();
        sb.AppendLine($"Dayflow · Economia gerada de {e.Periodo.Inicio:dd/MM/yyyy} a {e.Periodo.Fim:dd/MM/yyyy}");
        sb.AppendLine($"Recorrente: {Duracao((int)Math.Round(e.MinutosPorMesTotal))} por mês · Pontual: {Duracao(e.MinutosUnicosTotal)}");
        sb.AppendLine("Valores informados pelo usuário. Estimativa mensal: dia útil × 21, semana × 52/12.");
        sb.AppendLine();
        sb.AppendLine("Por projeto");
        if (e.PorProjeto.Count == 0) sb.AppendLine("  (nenhum relatório de conclusão no período)");
        foreach (var p in e.PorProjeto)
            sb.AppendLine($"  {p.Nome}: {Duracao((int)Math.Round(p.MinutosPorMes))}/mês, {Duracao(p.MinutosUnicos)} pontual ({p.Relatorios} tarefa(s), {p.Pessoas} pessoa(s) afetadas)");
        sb.AppendLine();
        foreach (var i in e.Itens)
        {
            var r = i.Relatorio;
            sb.AppendLine($"• {i.Titulo} [{i.Projeto}] concluída em {i.DataConclusao:dd/MM/yyyy}");
            sb.AppendLine($"  Economia: {r.MinutosEconomizados} min × {r.Pessoas} pessoa(s) {RotuloFrequencia(r.Frequencia)}"
                + (r.MinutosPorMes is { } m ? $" ≈ {Duracao((int)Math.Round(m))}/mês" : $" = {Duracao(r.MinutosPorOcorrencia)}"));
            if (r.Beneficiados is { } b) sb.AppendLine($"  Quem se beneficia: {b}");
            if (r.Resumo is { } s) sb.AppendLine($"  {s.Replace("\n", "\n  ", StringComparison.Ordinal)}");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    public static string CsvEconomia(RelatorioEconomia e)
    {
        ArgumentNullException.ThrowIfNull(e);
        var sb = new StringBuilder();
        sb.Append('﻿');
        Linha(sb, "Tipo", "Projeto", "Tarefa", "DataConclusao", "Minutos", "Pessoas", "Frequencia", "MinutosPorOcorrencia", "MinutosPorMes", "Beneficiados", "Resumo");
        var inv = CultureInfo.InvariantCulture;
        foreach (var i in e.Itens)
        {
            var r = i.Relatorio;
            Linha(sb, "ITEM", i.Projeto, i.Titulo, i.DataConclusao.ToString("yyyy-MM-dd", inv), r.MinutosEconomizados.ToString(inv),
                r.Pessoas.ToString(inv), r.Frequencia.ToString(), r.MinutosPorOcorrencia.ToString(inv),
                r.MinutosPorMes?.ToString("0.##", inv) ?? "", r.Beneficiados ?? "", r.Resumo ?? "");
        }
        foreach (var p in e.PorProjeto)
            Linha(sb, "PROJETO", p.Nome, p.Relatorios.ToString(inv) + " tarefa(s)", "", "", p.Pessoas.ToString(inv), "", p.MinutosUnicos.ToString(inv), p.MinutosPorMes.ToString("0.##", inv), "", "");
        Linha(sb, "TOTAL", "", e.RelatoriosTotal.ToString(inv) + " tarefa(s)", "", "", e.PessoasTotal.ToString(inv), "", e.MinutosUnicosTotal.ToString(inv), e.MinutosPorMesTotal.ToString("0.##", inv), "", "");
        return sb.ToString();
    }

    public static string NomeArquivoEconomia(RelatorioEconomia e, string extensao)
    {
        ArgumentNullException.ThrowIfNull(e);
        return $"dayflow-economia-{e.Periodo.Inicio:yyyy-MM-dd}_{e.Periodo.Fim:yyyy-MM-dd}.{extensao}";
    }

    public static string Duracao(int minutos) => $"{minutos / 60}h{minutos % 60:00}";

    internal static string Celula(string valor)
    {
        var v = valor ?? "";
        if (v.Length > 0 && "=+-@\t\r".Contains(v[0], StringComparison.Ordinal)) v = "'" + v;
        if (v.IndexOfAny([Separador, '"', '\n', '\r']) >= 0) v = "\"" + v.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        return v;
    }

    private static void Linha(StringBuilder sb, params string[] cols)
    {
        sb.AppendJoin(Separador, cols.Select(Celula));
        sb.Append("\r\n");
    }

    private static void Secao(StringBuilder sb, string titulo, IEnumerable<LinhaTarefaRelatorio> itens)
    {
        var lista = itens.ToList();
        sb.AppendLine($"{titulo} ({lista.Count})");
        foreach (var t in lista)
            sb.AppendLine($"  • {t.Titulo} [{t.Projeto}] {Duracao(t.MinutosNoPeriodo)}");
        sb.AppendLine();
    }

    private static string Percentual(int parte, int total) =>
        total == 0 ? "" : $" ({(double)parte / total:P0})".Replace(" ", "", StringComparison.Ordinal);
}
