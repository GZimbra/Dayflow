using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dayflow.App.Infra;
using Dayflow.Core.Domain;
using Dayflow.Core.Infra;
using Dayflow.Core.Services;
using Microsoft.Extensions.Logging;

namespace Dayflow.App.ViewModels;

public sealed partial class AjustesViewModel : VmBase
{
    private readonly IConfiguracoes _cfg;
    private readonly IAgendadorLembretes _agendador;
    private readonly IExportador _exportador;
    private readonly LogArquivoProvider _logs;
    private bool _carregando;

    public AjustesViewModel(IConfiguracoes cfg, IAgendadorLembretes agendador, IExportador exportador, LogArquivoProvider logs,
        IDialogos d, ILogger<AjustesViewModel> log) : base(d, log)
    {
        _cfg = cfg;
        _agendador = agendador;
        _exportador = exportador;
        _logs = logs;
        Versao = $"Dayflow {AppInfo.Current.VersionString} ({AppInfo.Current.BuildString})";
    }

    public IReadOnlyList<int> Horas { get; } = Enumerable.Range(0, 24).ToList();
    public ObservableCollection<DiaOpcao> Dias { get; } = new ColecaoObservavel<DiaOpcao>();
    public string Versao { get; }

    [ObservableProperty] private bool _lembretesAtivos;
    [ObservableProperty] private int _horaInicio;
    [ObservableProperty] private int _horaFim;
    [ObservableProperty] private string _tema = "Sistema";
    [ObservableProperty] private string _statusLembretes = "";

    public IReadOnlyList<string> Temas { get; } = ["Sistema", "Claro", "Escuro"];
    public IReadOnlyList<string> Orientacoes { get; } = ["Automática (gira)", "Sempre deitado", "Sempre em pé"];
    [ObservableProperty] private string _orientacao = "Automática (gira)";

    partial void OnOrientacaoChanged(string value)
    {
        if (_carregando) return;
        var o = (OrientacaoTela)Math.Max(0, Orientacoes.ToList().IndexOf(value));
        _cfg.Orientacao = o;
        Infra.Orientacao.Aplicar(o);
    }

    public void Carregar()
    {
        _carregando = true;
        var c = _cfg.Lembretes;
        LembretesAtivos = c.Ativo;
        HoraInicio = c.HoraInicio;
        HoraFim = c.HoraFim;
        Dias.Clear();
        foreach (var (bit, sigla) in Fmt.DiasDaSemana) Dias.Add(new DiaOpcao(bit, sigla) { Marcado = (c.Dias & bit) != 0 });
        Tema = _cfg.Tema switch { AppTheme.Light => "Claro", AppTheme.Dark => "Escuro", _ => "Sistema" };
        Orientacao = Orientacoes[Math.Clamp((int)_cfg.Orientacao, 0, Orientacoes.Count - 1)];
        AtualizarStatus(c);
        _carregando = false;
    }

    partial void OnLembretesAtivosChanged(bool value) { if (!_carregando) _ = AplicarAsync(); }
    partial void OnHoraInicioChanged(int value) { if (!_carregando) _ = AplicarAsync(); }
    partial void OnHoraFimChanged(int value) { if (!_carregando) _ = AplicarAsync(); }

    partial void OnTemaChanged(string value)
    {
        if (_carregando) return;
        var t = value switch { "Claro" => AppTheme.Light, "Escuro" => AppTheme.Dark, _ => AppTheme.Unspecified };
        _cfg.Tema = t;
        if (Application.Current is { } app) app.UserAppTheme = t;
    }

    [RelayCommand]
    private Task AlternarDiaAsync(DiaOpcao? d)
    {
        if (d is null) return Task.CompletedTask;
        d.Marcado = !d.Marcado;
        return AplicarAsync();
    }

    private Task AplicarAsync() => ExecutarAsync(async () =>
    {
        var dias = Dias.Where(d => d.Marcado).Aggregate(DiasSemana.Nenhum, (a, d) => a | d.Bit);
        var nova = new ConfiguracaoLembretes(LembretesAtivos, HoraInicio, HoraFim, dias, _cfg.Lembretes.SilenciadoAte);
        if (nova.Ativo) nova.Validar();
        var permitido = await _agendador.AplicarAsync(nova);
        if (nova.Ativo && !permitido)
        {
            _carregando = true;
            LembretesAtivos = false;
            _carregando = false;
            nova = nova with { Ativo = false };
            await Dialogos.AlertaAsync("Notificações bloqueadas", "Permita notificações para o Dayflow nas configurações do Android.");
        }
        _cfg.Lembretes = nova;
        AtualizarStatus(nova);
    });

    private void AtualizarStatus(ConfiguracaoLembretes c)
    {
        if (!c.Ativo) { StatusLembretes = "Desativados"; return; }
        try
        {
            var prox = AgendaLembretes.Proximo(c, DateTime.UtcNow, TimeZoneInfo.Local);
            StatusLembretes = prox is { } p
                ? $"Próximo: {TimeZoneInfo.ConvertTimeFromUtc(p, TimeZoneInfo.Local).ToString("ddd HH:mm", Fmt.PtBr)}"
                : "Nenhum horário na janela";
        }
        catch (RegraDeNegocioException ex) { StatusLembretes = ex.Message; }
    }

    [RelayCommand]
    private Task ExportarLogAsync() => ExecutarAsync(() =>
        _exportador.CompartilharAsync($"dayflow-log-{DateTime.Now:yyyyMMdd-HHmm}.txt", _logs.Exportar(), "text/plain", "Exportar log"));
}
