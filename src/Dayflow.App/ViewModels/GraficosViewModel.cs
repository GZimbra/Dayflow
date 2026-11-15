using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Dayflow.App.Controles;
using Dayflow.App.Infra;
using Dayflow.Core.Domain;
using Dayflow.Core.Services;
using Microcharts;
using Microsoft.Extensions.Logging;

namespace Dayflow.App.ViewModels;

public static class FmtEconomia
{
    /// <summary>"420 min · 7h00": minutos e horas lado a lado.</summary>
    public static string MinHoras(double minutos)
    {
        var m = (int)Math.Round(minutos);
        return m < 60 ? $"{m} min" : $"{m.ToString("N0", Fmt.PtBr)} min · {m / 60}h{m % 60:00}";
    }
}

public sealed class ItemEconomiaVm(ItemEconomia i)
{
    private readonly RelatorioConclusao _r = i.Relatorio;
    public string Tarefa => i.Titulo;
    public string Detalhe => i.Relatorio.Beneficiados is { } b ? $"{Fmt.Curto(i.DataConclusao)} · {b}" : Fmt.Curto(i.DataConclusao);
    public string MinutosOcorrencia => $"{_r.MinutosEconomizados} min";
    public string Pessoas => _r.Pessoas.ToString(Fmt.PtBr);
    public string Frequencia => ExportadorRelatorio.RotuloFrequencia(_r.Frequencia);
    public string PorOcorrencia => FmtEconomia.MinHoras(_r.MinutosPorOcorrencia);
    public string PorMes => _r.MinutosPorMes is { } m ? FmtEconomia.MinHoras(m) : "—";
}

public sealed class GrupoEconomiaVm(EconomiaProjeto p, IEnumerable<ItemEconomia> itens)
{
    public string Nome => p.Nome;
    public Color Cor => Fmt.Cor(p.Cor);
    public IReadOnlyList<ItemEconomiaVm> Itens { get; } = [.. itens.Select(i => new ItemEconomiaVm(i))];
    public string Subtitulo => $"{p.Relatorios} tarefa(s) · {p.Pessoas} pessoa(s) afetada(s)";
    public string Pessoas => p.Pessoas.ToString(Fmt.PtBr);
    public string Pontual => FmtEconomia.MinHoras(p.MinutosUnicos);
    public string PorMes => FmtEconomia.MinHoras(p.MinutosPorMes);
}

/// <summary>
/// Gráficos: todas as agregações vêm prontas do SQLite (ServicoRelatorios, GROUP BY).
/// Aqui só há mapeamento DTO -> entradas do Microcharts.
/// </summary>
public sealed partial class GraficosViewModel : VmBase, IRecipient<DadosAlterados>
{
    private readonly ServicoRelatorios _relatorios;
    private readonly IExportador _exportador;
    private readonly ServicoConclusoes _conclusoes;
    private DadosGraficos? _dados;

    public GraficosViewModel(ServicoRelatorios relatorios, ServicoConclusoes conclusoes, IExportador exportador, IRelogioCivil relogio, IDialogos d, ILogger<GraficosViewModel> log)
        : base(d, log)
    {
        _relatorios = relatorios;
        _exportador = exportador;
        _conclusoes = conclusoes;
        Periodo = new SeletorPeriodo(relogio, ModoPeriodo.Mes);
        Periodo.Mudou += (_, _) => _ = CarregarAsync();
        Empilhado = [];
        WeakReferenceMessenger.Default.Register(this);
    }

    public SeletorPeriodo Periodo { get; }

    [ObservableProperty] private Chart? _horasBarras;
    [ObservableProperty] private Chart? _horasPizza;
    [ObservableProperty] private IReadOnlyList<ConcluidasDia> _concluidasDias = [];
    [ObservableProperty] private Chart? _taxaConclusao;
    [ObservableProperty] private IReadOnlyList<TempoTarefaDia> _empilhado;
    [ObservableProperty] private bool _mostrarPizza;
    [ObservableProperty] private bool _semHoras;
    [ObservableProperty] private bool _semMultiDia;
    [ObservableProperty] private bool _semTaxa;
    [ObservableProperty] private string _totalConcluidas = "";
    [ObservableProperty] private IReadOnlyList<GrupoEconomiaVm> _economiaProjetos = [];
    [ObservableProperty] private string _economiaTotalPessoas = "0";
    [ObservableProperty] private string _economiaTotalMes = "0 min";
    [ObservableProperty] private string _economiaTotalPontual = "0 min";
    [ObservableProperty] private string _economiaTotalRelatorios = "0";
    [ObservableProperty] private bool _semEconomia = true;

