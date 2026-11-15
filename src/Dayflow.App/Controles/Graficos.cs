using Dayflow.App.Infra;
using Dayflow.Core.Services;
using Microcharts;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;

namespace Dayflow.App.Controles;

/// <summary>Cores e tipografia dos gráficos, seguindo o tema. Fonte carregada do pacote (asset local).</summary>
public sealed record TemaGrafico(SKColor Texto, SKColor TextoSuave, SKColor Fundo, SKColor Linha, SKTypeface Fonte)
{
    private static SKTypeface? _fonteCache;

    public SKColor Separador => Fundo == SKColors.Transparent ? SKColors.White : Fundo;

    /// <summary>Tema fixo claro para PNG exportado (legível em qualquer visualizador, independente do tema do app).</summary>
    public static TemaGrafico Exportacao() => new(
        SKColor.Parse("#111111"), SKColor.Parse("#6B6B6B"), SKColors.White, SKColor.Parse("#E5E5E2"), CarregarFonte());

    public static TemaGrafico Atual()
    {
        var escuro = Application.Current?.RequestedTheme == AppTheme.Dark;
        return new TemaGrafico(
            escuro ? SKColor.Parse("#F5F5F5") : SKColor.Parse("#111111"),
            escuro ? SKColor.Parse("#A3A3A3") : SKColor.Parse("#6B6B6B"),
            SKColors.Transparent,
            escuro ? SKColor.Parse("#2B2B2B") : SKColor.Parse("#E5E5E2"),
            CarregarFonte());
    }

    private static SKTypeface CarregarFonte()
    {
        if (_fonteCache is not null) return _fonteCache;
        try
        {
            // Bytes copiados para SKData: o typeface não depende de nenhum Stream (que seria descartado).
            using var s = FileSystem.OpenAppPackageFileAsync("Manrope-SemiBold.ttf").GetAwaiter().GetResult();
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            using var data = SKData.CreateCopy(ms.ToArray());
            _fonteCache = SKTypeface.FromData(data) ?? SKTypeface.Default;
        }
#pragma warning disable CA1031 // fonte é cosmética: qualquer falha (inclusive Java.IO) cai na fonte padrão
        catch (Exception)
        {
            _fonteCache = SKTypeface.Default;
        }
#pragma warning restore CA1031
        return _fonteCache;
    }
}

/// <summary>
/// View para gráficos do Microcharts (substitui Microcharts.Maui.ChartView).
/// Erro ao desenhar não derruba o app: vira mensagem no próprio gráfico + registro local.
/// </summary>
public sealed partial class GraficoChart : SKCanvasView
{
    public static readonly BindableProperty ChartProperty = BindableProperty.Create(
        nameof(Chart), typeof(Chart), typeof(GraficoChart), null,
        propertyChanged: (b, _, _) => ((GraficoChart)b).InvalidateSurface());

    public Chart? Chart
    {
        get => (Chart?)GetValue(ChartProperty);
        set => SetValue(ChartProperty, value);
    }

    protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        var c = e.Surface.Canvas;
        c.Clear(SKColors.Transparent);
        if (Chart is not { } chart || e.Info.Width <= 0 || e.Info.Height <= 0) return;
        AjustarATela(chart, e.Info.Width / (float)Math.Max(1, Width), Width);
        DesenhoSeguro.Executar(c, e.Info, () => chart.Draw(c, e.Info.Width, e.Info.Height), nameof(GraficoChart));
    }
}

public sealed partial class GraficoChart
{
    /// <summary>
    /// Texto em dp (não em px fixo: 26 px viravam ~9 dp num celular de densidade 2,75) e rótulos na vertical
    /// quando a barra fica estreita demais para o nome (celular em pé), para nenhum rótulo se sobrepor ou sumir.
    /// </summary>
    private static void AjustarATela(Chart chart, float escala, double larguraDp)
    {
        var tam = Math.Max(26f, 12f * escala);
        chart.LabelTextSize = tam;
        if (chart is AxisBasedChart a)
        {
            a.ValueLabelTextSize = tam;
            a.SerieLabelTextSize = tam;
        }
        var estreito = larguraDp > 0 && larguraDp < Infra.Tela.LarguraCompacta;
        if (chart is BarChart b)
        {
            var n = Math.Max(1, b.Entries?.Count() ?? 1);
            var vertical = larguraDp / n < 110;
            b.LabelOrientation = vertical ? Orientation.Vertical : Orientation.Horizontal;
            b.ValueLabelOrientation = vertical ? Orientation.Vertical : Orientation.Horizontal;
        }
        else if (chart is DonutChart d)
        {
            d.LabelMode = estreito ? LabelMode.LeftAndRight : LabelMode.RightOnly;
        }
    }
}

