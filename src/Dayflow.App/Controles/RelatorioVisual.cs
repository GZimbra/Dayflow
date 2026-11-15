using Dayflow.App.Infra;
using Dayflow.Core.Services;
using SkiaSharp;

namespace Dayflow.App.Controles;

/// <summary>
/// Relatório "apresentável" desenhado em SkiaSharp (100% local): mesmo desenho gera PNG e PDF.
/// Largura lógica fixa de 1240 px (A4 a 150 dpi); a altura cresce com o conteúdo.
/// </summary>
public static class RelatorioVisual
{
    public const int Largura = 1240;
    private const float M = 64; // margem
    private static readonly SKColor Laranja = SKColor.Parse("#F26A1B");
    private static readonly SKColor LaranjaNevoa = SKColor.Parse("#FDE6D7");
    private static readonly SKColor Preto = SKColor.Parse("#111111");
    private static readonly SKColor Cinza = SKColor.Parse("#6B6B6B");
    private static readonly SKColor CinzaClaro = SKColor.Parse("#F4F4F2");
    private static readonly SKColor Linha = SKColor.Parse("#E5E5E2");

    public static byte[] Png(Relatorio r, RelatorioEconomia? e, DateTime geradoEmLocal)
    {
        var altura = (int)Math.Ceiling(Medir(r, e, geradoEmLocal));
        using var surface = SKSurface.Create(new SKImageInfo(Largura, altura, SKColorType.Rgba8888, SKAlphaType.Premul))
            ?? throw new InvalidOperationException("Falha ao criar superfície.");
        surface.Canvas.Clear(SKColors.White);
        Desenhar(surface.Canvas, r, e, geradoEmLocal);
        using var img = surface.Snapshot();
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>PDF vetorial (texto selecionável), página única na largura A4.</summary>
    public static byte[] Pdf(Relatorio r, RelatorioEconomia? e, DateTime geradoEmLocal)
    {
        var altura = Medir(r, e, geradoEmLocal);
        const float escala = 595f / Largura; // pontos PDF
        using var ms = new MemoryStream();
        using (var doc = SKDocument.CreatePdf(ms, new SKDocumentPdfMetadata
        {
            Title = "Dayflow · Relatório", Creator = "Dayflow (offline)", Producer = "Dayflow",
        }))
        {
            var c = doc.BeginPage(595, altura * escala);
            c.Scale(escala);
            c.Clear(SKColors.White);
            Desenhar(c, r, e, geradoEmLocal);
            doc.EndPage();
            doc.Close();
        }
        return ms.ToArray();
    }

    private static float Medir(Relatorio r, RelatorioEconomia? e, DateTime g)
    {
        using var rec = new SKPictureRecorder();
        var c = rec.BeginRecording(new SKRect(0, 0, Largura, 100_000));
        var h = Desenhar(c, r, e, g);
        using var _ = rec.EndRecording();
        return h;
    }

    public static float Desenhar(SKCanvas c, Relatorio r, RelatorioEconomia? e, DateTime geradoEmLocal)
    {
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(r);
        var tf = TemaGrafico.Exportacao().Fonte;
        using var fMarca = new SKFont(tf, 20) { Embolden = true };
        using var fTitulo = new SKFont(tf, 46) { Embolden = true };
        using var fSub = new SKFont(tf, 24);
        using var fSecao = new SKFont(tf, 30) { Embolden = true };
        using var fRot = new SKFont(tf, 19);
        using var fKpi = new SKFont(tf, 52) { Embolden = true };
        using var fTxt = new SKFont(tf, 23);
        using var fTxtB = new SKFont(tf, 23) { Embolden = true };
        using var fPeq = new SKFont(tf, 19);
        using var pPreto = P(Preto); using var pBranco = P(SKColors.White); using var pCinza = P(Cinza);
        using var pLaranja = P(Laranja); using var pClaro = P(CinzaClaro); using var pNevoa = P(LaranjaNevoa);
        using var pBranco70 = P(SKColors.White.WithAlpha(180));
        using var pLinha = new SKPaint { Color = Linha, StrokeWidth = 2, IsAntialias = true };
        using var pCor = P(SKColors.Gray);

        float w = Largura, y = 0;

        // ===== Cabeçalho (faixa preta da marca) =====
        c.DrawRect(0, 0, w, 210, pPreto);
        c.DrawRect(0, 204, w, 6, pLaranja);
        c.DrawText("DAYFLOW", M, 62, SKTextAlign.Left, fMarca, pLaranja);
        c.DrawText("Relatório de atividades", M, 122, SKTextAlign.Left, fTitulo, pBranco);
        var per = r.Periodo.Inicio == r.Periodo.Fim
            ? Cap(r.Periodo.Inicio.ToString("dddd, dd 'de' MMMM 'de' yyyy", Fmt.PtBr))
            : $"{r.Periodo.Inicio:dd/MM/yyyy} a {r.Periodo.Fim:dd/MM/yyyy} · {r.Periodo.Dias} dias";
        c.DrawText(per, M, 168, SKTextAlign.Left, fSub, pBranco70);
        c.DrawText($"Gerado em {geradoEmLocal:dd/MM/yyyy HH:mm}", w - M, 168, SKTextAlign.Right, fPeq, pBranco70);
        y = 260;

        // ===== KPIs =====
        var concl = r.Concluidas.Count();
        var abertas = r.EmAberto.Count();
        var canc = r.Canceladas.Count();
        var taxa = concl + abertas == 0 ? 0 : (double)concl / (concl + abertas);
        var diasComTempo = r.PorDia.Count(d => d.Minutos > 0);
        var kpis = new (string Rot, string Val, string? Det, bool Destaque)[]
        {
            ("TEMPO REGISTRADO", Fmt.Duracao(r.MinutosTotais), diasComTempo > 1 ? $"média {Fmt.Duracao(r.MinutosTotais / Math.Max(1, diasComTempo))}/dia" : null, true),
            ("CONCLUÍDAS", concl.ToString(Fmt.PtBr), $"{canc} cancelada(s)", false),
            ("EM ABERTO", abertas.ToString(Fmt.PtBr), null, false),
            ("TAXA DE CONCLUSÃO", taxa.ToString("P0", Fmt.PtBr).Replace(" ", "", StringComparison.Ordinal), "canceladas fora da conta", false),
        };
        float gap = 20, kw = (w - 2 * M - 3 * gap) / 4, kh = 170;
        for (var i = 0; i < kpis.Length; i++)
        {
            var x = M + i * (kw + gap);
            var k = kpis[i];
            c.DrawRoundRect(new SKRect(x, y, x + kw, y + kh), 18, 18, k.Destaque ? pLaranja : pClaro);
            c.DrawText(k.Rot, x + 24, y + 42, SKTextAlign.Left, fRot, k.Destaque ? pBranco70 : pCinza);
            c.DrawText(k.Val, x + 24, y + 110, SKTextAlign.Left, fKpi, k.Destaque ? pBranco : pPreto);
            if (k.Det is { } det) c.DrawText(det, x + 24, y + 146, SKTextAlign.Left, fPeq, k.Destaque ? pBranco70 : pCinza);
        }
        y += kh + 64;

        // ===== Tempo por projeto =====
        y = Secao(c, "Tempo por projeto", y, fSecao, pPreto, pLaranja);
        if (r.PorProjeto.Count == 0) { c.DrawText("Sem tempo registrado no período.", M, y + 30, SKTextAlign.Left, fTxt, pCinza); y += 60; }
        var maxProj = Math.Max(1, r.PorProjeto.Count == 0 ? 1 : r.PorProjeto.Max(p => p.Minutos));
        foreach (var p in r.PorProjeto)
        {
            pCor.Color = Cor(p.Cor);
            c.DrawCircle(M + 9, y + 22, 9, pCor);
            c.DrawText(Encurtar(p.Nome, 26), M + 30, y + 30, SKTextAlign.Left, fTxt, pPreto);
            float bx = M + 380, bw = w - M - 230 - bx;
            c.DrawRoundRect(new SKRect(bx, y + 12, bx + bw, y + 34), 11, 11, pClaro);
            c.DrawRoundRect(new SKRect(bx, y + 12, bx + Math.Max(22, bw * p.Minutos / maxProj), y + 34), 11, 11, pCor);
            c.DrawText(Fmt.Duracao(p.Minutos), w - M - 90, y + 31, SKTextAlign.Right, fTxtB, pPreto);
            c.DrawText(Pct(p.Minutos, r.MinutosTotais), w - M, y + 31, SKTextAlign.Right, fTxt, pCinza);
            y += 54;
        }
        y += 40;

        // ===== Horas por dia =====
        if (r.PorDia.Count > 1)
        {
            y = Secao(c, "Horas por dia", y, fSecao, pPreto, pLaranja);
            y = BarrasDia(c, r.PorDia, y, w, fPeq, pCinza, pPreto, pLinha) + 40;
        }

        // ===== Atividades =====
        y = Secao(c, $"Atividades ({r.Tarefas.Count})", y, fSecao, pPreto, pLaranja);
        float cProj = M + 600, cSit = M + 880, cTempo = w - M;
        c.DrawText("ATIVIDADE", M + 16, y + 26, SKTextAlign.Left, fRot, pCinza);
        c.DrawText("PROJETO", cProj, y + 26, SKTextAlign.Left, fRot, pCinza);
        c.DrawText("SITUAÇÃO", cSit, y + 26, SKTextAlign.Left, fRot, pCinza);
        c.DrawText("TEMPO", cTempo, y + 26, SKTextAlign.Right, fRot, pCinza);
        y += 42;
        c.DrawLine(M, y, w - M, y, pLinha);
        var ordem = r.Concluidas.Concat(r.EmAberto).Concat(r.Canceladas).ToList();
        if (ordem.Count == 0) { c.DrawText("Nenhuma atividade no período.", M + 16, y + 40, SKTextAlign.Left, fTxt, pCinza); y += 60; }
        for (var i = 0; i < ordem.Count; i++)
        {
            var t = ordem[i];
            if (i % 2 == 1) c.DrawRect(M, y, w - 2 * M, 56, pClaro);
            var cancel = t.Situacao == SituacaoNoPeriodo.Cancelada;
            c.DrawText(Encurtar(t.Titulo, 40), M + 16, y + 37, SKTextAlign.Left, fTxt, cancel ? pCinza : pPreto);
            c.DrawText(Encurtar(t.Projeto, 18), cProj, y + 37, SKTextAlign.Left, fTxt, pCinza);
            var (rot, fundo, texto) = t.Situacao switch
            {
                SituacaoNoPeriodo.Concluida => ("Concluída", pLaranja, pBranco),
                SituacaoNoPeriodo.Cancelada => ("Cancelada", pClaro, pCinza),
                _ => ("Em aberto", pNevoa, pPreto),
            };
            var larg = fPeq.MeasureText(rot) + 28;
            c.DrawRoundRect(new SKRect(cSit, y + 13, cSit + larg, y + 45), 16, 16, fundo);
            c.DrawText(rot, cSit + 14, y + 36, SKTextAlign.Left, fPeq, texto);
            c.DrawText(t.MinutosNoPeriodo > 0 ? Fmt.Duracao(t.MinutosNoPeriodo) : "—", cTempo - 16, y + 37, SKTextAlign.Right, fTxtB, pPreto);
            y += 56;
        }
        y += 50;

        // ===== Economia gerada =====
        if (e is { Itens.Count: > 0 })
        {
            y = Secao(c, "Economia gerada", y, fSecao, pPreto, pLaranja);
            float ew = (w - 2 * M - gap) / 2;
            c.DrawRoundRect(new SKRect(M, y, M + ew, y + 130), 18, 18, pPreto);
            c.DrawText("RECORRENTE POR MÊS", M + 24, y + 40, SKTextAlign.Left, fRot, pBranco70);
            c.DrawText(FmtEconomia(e.MinutosPorMesTotal), M + 24, y + 98, SKTextAlign.Left, fKpi, pLaranja);
            c.DrawRoundRect(new SKRect(M + ew + gap, y, w - M, y + 130), 18, 18, pClaro);
            c.DrawText("PONTUAL", M + ew + gap + 24, y + 40, SKTextAlign.Left, fRot, pCinza);
            c.DrawText(FmtEconomia(e.MinutosUnicosTotal), M + ew + gap + 24, y + 98, SKTextAlign.Left, fKpi, pPreto);
            y += 160;
            foreach (var p in e.PorProjeto)
            {
                pCor.Color = Cor(p.Cor);
                c.DrawCircle(M + 9, y + 22, 9, pCor);
                c.DrawText($"{Encurtar(p.Nome, 30)} · {p.Relatorios} tarefa(s), {p.Pessoas} pessoa(s)", M + 30, y + 30, SKTextAlign.Left, fTxt, pPreto);
                c.DrawText($"{FmtEconomia(p.MinutosPorMes)}/mês", w - M, y + 30, SKTextAlign.Right, fTxtB, pLaranja);
                y += 48;
            }
            c.DrawText("Valores informados nos relatórios de conclusão. Mês: dia útil × 21, semana × 52/12.", M, y + 24, SKTextAlign.Left, fPeq, pCinza);
            y += 70;
        }

        // ===== Rodapé =====
        c.DrawLine(M, y, w - M, y, pLinha);
        c.DrawText("Gerado localmente pelo Dayflow · 100% offline · nenhum dado foi enviado pela internet", M, y + 44, SKTextAlign.Left, fPeq, pCinza);
        return y + 80;
    }

    private static float Secao(SKCanvas c, string titulo, float y, SKFont f, SKPaint texto, SKPaint marca)
    {
        c.DrawRoundRect(new SKRect(M, y, M + 8, y + 34), 4, 4, marca);
        c.DrawText(titulo, M + 24, y + 29, SKTextAlign.Left, f, texto);
        return y + 66;
    }

    private static float BarrasDia(SKCanvas c, IReadOnlyList<MinutosDia> dias, float y, float w, SKFont f, SKPaint suave, SKPaint texto, SKPaint grade)
    {
        float altura = 240, x0 = M + 60, x1 = w - M, y0 = y + altura, y1 = y + 10;
        var maxH = Math.Max(1, (int)Math.Ceiling(dias.Max(d => d.Minutos) / 60.0));
        var passo = maxH <= 4 ? 1 : (int)Math.Ceiling(maxH / 4.0);
        var topo = (int)Math.Ceiling(maxH / (double)passo) * passo;
        for (var v = 0; v <= topo; v += passo)
        {
            var yy = y0 - (y0 - y1) * v / topo;
            c.DrawLine(x0, yy, x1, yy, grade);
            c.DrawText($"{v}h", x0 - 14, yy + 7, SKTextAlign.Right, f, suave);
        }
        var slot = (x1 - x0) / dias.Count;
        var k = Math.Max(1, (int)Math.Ceiling(70 / slot));
        var bw = Math.Min(slot * 0.62f, 48);
        using var barra = P(Laranja);
        for (var i = 0; i < dias.Count; i++)
        {
            var cx = x0 + slot * (i + 0.5f);
            var hgt = (y0 - y1) * dias[i].Minutos / 60f / topo;
            if (dias[i].Minutos > 0)
                c.DrawRoundRect(new SKRect(cx - bw / 2, y0 - hgt, cx + bw / 2, y0), 6, 6, barra);
            if (i % k == 0)
                c.DrawText(Fmt.Curto(dias[i].Dia), cx, y0 + 32, SKTextAlign.Center, f,
                    dias[i].Dia.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ? suave : texto);
        }
        return y0 + 40;
    }

    private static SKPaint P(SKColor c) => new() { Color = c, IsAntialias = true };
    private static SKColor Cor(string hex) => SKColor.TryParse(hex, out var c) ? c : SKColors.Gray;
    private static string Encurtar(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
    private static string Pct(int parte, int total) => total == 0 ? "" : ((double)parte / total).ToString("P0", Fmt.PtBr).Replace(" ", "", StringComparison.Ordinal);
    private static string Cap(string s) => s.Length == 0 ? s : char.ToUpper(s[0], Fmt.PtBr) + s[1..];
    private static string FmtEconomia(double minutos) => Fmt.Duracao((int)Math.Round(minutos));
}
