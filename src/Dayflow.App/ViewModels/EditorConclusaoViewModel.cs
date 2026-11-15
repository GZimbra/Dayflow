using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Dayflow.App.Infra;
using Dayflow.Core.Domain;
using Dayflow.Core.Services;
using Microsoft.Extensions.Logging;

namespace Dayflow.App.ViewModels;

public sealed record FrequenciaOpcao(FrequenciaEconomia Valor, string Nome)
{
    public override string ToString() => Nome;
}

/// <summary>Relatório de conclusão: o que foi feito + tempo economizado informado (min × pessoas × frequência).</summary>
public sealed partial class EditorConclusaoViewModel(
    ServicoConclusoes conclusoes, ServicoTarefas tarefas, INavegacao nav, IDialogos dialogos, ILogger<EditorConclusaoViewModel> log)
    : VmBase(dialogos, log)
{
    private int _tarefaId;

    public IReadOnlyList<FrequenciaOpcao> Frequencias { get; } =
    [
        new(FrequenciaEconomia.UnicaVez, "Uma única vez"),
        new(FrequenciaEconomia.PorDia, "Por dia útil"),
        new(FrequenciaEconomia.PorSemana, "Por semana"),
        new(FrequenciaEconomia.PorMes, "Por mês"),
    ];

    [ObservableProperty] private string _tituloTarefa = "";
    [ObservableProperty] private string _projeto = "";
    [ObservableProperty] private string? _resumo;
    [ObservableProperty] private string? _beneficiados;
    [ObservableProperty] private string _minutos = "";
    [ObservableProperty] private string _pessoas = "1";
    [ObservableProperty] private FrequenciaOpcao? _frequencia;
    [ObservableProperty] private bool _existente;

    public string Previa
    {
        get
        {
            if (!int.TryParse(Minutos, out var m) || m <= 0 || !int.TryParse(Pessoas, out var p) || p <= 0)
                return "Informe minutos e pessoas para ver a estimativa.";
            var porVez = (long)m * p;
            var f = RelatorioConclusao.FatorMensal(Frequencia?.Valor ?? FrequenciaEconomia.UnicaVez);
            return f is { } fator
                ? $"{Fmt.Duracao((int)Math.Min(int.MaxValue, porVez))} por ocorrência ≈ {Fmt.Duracao((int)Math.Min(int.MaxValue, Math.Round(porVez * fator)))} por mês"
                : $"{Fmt.Duracao((int)Math.Min(int.MaxValue, porVez))} economizados uma vez";
        }
    }

    partial void OnMinutosChanged(string value) => OnPropertyChanged(nameof(Previa));
    partial void OnPessoasChanged(string value) => OnPropertyChanged(nameof(Previa));
    partial void OnFrequenciaChanged(FrequenciaOpcao? value) => OnPropertyChanged(nameof(Previa));

    public async Task InicializarAsync(int tarefaId, string projeto)
    {
        _tarefaId = tarefaId;
        Projeto = projeto;
        var t = await tarefas.ObterAsync(tarefaId);
        TituloTarefa = t.Titulo;
        var r = await conclusoes.ObterAsync(tarefaId);
        Existente = r is not null;
        Resumo = r?.Resumo;
        Beneficiados = r?.Beneficiados;
        Minutos = r is null ? "" : r.MinutosEconomizados.ToString(Fmt.PtBr);
        Pessoas = (r?.Pessoas ?? 1).ToString(Fmt.PtBr);
        Frequencia = Frequencias.First(f => f.Valor == (r?.Frequencia ?? FrequenciaEconomia.PorDia));
    }

    [RelayCommand]
    private Task SalvarAsync() => ExecutarAsync(async () =>
    {
        var m = string.IsNullOrWhiteSpace(Minutos) ? 0 : int.TryParse(Minutos, out var v) ? v
            : throw new RegraDeNegocioException("conclusao.minutos", "Minutos inválidos.");
        if (!int.TryParse(Pessoas, out var p))
            throw new RegraDeNegocioException("conclusao.pessoas", "Informe quantas pessoas se beneficiam.");
        await conclusoes.SalvarAsync(_tarefaId, Resumo, Beneficiados, m, Frequencia?.Valor ?? FrequenciaEconomia.UnicaVez, p);
        WeakReferenceMessenger.Default.Send(new DadosAlterados("conclusao"));
        await nav.FecharAsync();
    });

    [RelayCommand]
    private async Task RemoverAsync()
    {
        if (!await Dialogos.ConfirmarAsync("Remover relatório", "O relatório de conclusão desta tarefa será apagado.", "Remover")) return;
        await ExecutarAsync(async () =>
        {
            await conclusoes.RemoverAsync(_tarefaId);
            WeakReferenceMessenger.Default.Send(new DadosAlterados("conclusao"));
            await nav.FecharAsync();
        });
    }

    [RelayCommand]
    private Task CancelarAsync() => nav.FecharAsync();
}
