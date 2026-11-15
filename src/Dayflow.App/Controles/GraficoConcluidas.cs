using Dayflow.App.Infra;
using Dayflow.Core.Services;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;

namespace Dayflow.App.Controles;

/// <summary>
/// Tarefas concluídas por dia: linha com pontos, eixo Y só com inteiros, linha de média
/// e rótulos de data horizontais espaçados para não sobrepor. Desenho próprio em SkiaSharp.
/// </summary>
public sealed class GraficoConcluidas : SKCanvasView
{
    public static readonly BindableProperty DadosProperty = BindableProperty.Create(
        nameof(Dados), typeof(IReadOnlyList<ConcluidasDia>), typeof(GraficoConcluidas), null,
        propertyChanged: (b, _, _) => ((GraficoConcluidas)b).InvalidateSurface());

    public IReadOnlyList<ConcluidasDia>? Dados
    {
        get => (IReadOnlyList<ConcluidasDia>?)GetValue(DadosProperty);
        set => SetValue(DadosProperty, value);
    }

    private static readonly SKColor Laranja = SKColor.Parse("#F26A1B");

    protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        e.Surface.Canvas.Clear(SKColors.Transparent);
        if (Dados is not { Count: > 0 } d) return;
        DesenhoSeguro.Executar(e.Surface.Canvas, e.Info,
            () => Desenhar(e.Surface.Canvas, e.Info.Width, e.Info.Height, e.Info.Width / (float)Math.Max(1, Width), d, TemaGrafico.Atual()),
            nameof(GraficoConcluidas));
    }

    public static void Desenhar(SKCanvas c, float w, float h, float escala, IReadOnlyList<ConcluidasDia> dados, TemaGrafico t)
    {
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(dados);
        ArgumentNullException.ThrowIfNull(t);
        if (dados.Count == 0) return;

        using var fonte = new SKFont(t.Fonte, 12 * escala);
        using var fValor = new SKFont(t.Fonte, 11 * escala);
        using var texto = new SKPaint { Color = t.Texto, IsAntialias = true };
        using var suave = new SKPaint { Color = t.TextoSuave, IsAntialias = true };
        using var grade = new SKPaint { Color = t.Linha, StrokeWidth = 1 * escala, IsAntialias = true };
        using var barra = new SKPaint { Color = Laranja, IsAntialias = true };
        using var media = new SKPaint
        {
            Color = t.Texto.WithAlpha(150), StrokeWidth = 1.5f * escala, IsAntialias = true, Style = SKPaintStyle.Stroke,
            PathEffect = SKPathEffect.CreateDash([6 * escala, 5 * escala], 0),
        };

        var max = Math.Max(1, dados.Max(x => x.Quantidade));
        var passoY = max <= 5 ? 1 : (int)Math.Ceiling(max / 4.0);
        var topo = (int)Math.Ceiling(max / (double)passoY) * passoY;

        float eixo = 32 * escala, topoPx = 40 * escala, baseRotulos = 26 * escala;
        float x0 = eixo, x1 = w - 4 * escala, y0 = h - baseRotulos, y1 = topoPx;
        float altura = y0 - y1, slot = (x1 - x0) / dados.Count;

        // Grade horizontal + valores inteiros do eixo
        for (var v = 0; v <= topo; v += passoY)
        {
            var y = y0 - altura * v / topo;
            c.DrawLine(x0, y, x1, y, grade);
            c.DrawText(v.ToString(Fmt.PtBr), x0 - 8 * escala, y + 4 * escala, SKTextAlign.Right, fonte, suave);
        }

        // Rótulos de data: só a cada k dias, para nunca sobrepor
        var semana = dados.Count <= 7;
        var larguraRotulo = (semana ? 52 : 40) * escala;
        var k = Math.Max(1, (int)Math.Ceiling(larguraRotulo / slot));
        var mostrarValor = slot >= 16 * escala;
        var pontos = dados.Select((x, i) => new SKPoint(x0 + slot * (i + 0.5f), y0 - altura * x.Quantidade / topo)).ToArray();

        // Área + linha (sem suavização: contagem por dia não tem valores intermediários)
        using (var area = new SKPath())
        {
            area.MoveTo(pontos[0].X, y0);
            foreach (var p in pontos) area.LineTo(p);
            area.LineTo(pontos[^1].X, y0);
            area.Close();
            using var fill = new SKPaint
            {
                IsAntialias = true,
                Shader = SKShader.CreateLinearGradient(new SKPoint(0, y1), new SKPoint(0, y0),
                    [Laranja.WithAlpha(70), Laranja.WithAlpha(8)], SKShaderTileMode.Clamp),
            };
            c.DrawPath(area, fill);
        }
        using (var linha = new SKPath())
        {
            linha.MoveTo(pontos[0]);
            foreach (var p in pontos.Skip(1)) linha.LineTo(p);
            using var traco = new SKPaint
            {
                Color = Laranja, StrokeWidth = 2.5f * escala, IsAntialias = true, Style = SKPaintStyle.Stroke,
                StrokeJoin = SKStrokeJoin.Round, StrokeCap = SKStrokeCap.Round,
            };
            c.DrawPath(linha, traco);
        }

        var raioPonto = Math.Clamp(slot * 0.18f, 2 * escala, 4.5f * escala);
        using var miolo = new SKPaint { Color = t.Separador, IsAntialias = true };
        for (var i = 0; i < dados.Count; i++)
        {
            var (dia, qtd) = (dados[i].Dia, dados[i].Quantidade);
            var p = pontos[i];
            var fimDeSemana = dia.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
            c.DrawCircle(p, raioPonto, barra);
            c.DrawCircle(p, raioPonto * 0.45f, miolo);
            if (mostrarValor && qtd > 0)
                c.DrawText(qtd.ToString(Fmt.PtBr), p.X, p.Y - raioPonto - 5 * escala, SKTextAlign.Center, fValor, texto);

            if (i % k == 0)
            {
                // tique no eixo X alinhado ao ponto
                c.DrawLine(p.X, y0, p.X, y0 + 4 * escala, grade);
                var rotulo = semana ? $"{Fmt.PtBr.DateTimeFormat.GetAbbreviatedDayName(dia.DayOfWeek).TrimEnd('.')} {dia.Day:00}" : Fmt.Curto(dia);
                c.DrawText(rotulo, p.X, h - 6 * escala, SKTextAlign.Center, fonte, fimDeSemana ? suave : texto);
            }
        }

        // Média diária do período
        var m = dados.Average(x => x.Quantidade);
        if (dados.Count > 1 && m > 0)
        {
            var ym = y0 - altura * (float)m / topo;
            c.DrawLine(x0, ym, x1, ym, media);
            var rot = $"média {m.ToString("0.#", Fmt.PtBr)}/dia";
            // legenda fora da área de plotagem (canto superior direito), para não cobrir pontos
            var ly = y1 - 12 * escala;
            c.DrawText(rot, x1, ly + 4 * escala, SKTextAlign.Right, fonte, suave);
            var lx = x1 - fonte.MeasureText(rot) - 8 * escala;
            c.DrawLine(lx - 22 * escala, ly, lx, ly, media);
        }
    }
}
