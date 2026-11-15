using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Dayflow.App.Controles;
using Dayflow.App.Infra;
using Dayflow.Core.Domain;
using Dayflow.Core.Services;
using Microsoft.Extensions.Logging;

namespace Dayflow.App.ViewModels;

public sealed class LinhaProjetoVm(HorasProjeto h, int total)
{
    public string Nome => h.Nome;
    public Color Cor => Fmt.Cor(h.Cor);
    public string Tempo => Fmt.Duracao(h.Minutos);
    public double Fracao => total == 0 ? 0 : (double)h.Minutos / total;
    public string Percentual => total == 0 ? "" : $"{Fracao:P0}".Replace(" ", "", StringComparison.Ordinal);
}

public sealed class LinhaTarefaVm(LinhaTarefaRelatorio t, string? corProjeto = null)
{
    public string Titulo => t.Titulo;
    public Color Cor => corProjeto is null ? Color.FromArgb("#A3A3A3") : Fmt.Cor(corProjeto);
    public string Status => t.Situacao switch
    {
        SituacaoNoPeriodo.Concluida => "CONCLUÍDA",
        SituacaoNoPeriodo.Cancelada => "CANCELADA",
        _ => "EM ABERTO",
    };
    public Color StatusFundo => t.Situacao switch
    {
        SituacaoNoPeriodo.Concluida => Color.FromArgb("#F26A1B"),
        SituacaoNoPeriodo.Cancelada => Color.FromArgb("#E5E5E2"),
        _ => Color.FromArgb("#111111"),
    };
    public Color StatusTexto => t.Situacao == SituacaoNoPeriodo.Cancelada ? Color.FromArgb("#6B6B6B") : Colors.White;
    public TextDecorations Riscado => t.Situacao == SituacaoNoPeriodo.Cancelada ? TextDecorations.Strikethrough : TextDecorations.None;
    public string Projeto => t.Projeto;
    public string Tempo => t.MinutosNoPeriodo > 0 ? Fmt.Duracao(t.MinutosNoPeriodo) : "—";
    public string Detalhe => t.Situacao == SituacaoNoPeriodo.Concluida && t.DataConclusao is { } d
        ? $"{t.Projeto} · concluída {Fmt.Curto(d)}" : t.Projeto;
}

public sealed class LinhaEconomiaProjetoVm(EconomiaProjeto e)
{
    public string Nome => e.Nome;
    public Color Cor => Fmt.Cor(e.Cor);
    public string PorMes => Fmt.Duracao((int)Math.Round(e.MinutosPorMes)) + "/mês";
    public string Pontual => e.MinutosUnicos > 0 ? Fmt.Duracao(e.MinutosUnicos) + " pontual" : "";
    public string PontualValor => Fmt.Duracao(e.MinutosUnicos);
    public string Relatorios => e.Relatorios.ToString(Fmt.PtBr);
    public string Qtd => e.Relatorios == 1 ? "1 relatório" : $"{e.Relatorios} relatórios";
}

public sealed class LinhaEconomiaItemVm(ItemEconomia i)
{
    public string Titulo => i.Titulo;
    public string Detalhe => $"{i.Projeto} · {Fmt.Curto(i.DataConclusao)} · {i.Relatorio.MinutosEconomizados} min × {i.Relatorio.Pessoas} {ExportadorRelatorio.RotuloFrequencia(i.Relatorio.Frequencia)}";
    public string Valor => i.Relatorio.MinutosPorMes is { } m ? Fmt.Duracao((int)Math.Round(m)) + "/mês" : Fmt.Duracao(i.Relatorio.MinutosPorOcorrencia);
    public string? Resumo => i.Relatorio.Resumo;
    public bool TemResumo => i.Relatorio.Resumo is not null;
}

public sealed partial class RelatoriosViewModel : VmBase, IRecipient<DadosAlterados>
{
    private readonly ServicoRelatorios _relatorios;
    private readonly IExportador _exportador;
    private readonly ServicoConclusoes _conclusoes;
    private Relatorio? _ultimo;
    private RelatorioEconomia? _economia;

    public RelatoriosViewModel(ServicoRelatorios relatorios, ServicoConclusoes conclusoes, IExportador exportador, IRelogioCivil relogio, IDialogos d,
        ILogger<RelatoriosViewModel> log) : base(d, log)
    {
        _relatorios = relatorios;
        _exportador = exportador;
        _conclusoes = conclusoes;
        Periodo = new SeletorPeriodo(relogio, ModoPeriodo.Dia);
        Periodo.Mudou += (_, _) => _ = CarregarAsync();
        TempoTotal = "0 min";
        WeakReferenceMessenger.Default.Register(this);
    }

