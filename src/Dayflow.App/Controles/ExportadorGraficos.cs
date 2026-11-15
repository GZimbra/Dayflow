using Dayflow.App.Infra;
using Dayflow.Core.Services;
using Microcharts;
using SkiaSharp;

namespace Dayflow.App.Controles;

public enum GraficoExportavel { Todos, Horas, Concluidas, MultiDia, Taxa, Economia }

public sealed record DadosGraficos(
    Periodo Periodo,
    IReadOnlyList<HorasProjeto> Horas,
    IReadOnlyList<ConcluidasDia> Concluidas,
    IReadOnlyList<TempoTarefaDia> MultiDia,
    IReadOnlyList<TaxaConclusaoProjeto> Taxa,
    bool HorasEmPizza,
    RelatorioEconomia Economia);

/// <summary>
/// Renderiza os gráficos em PNG fora da tela (SkiaSharp raster, CPU, 100% local).
/// Mesmas fábricas usadas na tela; tema claro fixo. Nada é gravado fora do cache até o usuário compartilhar.
/// </summary>
public static class ExportadorGraficos
{
    private const int Largura = 1600;
    private const float Escala = 2f;
    private const int AlturaGrafico = 620;
    private const int Margem = 48;
    private const int LinhaTabela = 56;
    private static readonly SKColor Laranja = SKColor.Parse("#F26A1B");
    private static readonly SKColor Preto = SKColor.Parse("#111111");

    public static string Titulo(GraficoExportavel g) => g switch
    {
        GraficoExportavel.Horas => "Horas por projeto",
        GraficoExportavel.Concluidas => "Tarefas concluídas por dia",
        GraficoExportavel.MultiDia => "Tempo das tarefas multi-dia",
        GraficoExportavel.Taxa => "Taxa de conclusão por projeto",
        GraficoExportavel.Economia => "Economia por projeto",
        _ => "Gráficos",
    };

    public static string NomeArquivo(GraficoExportavel g, Periodo p)
    {
        ArgumentNullException.ThrowIfNull(p);
        var nome = g switch
        {
            GraficoExportavel.Horas => "horas-projeto",
            GraficoExportavel.Concluidas => "concluidas-dia",
            GraficoExportavel.MultiDia => "multi-dia",
            GraficoExportavel.Taxa => "taxa-conclusao",
            GraficoExportavel.Economia => "economia-projeto",
            _ => "graficos",
        };
        return $"dayflow-{nome}-{p.Inicio:yyyy-MM-dd}_{p.Fim:yyyy-MM-dd}.png";
    }

    public static byte[] Png(GraficoExportavel qual, DadosGraficos d)
    {
        ArgumentNullException.ThrowIfNull(d);
        var t = TemaGrafico.Exportacao();
        var secoes = (qual == GraficoExportavel.Todos
            ? new[] { GraficoExportavel.Horas, GraficoExportavel.Concluidas, GraficoExportavel.MultiDia, GraficoExportavel.Taxa, GraficoExportavel.Economia }
            : [qual]).Select(g => (g, Altura: AlturaSecao(g, d, t))).ToList();

        const int cabecalho = 150, tituloSecao = 70, rodape = 70;
        var altura = cabecalho + secoes.Sum(s => (secoes.Count > 1 ? tituloSecao : 0) + s.Altura + Margem) + rodape;

        using var surface = SKSurface.Create(new SKImageInfo(Largura, altura, SKColorType.Rgba8888, SKAlphaType.Premul))
            ?? throw new InvalidOperationException("Falha ao criar superfície de renderização.");
        var c = surface.Canvas;
        c.Clear(SKColors.White);

        using var fTitulo = new SKFont(t.Fonte, 44);
        using var fSub = new SKFont(t.Fonte, 26);
        using var fSecao = new SKFont(t.Fonte, 32);
        using var pPreto = new SKPaint { Color = Preto, IsAntialias = true };
        using var pSuave = new SKPaint { Color = t.TextoSuave, IsAntialias = true };
        using var pLaranja = new SKPaint { Color = Laranja, IsAntialias = true };

        // Faixa da marca: preto + barra laranja
        c.DrawRect(0, 0, Largura, 12, pLaranja);
        c.DrawText("Dayflow · " + Titulo(qual), Margem, 80, SKTextAlign.Left, fTitulo, pPreto);
        c.DrawText($"{d.Periodo.Inicio:dd/MM/yyyy} a {d.Periodo.Fim:dd/MM/yyyy}", Margem, 122, SKTextAlign.Left, fSub, pSuave);

        float y = cabecalho;
        foreach (var (g, alt) in secoes)
        {
            if (secoes.Count > 1)
            {
                c.DrawRect(Margem, y + 18, 8, 36, pLaranja);
                c.DrawText(Titulo(g), Margem + 22, y + 48, SKTextAlign.Left, fSecao, pPreto);
                y += tituloSecao;
            }
            c.Save();
            c.Translate(Margem, y);
            Desenhar(c, g, d, t, Largura - 2 * Margem, alt, fSub, pSuave);
            c.Restore();
            y += alt + Margem;
        }

        c.DrawText("Gerado localmente pelo Dayflow (offline).", Margem, altura - 30, SKTextAlign.Left, fSub, pSuave);

        using var img = surface.Snapshot();
        using var png = img.Encode(SKEncodedImageFormat.Png, 100);
        return png.ToArray();
    }