    public bool MostrarBarras => !MostrarPizza;
    partial void OnMostrarPizzaChanged(bool value) => OnPropertyChanged(nameof(MostrarBarras));

    public void Receive(DadosAlterados message) => Invalidar(CarregarAsync);

    [RelayCommand]
    private void AlternarHoras(string? modo) => MostrarPizza = modo == "pizza";

    [RelayCommand]
    public Task CarregarAsync() => ExecutarAsync(async () =>
    {
        var g = NovaGeracao();
        var p = Periodo.Atual;
        var horas = await _relatorios.HorasPorProjetoAsync(p);
        var concl = await _relatorios.ConcluidasPorDiaAsync(p);
        var taxa = await _relatorios.TaxaConclusaoPorProjetoAsync(p);
        var multi = await _relatorios.TempoTarefasMultiDiaAsync(p);
        var eco = await _conclusoes.EconomiaAsync(p);
        if (!EhAtual(g)) return;
        Empilhado = multi;

        var tema = TemaGrafico.Atual();
        HorasBarras = FabricaGraficos.HorasBarras(horas, tema);
        HorasPizza = FabricaGraficos.HorasPizza(horas, tema);
        ConcluidasDias = concl;
        TaxaConclusao = FabricaGraficos.Taxa(taxa, tema);
        var total = concl.Sum(c => c.Quantidade);
        TotalConcluidas = total == 0 ? "Nenhuma tarefa concluída no período." : $"{total} concluída(s) no período";

        SemHoras = horas.Count == 0;
        SemMultiDia = Empilhado.Count == 0;
        SemTaxa = taxa.Count == 0;
        EconomiaProjetos = eco.PorProjeto.Select(x => new GrupoEconomiaVm(x,
            eco.Itens.Where(i => i.Projeto == x.Nome))).ToList();
        EconomiaTotalMes = FmtEconomia.MinHoras(eco.MinutosPorMesTotal);
        EconomiaTotalPontual = FmtEconomia.MinHoras(eco.MinutosUnicosTotal);
        EconomiaTotalRelatorios = $"{eco.RelatoriosTotal} tarefa(s)";
        EconomiaTotalPessoas = eco.PessoasTotal.ToString(Fmt.PtBr);
        SemEconomia = eco.PorProjeto.Count == 0;
        _dados = new DadosGraficos(p, horas, concl, Empilhado, taxa, MostrarPizza, eco);
    });

    /// <summary>Exporta PNG renderizado localmente e abre o compartilhamento do Android (ação explícita do usuário).</summary>
    [RelayCommand]
    private async Task ExportarPngAsync()
    {
        if (_dados is null) return;
        var opcoes = new Dictionary<string, GraficoExportavel>
        {
            ["Todos os gráficos (uma imagem)"] = GraficoExportavel.Todos,
            [ExportadorGraficos.Titulo(GraficoExportavel.Horas)] = GraficoExportavel.Horas,
            [ExportadorGraficos.Titulo(GraficoExportavel.Concluidas)] = GraficoExportavel.Concluidas,
            [ExportadorGraficos.Titulo(GraficoExportavel.MultiDia)] = GraficoExportavel.MultiDia,
            [ExportadorGraficos.Titulo(GraficoExportavel.Taxa)] = GraficoExportavel.Taxa,
            [ExportadorGraficos.Titulo(GraficoExportavel.Economia)] = GraficoExportavel.Economia,
        };
        var r = await Dialogos.EscolherAsync("Exportar gráficos (PNG)", null, [.. opcoes.Keys]);
        if (r is null || !opcoes.TryGetValue(r, out var qual)) return;
        await ExecutarAsync(async () =>
        {
            var dados = _dados with { HorasEmPizza = MostrarPizza };
            var png = await Task.Run(() => ExportadorGraficos.Png(qual, dados));
            await _exportador.CompartilharAsync(ExportadorGraficos.NomeArquivo(qual, dados.Periodo), png, "image/png", "Exportar gráficos");
            Log.LogInformation("Gráfico exportado ({Grafico})", qual);
        });
    }
}