    public SeletorPeriodo Periodo { get; }
    public ObservableCollection<LinhaProjetoVm> PorProjeto { get; } = new ColecaoObservavel<LinhaProjetoVm>();
    public ObservableCollection<LinhaTarefaVm> Concluidas { get; } = new ColecaoObservavel<LinhaTarefaVm>();
    public ObservableCollection<LinhaTarefaVm> EmAberto { get; } = new ColecaoObservavel<LinhaTarefaVm>();
    public ObservableCollection<LinhaTarefaVm> Canceladas { get; } = new ColecaoObservavel<LinhaTarefaVm>();
    public ObservableCollection<LinhaTarefaVm> ListaAtual { get; } = new ColecaoObservavel<LinhaTarefaVm>();
    public ObservableCollection<LinhaEconomiaProjetoVm> EconomiaPorProjeto { get; } = new ColecaoObservavel<LinhaEconomiaProjetoVm>();
    public ObservableCollection<LinhaEconomiaItemVm> EconomiaItens { get; } = new ColecaoObservavel<LinhaEconomiaItemVm>();

    [ObservableProperty] private string _economiaMensal = "0 min";
    [ObservableProperty] private string _economiaPontual = "0 min";
    [ObservableProperty] private bool _semEconomia = true;
    [ObservableProperty] private string _economiaItensOcultos = "";

    [ObservableProperty] private string _tempoTotal;
    [ObservableProperty] private int _qtdConcluidas;
    [ObservableProperty] private int _qtdEmAberto;
    [ObservableProperty] private int _qtdCanceladas;
    [ObservableProperty] private bool _semTempo;
    [ObservableProperty] private string _taxa = "—";
    [ObservableProperty] private string _mediaDia = "—";
    [ObservableProperty] private string _periodoTexto = "";
    [ObservableProperty] private string _aba = "concluidas";
    [ObservableProperty] private string _vazioLista = "";
    [ObservableProperty] private bool _listaVazia = true;

    public bool AbaConcluidas => Aba == "concluidas";
    public bool AbaEmAberto => Aba == "aberto";
    public bool AbaCanceladas => Aba == "canceladas";

    partial void OnAbaChanged(string value)
    {
        _limiteLista = BlocoLista;
        OnPropertyChanged(nameof(AbaConcluidas));
        OnPropertyChanged(nameof(AbaEmAberto));
        OnPropertyChanged(nameof(AbaCanceladas));
        AtualizarLista();
    }

    [RelayCommand]
    private void MudarAba(string aba) => Aba = aba;

    private void AtualizarLista()
    {
        var (fonte, vazio) = Aba switch
        {
            "aberto" => (EmAberto, "Nada em aberto no período."),
            "canceladas" => (Canceladas, "Nenhuma tarefa cancelada."),
            _ => (Concluidas, "Nada concluído no período."),
        };
        // Lista não virtualizada (está dentro do ScrollView da página): mostra em blocos para não travar em períodos longos.
        Preencher(ListaAtual, fonte.Take(_limiteLista));
        Restantes = Math.Max(0, fonte.Count - ListaAtual.Count);
        VazioLista = vazio;
        ListaVazia = ListaAtual.Count == 0;
    }

    private const int BlocoLista = 30;
    private int _limiteLista = BlocoLista;
    [ObservableProperty] private int _restantes;
    [ObservableProperty] private bool _temMais;
    partial void OnRestantesChanged(int value)
    {
        TemMais = value > 0;
        OnPropertyChanged(nameof(TextoMais));
    }
    public string TextoMais => $"Mostrar mais ({Restantes} restantes)";

    [RelayCommand]
    private void MostrarMais()
    {
        _limiteLista += BlocoLista;
        AtualizarLista();
    }

    public void Receive(DadosAlterados message) => Invalidar(CarregarAsync);