    private static int AlturaSecao(GraficoExportavel g, DadosGraficos d, TemaGrafico t)
    {
        if (Vazio(g, d)) return 80;
        if (g == GraficoExportavel.Economia) return LinhaTabela * (1 + d.Economia.Itens.Count + 2 * d.Economia.PorProjeto.Count + 1) + 120;
        if (g != GraficoExportavel.MultiDia) return AlturaGrafico;
        using var rec = new SKPictureRecorder();
        var c = rec.BeginRecording(new SKRect(0, 0, Largura - 2 * Margem, 10000));
        var h = GraficoEmpilhado.Desenhar(c, Largura - 2 * Margem, Escala, d.MultiDia, t);
        using var _ = rec.EndRecording();
        return (int)Math.Ceiling(h);
    }

    private static bool Vazio(GraficoExportavel g, DadosGraficos d) => g switch
    {
        GraficoExportavel.Horas => d.Horas.Count == 0,
        GraficoExportavel.Concluidas => d.Concluidas.Count == 0,
        GraficoExportavel.MultiDia => d.MultiDia.Count == 0,
        GraficoExportavel.Taxa => d.Taxa.Count == 0,
        GraficoExportavel.Economia => d.Economia.Itens.Count == 0,
        _ => true,
    };

    private static void Desenhar(SKCanvas c, GraficoExportavel g, DadosGraficos d, TemaGrafico t, int w, int h, SKFont fonte, SKPaint suave)
    {
        if (Vazio(g, d))
        {
            c.DrawText("Sem dados no período.", 0, 40, SKTextAlign.Left, fonte, suave);
            return;
        }
        if (g == GraficoExportavel.Economia)
        {
            Tabela(c, d.Economia, t, w, fonte, suave);
            return;
        }
        if (g == GraficoExportavel.Concluidas)
        {
            GraficoConcluidas.Desenhar(c, w, h, Escala, d.Concluidas, t);
            return;
        }
        if (g == GraficoExportavel.MultiDia)
        {
            GraficoEmpilhado.Desenhar(c, w, Escala, d.MultiDia, t);
            return;
        }
        Chart chart = g switch
        {
            GraficoExportavel.Horas => d.HorasEmPizza ? FabricaGraficos.HorasPizza(d.Horas, t) : FabricaGraficos.HorasBarras(d.Horas, t),
            _ => FabricaGraficos.Taxa(d.Taxa, t),
        };
        chart.Draw(c, w, h);
    }

