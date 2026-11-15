using Dayflow.App.Infra;

namespace Dayflow.App;

public partial class AppShell : Shell
{
    /// <summary>Largura a partir da qual o trilho de navegação completo (com nomes) fica fixo (tablet).</summary>
    public const double LarguraTablet = 840;

    /// <summary>Celular deitado a partir desta largura ganha trilho fixo só com ícones.</summary>
    public const double LarguraTrilhoCompacto = 640;

    private const double LarguraTrilhoCompleto = 232;
    private const double LarguraTrilhoIcones = 80;

    private enum ModoNavegacao { Gaveta, TrilhoIcones, TrilhoCompleto }

    private ModoNavegacao? _modo;

    public AppShell() => InitializeComponent();

    /// <summary>
    /// Tablet (≥ 840 dp de largura e altura de tablet): trilho fixo com nomes, como antes.
    /// Celular deitado (ex.: POCO X7 Pro ≈ 986×443 dp): trilho fixo só com ícones (80 dp), sem barra de título,
    /// para não perder 232 dp de largura nem 56 dp de altura numa tela baixa.
    /// Celular em pé: menu lateral (gaveta) com barra de título.
    /// </summary>
    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        if (width <= 0 || height <= 0) return;
        AtualizarClasseTela(width);
        var modo = width >= LarguraTablet && !Tela.Baixa(height) && Math.Min(width, height) >= Tela.MenorLadoTablet
            ? ModoNavegacao.TrilhoCompleto
            : width >= LarguraTrilhoCompacto && width > height
                ? ModoNavegacao.TrilhoIcones
                : ModoNavegacao.Gaveta;
        if (_modo == modo) return; // só ao cruzar o limite (girar a tela), não a cada medida
        _modo = modo;
        // Fora do passe de layout: trocar o flyout durante a medição causa piscadas/erros no Android.
        Dispatcher.Dispatch(() => AplicarNavegacao(modo));
    }

    private bool? _estreita;

    /// <summary>Recursos globais TelaEstreita/TelaLarga: tabelas largas viram listas no celular em pé.</summary>
    private void AtualizarClasseTela(double width)
    {
        var estreita = Tela.Estreita(width);
        if (_estreita == estreita) return;
        _estreita = estreita;
        Dispatcher.Dispatch(() =>
        {
            if (Application.Current is not { } app) return;
            app.Resources["TelaEstreita"] = estreita;
            app.Resources["TelaLarga"] = !estreita;
        });
    }

    private void AplicarNavegacao(ModoNavegacao modo)
    {
        var fixo = modo != ModoNavegacao.Gaveta;
        var icones = modo == ModoNavegacao.TrilhoIcones;
        if (Application.Current is { } app) app.Resources["TrilhoComRotulos"] = !icones;
        FlyoutWidth = icones ? LarguraTrilhoIcones : LarguraTrilhoCompleto;
        CabecalhoTrilho.Padding = icones ? new Thickness(20, 24, 20, 20) : new Thickness(24, 32, 24, 28);
        FlyoutBehavior = fixo ? FlyoutBehavior.Locked : FlyoutBehavior.Flyout;
        SetNavBarIsVisible(this, !fixo);
        if (CurrentPage is { } pg) SetNavBarIsVisible(pg, !fixo);
    }

    /// <summary>Página nova (troca de aba) herda a visibilidade da barra do modo atual.</summary>
    protected override void OnNavigated(ShellNavigatedEventArgs args)
    {
        base.OnNavigated(args);
        if (_modo is { } m && CurrentPage is { } pg) SetNavBarIsVisible(pg, m == ModoNavegacao.Gaveta);
    }
}
