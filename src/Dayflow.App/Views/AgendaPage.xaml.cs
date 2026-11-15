using Dayflow.App.ViewModels;

namespace Dayflow.App.Views;

public partial class AgendaPage : ContentPage
{
    private readonly AgendaViewModel _vm;

    public AgendaPage(AgendaViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        _vm.PropertyChanged += AoMudarVm;
    }

    /// <summary>Ao abrir "Por hora": mostra a partir da hora atual (hoje) ou das 7h (outros dias).</summary>
    private void RolarParaHoraUtil() => Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(120), () =>
    {
        try
        {
            var alvo = _vm.Horas.FirstOrDefault(h => h.Agora)?.Hora ?? 7;
            if (_vm.Horas.Count > alvo) ListaHoras.ScrollTo(Math.Max(0, alvo - 1), position: ScrollToPosition.Start, animate: false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentOutOfRangeException)
        {
            // rolagem é conveniência
        }
    });

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_vm.AoAparecer()) await _vm.CarregarAsync();
        else _vm.Retomar();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _vm.AoSumir();
        _vm.PararTique();
    }

    private void AoReordenar(object? sender, EventArgs e) => _vm.ReordenarCommand.Execute(null);

    private (bool, int, int, bool, bool)? _estado;
    private const string ChaveRecolhido = "agenda_cal_recolhido";

    /// <summary>
    /// Deitado (largura ≥ altura) ou tela larga: calendário | dia lado a lado, sempre.
    /// Em pé: calendário em cima, lista do dia embaixo com o resto. No celular em pé (largura &lt; 600 dp,
    /// ex.: POCO X7 Pro ≈ 443 dp) o calendário abre recolhido na semana do dia ("Ver mês" expande; a escolha fica salva).
    /// </summary>
    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        if (width <= 0 || height <= 0) return;
        var largo = width >= height || width >= 900;
        var colCal = (int)Math.Clamp(width * 0.4, 300, 400);
        var altCal = (int)Math.Max(260, height * 0.45);
        var estreito = Infra.Tela.Estreita(width);
        // Altura curta = celular deitado: cabeçalho e cronômetro encolhem para a lista do dia caber.
        var baixo = Infra.Tela.Baixa(height);
        var estado = (largo, largo ? colCal : 0, largo ? 0 : altCal, estreito, baixo);
        if (_estado == estado) return;
        _estado = estado;
        Dispatcher.Dispatch(() =>
        {
            AplicarLayout(largo, colCal, altCal);
            AplicarDensidade(estreito, baixo);
            AplicarCalendario(recolhivel: !largo && estreito, baixo);
        });
    }

    private void AplicarDensidade(bool estreito, bool baixo)
    {
        var compacto = estreito || baixo;
        TituloDiaLabel.FontSize = baixo ? 22 : estreito ? 26 : 34;
        BotaoNova.Text = compacto ? "+ Nova" : "+  Nova tarefa";
        BotaoNova.Padding = compacto ? new Thickness(14, 10) : new Thickness(18, 12);
        CartaoCronometro.Padding = compacto ? new Thickness(14, 8) : new Thickness(20, 16);
        CronometroTempoLabel.FontSize = compacto ? 22 : 30;
        // Em ~410 dp úteis o ícone + espaçamentos largos comiam o nome da tarefa.
        IconeCronometro.IsVisible = !estreito;
        GradeCronometro.ColumnSpacing = estreito ? 10 : 16;
        BotaoPausar.Padding = compacto ? new Thickness(12, 8) : new Thickness(18, 12);
        PainelDia.RowSpacing = compacto ? 8 : 14;
    }

    /// <summary>Altura da célula e modo do calendário (mês inteiro ou só a semana).</summary>
    private void AplicarCalendario(bool recolhivel, bool baixo)
    {
        var alt = baixo ? 42d : recolhivel ? 46d : 54d;
        Resources["AltCelulaCal"] = alt;
        GradeMes.HeightRequest = alt * 6;
        FaixaSemana.HeightRequest = alt;
        BotaoAlternarCalendario.IsVisible = recolhivel;
        _recolhivel = recolhivel;
        _vm.CalendarioRecolhido = recolhivel && Preferences.Default.Get(ChaveRecolhido, true);
    }

    private bool _recolhivel;

    private void AoMudarVm(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AgendaViewModel.ModoHora) && _vm.ModoHora) RolarParaHoraUtil();
        // Só o celular em pé lembra a escolha; deitado/tablet sempre mostram o mês.
        if (e.PropertyName == nameof(AgendaViewModel.CalendarioRecolhido) && _recolhivel)
            Preferences.Default.Set(ChaveRecolhido, _vm.CalendarioRecolhido);
    }

    private void AplicarLayout(bool largo, int colCal, int altCal)
    {
        Grade.ColumnDefinitions.Clear();
        Grade.RowDefinitions.Clear();
        if (largo)
        {
            Grade.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(colCal)));
            Grade.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            Grade.RowDefinitions.Add(new RowDefinition(GridLength.Star));
            Grid.SetColumn(PainelCalendario, 0); Grid.SetRow(PainelCalendario, 0);
            Grid.SetColumn(PainelDia, 1); Grid.SetRow(PainelDia, 0);
            Grade.Padding = colCal < 360 ? new Thickness(16, 12) : new Thickness(28, 24);
            Grade.ColumnSpacing = colCal < 360 ? 16 : 28;
            PainelCalendario.MaximumHeightRequest = double.PositiveInfinity;
        }
        else
        {
            Grade.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            Grade.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            Grade.RowDefinitions.Add(new RowDefinition(GridLength.Star));
            Grid.SetColumn(PainelCalendario, 0); Grid.SetRow(PainelCalendario, 0);
            Grid.SetColumn(PainelDia, 0); Grid.SetRow(PainelDia, 1);
            Grade.Padding = new Thickness(16, 12);
            PainelCalendario.MaximumHeightRequest = altCal;
        }
    }
}