/// <summary>Executa um desenho; se falhar, limpa, escreve aviso e registra o erro localmente.</summary>
public static class DesenhoSeguro
{
    public static void Executar(SKCanvas c, SKImageInfo info, Action desenhar, string origem)
    {
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(desenhar);
        var salvo = c.Save();
        try
        {
            desenhar();
        }
#pragma warning disable CA1031 // pintura roda em callback do Android: exceção aqui fecharia o app
        catch (Exception ex)
        {
            c.RestoreToCount(salvo);
            c.Clear(SKColors.Transparent);
            using var f = new SKFont(SKTypeface.Default, Math.Max(12, info.Width / 40f));
            using var p = new SKPaint { Color = SKColor.Parse("#6B6B6B"), IsAntialias = true };
            c.DrawText("Não foi possível desenhar este gráfico.", 12, info.Height / 2f, SKTextAlign.Left, f, p);
#if ANDROID
            Platforms.Android.Diagnostico.Registrar(ex, "grafico/" + origem);
#else
            _ = ex;
#endif
        }
#pragma warning restore CA1031
        finally
        {
            if (c.SaveCount > salvo) c.RestoreToCount(salvo);
        }
    }
}

public static class FabricaGraficos
{
    private const float TamRotulo = 26f;
    private static readonly SKColor Laranja = SKColor.Parse("#F26A1B");

    public static Chart HorasBarras(IReadOnlyList<HorasProjeto> horas, TemaGrafico t) => Estilizar(new BarChart
    {
        Entries = horas.Select(h => new ChartEntry(h.Minutos / 60f)
        {
            Label = Encurtar(h.Nome, 14), ValueLabel = Fmt.Duracao(h.Minutos),
            Color = SKColor.Parse(h.Cor), TextColor = t.Texto, ValueLabelColor = t.Texto,
        }).ToList(),
        LabelOrientation = Orientation.Horizontal,
        ValueLabelOrientation = Orientation.Horizontal,
        CornerRadius = 8,
        BarAreaAlpha = 0,
        MinValue = 0,
    }, t);

    public static Chart HorasPizza(IReadOnlyList<HorasProjeto> horas, TemaGrafico t) => Estilizar(new DonutChart
    {
        Entries = horas.Select(h => new ChartEntry(h.Minutos)
        {
            Label = Encurtar(h.Nome, 18), ValueLabel = Fmt.Duracao(h.Minutos),
            Color = SKColor.Parse(h.Cor), TextColor = t.Texto, ValueLabelColor = t.Texto,
        }).ToList(),
        HoleRadius = 0.58f,
        LabelMode = LabelMode.RightOnly,
    }, t);

    public static Chart Taxa(IReadOnlyList<TaxaConclusaoProjeto> taxa, TemaGrafico t) => Estilizar(new BarChart
    {
        Entries = taxa.Select(x => new ChartEntry((float)(x.Taxa * 100))
        {
            Label = Encurtar(x.Nome, 14),
            ValueLabel = $"{x.Taxa:P0}".Replace(" ", "", StringComparison.Ordinal) + $" ({x.Concluidas}/{x.Concluidas + x.Abertas})",
            Color = SKColor.Parse(x.Cor), TextColor = t.Texto, ValueLabelColor = t.Texto,
        }).ToList(),
        LabelOrientation = Orientation.Horizontal,
        ValueLabelOrientation = Orientation.Horizontal,
        CornerRadius = 8,
        BarAreaAlpha = 0,
        MinValue = 0,
        MaxValue = 100,
    }, t);

