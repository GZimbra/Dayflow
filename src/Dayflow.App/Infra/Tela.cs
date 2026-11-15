namespace Dayflow.App.Infra;

/// <summary>
/// Classes de tamanho de tela (Material 3, em dp), usadas por todas as telas em vez de caminhos por aparelho.
/// Ex.: POCO X7 Pro (6,67", 1220×2712, densidade 2,75) ≈ 443×986 dp em pé e 986×443 dp deitado.
/// </summary>
public static class Tela
{
    /// <summary>Abaixo disso a largura é "compacta" (celular em pé).</summary>
    public const double LarguraCompacta = 600;

    /// <summary>Menor lado abaixo disso = celular (deitado ou em pé). Tablet de 7" já passa de 600.</summary>
    public const double MenorLadoTablet = 600;

    /// <summary>Altura curta: celular deitado.</summary>
    public const double AlturaBaixa = 520;

    public static bool Estreita(double largura) => largura > 0 && largura < LarguraCompacta;

    public static bool Baixa(double altura) => altura > 0 && altura < AlturaBaixa;

    /// <summary>Celular pelo menor lado físico da tela (não muda ao girar). Fallback: idioma do MAUI.</summary>
    public static bool EhCelular
    {
        get
        {
            try
            {
                var info = DeviceDisplay.Current.MainDisplayInfo;
                if (info.Density > 0 && info.Width > 0 && info.Height > 0)
                    return Math.Min(info.Width, info.Height) / info.Density < MenorLadoTablet;
            }
            catch (Exception ex) when (ex is InvalidOperationException or NotImplementedException)
            {
                // sem display (testes / net10.0)
            }
            return DeviceInfo.Current.Idiom == DeviceIdiom.Phone;
        }
    }

    /// <summary>
    /// Tamanhos de fonte e alturas que mudam com a classe do aparelho. Os estilos usam DynamicResource,
    /// então trocar aqui reflete em todas as telas. Chamado uma vez na inicialização.
    /// </summary>
    public static void AplicarDensidade(ResourceDictionary r)
    {
        ArgumentNullException.ThrowIfNull(r);
        var cel = EhCelular;
        r["TamDisplay"] = cel ? 28d : 34d;
        r["TamTitulo"] = cel ? 20d : 22d;
        r["TamSubtitulo"] = cel ? 16d : 17d;
        r["AltGrafico"] = cel ? 240d : 300d;
        r["AltGraficoEmpilhado"] = cel ? 220d : 240d;
        r["PaddingCartao"] = cel ? new Thickness(16) : new Thickness(20);
    }
}