    [RelayCommand]
    public Task CarregarAsync() => ExecutarAsync(async () =>
    {
        var g = NovaGeracao();
        _limiteLista = BlocoLista;
        var per = Periodo.Atual;
        var r = await _relatorios.RelatorioAsync(per);
        var e = await _conclusoes.EconomiaAsync(per);
        if (!EhAtual(g)) return;
        _ultimo = r;
        TempoTotal = Fmt.Duracao(r.MinutosTotais);
        Preencher(PorProjeto, r.PorProjeto.Select(h => new LinhaProjetoVm(h, r.MinutosTotais)));
        var cores = r.PorProjeto.GroupBy(p => p.Nome).ToDictionary(x => x.Key, x => x.First().Cor);
        LinhaTarefaVm Vm(LinhaTarefaRelatorio t) => new(t, cores.GetValueOrDefault(t.Projeto));
        Preencher(Concluidas, r.Concluidas.Select(Vm));
        Preencher(EmAberto, r.EmAberto.Select(Vm));
        Preencher(Canceladas, r.Canceladas.Select(Vm));
        QtdConcluidas = Concluidas.Count;
        QtdEmAberto = EmAberto.Count;
        QtdCanceladas = Canceladas.Count;
        SemTempo = r.MinutosTotais == 0;
        var total = QtdConcluidas + QtdEmAberto;
        Taxa = total == 0 ? "—" : $"{(double)QtdConcluidas / total:P0}".Replace(" ", "", StringComparison.Ordinal);
        var dias = Math.Max(1, r.Periodo.Dias);
        MediaDia = Fmt.Duracao(r.MinutosTotais / dias) + "/dia";
        PeriodoTexto = r.Periodo.Inicio == r.Periodo.Fim
            ? r.Periodo.Inicio.ToString("dddd, dd 'de' MMMM 'de' yyyy", Fmt.PtBr)
            : $"{r.Periodo.Inicio.ToString("dd/MM/yyyy", Fmt.PtBr)} a {r.Periodo.Fim.ToString("dd/MM/yyyy", Fmt.PtBr)} · {dias} dias";
        AtualizarLista();

        _economia = e;
        EconomiaMensal = Fmt.Duracao((int)Math.Round(e.MinutosPorMesTotal));
        EconomiaPontual = Fmt.Duracao(e.MinutosUnicosTotal);
        Preencher(EconomiaPorProjeto, e.PorProjeto.Select(x => new LinhaEconomiaProjetoVm(x)));
        Preencher(EconomiaItens, e.Itens.Take(20).Select(x => new LinhaEconomiaItemVm(x)));
        EconomiaItensOcultos = Math.Max(0, e.Itens.Count - 20) is var n and > 0 ? $"+ {n} no relatório exportado" : "";
        SemEconomia = e.Itens.Count == 0;
    });

    [RelayCommand]
    private Task ExportarEconomiaTextoAsync() => ExportarEconomia("txt", "text/plain", ExportadorRelatorio.TextoEconomia);

    [RelayCommand]
    private Task ExportarEconomiaCsvAsync() => ExportarEconomia("csv", "text/csv", ExportadorRelatorio.CsvEconomia);

    private Task ExportarEconomia(string ext, string mime, Func<RelatorioEconomia, string> gerar) => ExecutarAsync(async () =>
    {
        if (_economia is null) return;
        await _exportador.CompartilharAsync(ExportadorRelatorio.NomeArquivoEconomia(_economia, ext), gerar(_economia), mime, "Exportar economia gerada");
        Log.LogInformation("Economia exportada ({Formato})", ext);
    });

    [RelayCommand]
    private async Task ExportarAsync()
    {
        if (_ultimo is null) return;
        const string pdf = "Relatório em PDF", png = "Imagem PNG", txt = "Texto", csv = "Planilha CSV";
        var escolha = await Dialogos.EscolherAsync("Exportar relatório", null, pdf, png, txt, csv);
        switch (escolha)
        {
            case pdf: await ExportarVisual("pdf", "application/pdf", RelatorioVisual.Pdf); break;
            case png: await ExportarVisual("png", "image/png", RelatorioVisual.Png); break;
            case txt: await ExportarTextoAsync(); break;
            case csv: await ExportarCsvAsync(); break;
        }
    }

    private Task ExportarVisual(string ext, string mime, Func<Relatorio, RelatorioEconomia?, DateTime, byte[]> gerar) => ExecutarAsync(async () =>
    {
        if (_ultimo is null) return;
        var r = _ultimo;
        var e = _economia;
        var bytes = await Task.Run(() => gerar(r, e, DateTime.Now));
        await _exportador.CompartilharAsync(ExportadorRelatorio.NomeArquivo(r, ext), bytes, mime, "Exportar relatório");
        Log.LogInformation("Relatório exportado ({Formato}, {Bytes} bytes)", ext, bytes.Length);
    });

    [RelayCommand]
    private Task ExportarTextoAsync() => Exportar("txt", "text/plain", ExportadorRelatorio.Texto);

    [RelayCommand]
    private Task ExportarCsvAsync() => Exportar("csv", "text/csv", ExportadorRelatorio.Csv);

    private Task Exportar(string ext, string mime, Func<Relatorio, string> gerar) => ExecutarAsync(async () =>
    {
        if (_ultimo is null) return;
        await _exportador.CompartilharAsync(ExportadorRelatorio.NomeArquivo(_ultimo, ext), gerar(_ultimo), mime, "Exportar relatório");
        Log.LogInformation("Relatório exportado ({Formato})", ext);
    });

    private static void Preencher<T>(ObservableCollection<T> alvo, IEnumerable<T> itens) => Substituir(alvo, itens);
}