    private static Chart Estilizar(Chart c, TemaGrafico t)
    {
        c.BackgroundColor = t.Fundo;
        c.LabelTextSize = TamRotulo;
        c.LabelColor = t.Texto;
        c.Typeface = t.Fonte;
        c.Margin = 24;
        c.IsAnimated = false;
        if (c is AxisBasedChart a)
        {
            a.ValueLabelTextSize = TamRotulo;
            a.SerieLabelTextSize = TamRotulo;
            a.YAxisTextPaint = new SKPaint { Color = t.TextoSuave, IsAntialias = true };
            a.YAxisLinesPaint = new SKPaint { Color = t.Linha, StrokeWidth = 1, IsAntialias = true };
        }
        return c;
    }

    private static string Encurtar(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
}

/// <summary>
/// Barras horizontais empilhadas: uma barra por tarefa multi-dia, um segmento por dia trabalhado.
/// Microcharts não tem barra empilhada; desenho próprio em SkiaSharp (100% local).
/// Cor dos segmentos: rampa do laranja claro (dias antigos) ao laranja forte (recentes).
/// </summary>
public sealed class GraficoEmpilhado : SKCanvasView
{
    public static readonly BindableProperty DadosProperty = BindableProperty.Create(
        nameof(Dados), typeof(IReadOnlyList<TempoTarefaDia>), typeof(GraficoEmpilhado), null,
        propertyChanged: (b, _, _) => ((GraficoEmpilhado)b).InvalidateSurface());

    public IReadOnlyList<TempoTarefaDia>? Dados
    {
        get => (IReadOnlyList<TempoTarefaDia>?)GetValue(DadosProperty);
        set => SetValue(DadosProperty, value);
    }

    private static readonly SKColor Claro = SKColor.Parse("#FDD3B5");
    private static readonly SKColor Forte = SKColor.Parse("#C24D0B");

    protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        var c = e.Surface.Canvas;
        c.Clear(SKColors.Transparent);
        var dados = Dados;
        if (dados is null || dados.Count == 0) return;

        var escala = e.Info.Width / (float)Math.Max(1, Width);
        float altura = 0;
        DesenhoSeguro.Executar(c, e.Info, () => altura = Desenhar(c, e.Info.Width, escala, dados, TemaGrafico.Atual()), nameof(GraficoEmpilhado));
        if (altura <= 0) return;
        var alturaDesejada = altura / escala;
        if (Math.Abs(HeightRequest - alturaDesejada) > 2)
            MainThread.BeginInvokeOnMainThread(() => HeightRequest = alturaDesejada);
    }