    /// <summary>
    /// Tabela por projeto: cada tarefa com min/vez, pessoas, frequência, por ocorrência e por mês;
    /// subtotal por projeto e total geral (faixa preta). Valores informados pelo usuário.
    /// </summary>
    private static void Tabela(SKCanvas c, RelatorioEconomia e, TemaGrafico t, int w, SKFont fonte, SKPaint suave)
    {
        using var fTxt = new SKFont(t.Fonte, 26);
        using var fForte = new SKFont(t.Fonte, 28) { Embolden = true };
        using var pTexto = new SKPaint { Color = t.Texto, IsAntialias = true };
        using var pLaranja = new SKPaint { Color = Laranja, IsAntialias = true };
        using var pBranco = new SKPaint { Color = SKColors.White, IsAntialias = true };
        using var pPreto = new SKPaint { Color = Preto, IsAntialias = true };
        using var pLinha = new SKPaint { Color = t.Linha, StrokeWidth = 2 };
        using var pCor = new SKPaint { IsAntialias = true };
        // Colunas (borda direita, exceto Frequência que é alinhada à esquerda)
        float cMin = w * 0.47f, cPes = w * 0.555f, cFreq = w * 0.58f, cOc = w * 0.83f, cMes = w;
        float y = 0;
        const float b = 38; // baseline dentro da linha

        void Celulas(string tarefa, string min, string pes, string freq, string oc, string mes, SKFont f, SKPaint p, SKPaint pMes, float x0)
        {
            var nome = tarefa.Length > 34 ? tarefa[..33] + "…" : tarefa;
            c.DrawText(nome, x0, y + b, SKTextAlign.Left, f, p);
            c.DrawText(min, cMin, y + b, SKTextAlign.Right, f, p);
            c.DrawText(pes, cPes, y + b, SKTextAlign.Right, f, p);
            c.DrawText(freq, cFreq, y + b, SKTextAlign.Left, f, p);
            c.DrawText(oc, cOc, y + b, SKTextAlign.Right, f, p);
            c.DrawText(mes, cMes - (p == pBranco ? 16 : 0), y + b, SKTextAlign.Right, f, pMes);
        }

        Celulas("Tarefa", "Min/vez", "Pessoas", "Frequência", "Por ocorrência", "Por mês", fonte, suave, suave, 0);
        y += LinhaTabela;
        foreach (var p in e.PorProjeto)
        {
            c.DrawLine(0, y, w, y, pLinha);
            pCor.Color = SKColor.TryParse(p.Cor, out var cor) ? cor : SKColors.Gray;
            c.DrawCircle(10, y + 28, 9, pCor);
            c.DrawText($"{p.Nome}  ·  {p.Relatorios} tarefa(s), {p.Pessoas} pessoa(s)", 32, y + b, SKTextAlign.Left, fForte, pTexto);
            y += LinhaTabela;
            foreach (var i in e.Itens.Where(x => x.Projeto == p.Nome))
            {
                var r = i.Relatorio;
                Celulas(i.Titulo, $"{r.MinutosEconomizados} min", r.Pessoas.ToString(Fmt.PtBr),
                    ExportadorRelatorio.RotuloFrequencia(r.Frequencia), MinHoras(r.MinutosPorOcorrencia),
                    r.MinutosPorMes is { } m ? MinHoras(m) : "—", fTxt, pTexto, pLaranja, 32);
                y += LinhaTabela;
            }
            Celulas("Subtotal", "", p.Pessoas.ToString(Fmt.PtBr), "", MinHoras(p.MinutosUnicos) + " pontual",
                MinHoras(p.MinutosPorMes), fonte, suave, pLaranja, 32);
            y += LinhaTabela;
        }
        c.DrawRoundRect(new SKRect(0, y + 8, w, y + 8 + LinhaTabela), 8, 8, pPreto);
        y += 8;
        Celulas($"Total geral · {e.RelatoriosTotal} tarefa(s)", "", e.PessoasTotal.ToString(Fmt.PtBr), "",
            MinHoras(e.MinutosUnicosTotal), MinHoras(e.MinutosPorMesTotal), fForte, pBranco, pLaranja, 20);
        y += LinhaTabela + 44;
        c.DrawText("Total: coluna Por ocorrência = economia pontual (única vez); Por mês = economia recorrente.", 0, y, SKTextAlign.Left, fonte, suave);
        c.DrawText("Valores informados nos relatórios de conclusão. Por ocorrência = minutos × pessoas. Mês: dia útil × 21, semana × 52/12.",
            0, y + 40, SKTextAlign.Left, fonte, suave);
    }

    private static string MinHoras(double minutos) => ViewModels.FmtEconomia.MinHoras(minutos);
}