    /// <summary>Desenha em qualquer canvas (tela ou PNG exportado). Retorna a altura usada, em pixels.</summary>
    public static float Desenhar(SKCanvas c, float larguraPx, float escala, IReadOnlyList<TempoTarefaDia> dados, TemaGrafico t)
    {
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(dados);
        ArgumentNullException.ThrowIfNull(t);
        if (dados.Count == 0) return 0;
        var tarefas = dados.GroupBy(d => (d.TarefaId, d.Titulo))
            .Select(g => (g.Key.Titulo, Total: g.Sum(x => x.Minutos), Segmentos: g.OrderBy(x => x.Dia).ToList()))
            .OrderByDescending(x => x.Total).ToList();
        var dias = dados.Select(d => d.Dia).Distinct().Order().ToList();
        var indiceDia = dias.Select((d, i) => (d, i)).ToDictionary(x => x.d, x => x.i);
        var max = tarefas.Max(x => x.Total);

        using var fonte = new SKFont(t.Fonte, 13 * escala);
        using var texto = new SKPaint { Color = t.Texto, IsAntialias = true };
        using var suave = new SKPaint { Color = t.TextoSuave, IsAntialias = true };
        using var seg = new SKPaint { IsAntialias = true };
        using var sep = new SKPaint { Color = t.Separador, StrokeWidth = 2 * escala };

        // Estreito (celular em pé): nome da tarefa numa linha própria acima da barra, inteiro
        // (antes o nome de até 27 letras invadia a barra numa coluna de ~120 dp).
        var estreito = larguraPx / escala < 520;
        float margem = 8 * escala, colRotulo = estreito ? 0 : Math.Min(220 * escala, larguraPx * 0.32f), colTotal = 64 * escala;
        float alturaBarra = 22 * escala, passo = (estreito ? 30 : 40) * escala, y = margem + 6 * escala;
        float larguraUtil = larguraPx - colRotulo - colTotal - 2 * margem;

        foreach (var (titulo, total, segmentos) in tarefas)
        {
            if (estreito)
            {
                foreach (var linha in Quebrar(titulo, fonte, larguraPx - 2 * margem))
                {
                    y += 16 * escala;
                    c.DrawText(linha, margem, y, SKTextAlign.Left, fonte, texto);
                }
                y += 6 * escala;
            }
            else
            {
                var rotulo = titulo.Length > 28 ? titulo[..27] + "…" : titulo;
                c.DrawText(rotulo, margem, y + alturaBarra * 0.72f, SKTextAlign.Left, fonte, texto);
            }
            var x = margem + colRotulo;
            foreach (var s in segmentos)
            {
                var w = larguraUtil * s.Minutos / max;
                var f = dias.Count == 1 ? 1f : indiceDia[s.Dia] / (float)(dias.Count - 1);
                seg.Color = Interpolar(Claro, Forte, f);
                c.DrawRoundRect(new SKRect(x, y, x + w, y + alturaBarra), 4 * escala, 4 * escala, seg);
                c.DrawLine(x + w, y, x + w, y + alturaBarra, sep);
                x += w;
            }
            c.DrawText(Fmt.Duracao(total), x + 8 * escala, y + alturaBarra * 0.72f, SKTextAlign.Left, fonte, suave);
            y += passo;
        }

        // Legenda: rampa de cor = ordem cronológica dos dias
        y += 6 * escala;
        var lx = margem + colRotulo;
        using var rampa = new SKPaint
        {
            Shader = SKShader.CreateLinearGradient(new SKPoint(lx, 0), new SKPoint(lx + 160 * escala, 0), [Claro, Forte], SKShaderTileMode.Clamp),
        };
        c.DrawRoundRect(new SKRect(lx, y, lx + 160 * escala, y + 10 * escala), 5 * escala, 5 * escala, rampa);
        c.DrawText($"{Fmt.Curto(dias[0])}", lx, y + 28 * escala, SKTextAlign.Left, fonte, suave);
        c.DrawText($"{Fmt.Curto(dias[^1])}", lx + 160 * escala, y + 28 * escala, SKTextAlign.Right, fonte, suave);
        if (estreito)
        {
            c.DrawText("cada segmento = um dia trabalhado", lx, y + 48 * escala, SKTextAlign.Left, fonte, suave);
            return y + 60 * escala;
        }
        c.DrawText("cada segmento = um dia trabalhado", lx + 176 * escala, y + 10 * escala, SKTextAlign.Left, fonte, suave);

        return y + 40 * escala;
    }

    /// <summary>Quebra por palavras para caber na largura (máx. 3 linhas; a última recebe "…" se sobrar texto).</summary>
    private static List<string> Quebrar(string texto, SKFont fonte, float largura)
    {
        var linhas = new List<string>();
        var atual = "";
        foreach (var palavra in texto.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var tentativa = atual.Length == 0 ? palavra : atual + " " + palavra;
            if (fonte.MeasureText(tentativa) <= largura || atual.Length == 0) { atual = tentativa; continue; }
            linhas.Add(atual);
            atual = palavra;
        }
        if (atual.Length > 0) linhas.Add(atual);
        if (linhas.Count > 3) { linhas = linhas.Take(3).ToList(); linhas[2] += "…"; }
        return linhas;
    }

    private static SKColor Interpolar(SKColor a, SKColor b, float f) => new(
        (byte)(a.Red + (b.Red - a.Red) * f), (byte)(a.Green + (b.Green - a.Green) * f), (byte)(a.Blue + (b.Blue - a.Blue) * f));
}
