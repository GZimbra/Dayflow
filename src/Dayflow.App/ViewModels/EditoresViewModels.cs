using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Dayflow.App.Infra;
using Dayflow.Core.Domain;
using Dayflow.Core.Services;
using Microsoft.Extensions.Logging;

namespace Dayflow.App.ViewModels;

public sealed record DuracaoOpcao(int Minutos, string Nome)
{
    public override string ToString() => Nome;
}

public sealed record ProjetoOpcao(int? Id, string Nome, Color Cor)
{
    public override string ToString() => Nome;
}

public sealed partial class EditorTarefaViewModel(
    ServicoTarefas tarefas, ServicoProjetos projetos, ServicoHorarios horarios, INavegacao nav, IDialogos dialogos,
    ILogger<EditorTarefaViewModel> log)
    : VmBase(dialogos, log)
{
    private int? _id;
    private DateOnly _dia;
    private bool _tinhaHorario;

    public static IReadOnlyList<DuracaoOpcao> Duracoes { get; } =
    [
        new(15, "15 min"), new(30, "30 min"), new(45, "45 min"), new(60, "1 h"), new(90, "1 h 30"),
        new(120, "2 h"), new(180, "3 h"), new(240, "4 h"),
    ];

    public IReadOnlyList<DuracaoOpcao> OpcoesDuracao => Duracoes;

    /// <summary>Horário planejado no dia que está aberto na Agenda (visão hora a hora).</summary>
    [ObservableProperty] private bool _temHorario;
    [ObservableProperty] private TimeSpan _horario = new(9, 0, 0);
    [ObservableProperty] private DuracaoOpcao? _duracao;
    public string RotuloDiaHorario => $"Horário em {Fmt.Curto(_dia)}";

    public ObservableCollection<ProjetoOpcao> Projetos { get; } = new ColecaoObservavel<ProjetoOpcao>();

    [ObservableProperty] private string _titulo = "";
    [ObservableProperty] private string? _descricao;
    [ObservableProperty] private ProjetoOpcao? _projeto;
    [ObservableProperty] private DateTime _dataInicio;
    [ObservableProperty] private bool _temPrazo;
    [ObservableProperty] private DateTime _dataFimPrevista;
    [ObservableProperty] private bool _ehRotina;

    public bool Editando => _id is not null;
    public string Cabecalho => Editando ? "Editar tarefa" : "Nova tarefa";

    public async Task InicializarAsync(int? id, DateOnly dia, int? inicioMin = null)
    {
        _id = id;
        _dia = dia;
        _tinhaHorario = false;
        TemHorario = inicioMin is not null;
        Horario = TimeSpan.FromMinutes(inicioMin ?? 9 * 60);
        Duracao = Duracoes.First(d => d.Minutos == 60);
        OnPropertyChanged(nameof(RotuloDiaHorario));
        var lista = await projetos.ListarAsync(incluirArquivados: id is not null);
        Substituir(Projetos, lista.Select(p => new ProjetoOpcao(p.Id, p.Ativo ? p.Nome : $"{p.Nome} (arquivado)", Fmt.Cor(p.Cor)))
            .Prepend(new ProjetoOpcao(null, Rotulos.SemProjeto, Fmt.Cor(Rotulos.CorSemProjeto))));

        if (id is { } i)
        {
            var t = await tarefas.ObterAsync(i);
            Titulo = t.Titulo;
            Descricao = t.Descricao;
            Projeto = Projetos.FirstOrDefault(p => p.Id == t.ProjetoId) ?? Projetos[0];
            DataInicio = t.DataInicio.ToDateTime(TimeOnly.MinValue);
            TemPrazo = t.DataFimPrevista is not null && !t.EhRotina;
            DataFimPrevista = (t.DataFimPrevista ?? t.DataInicio).ToDateTime(TimeOnly.MinValue);
            EhRotina = t.EhRotina;
            var doDia = (await tarefas.DoDiaAsync(dia)).FirstOrDefault(x => x.Tarefa.Id == i);
            if (doDia?.InicioPlanejadoMin is { } ini)
            {
                _tinhaHorario = true;
                TemHorario = true;
                Horario = TimeSpan.FromMinutes(ini);
                Duracao = Duracoes.FirstOrDefault(d => d.Minutos == doDia.DuracaoPlanejadaMin) ?? Duracao;
            }
        }
        else
        {
            Titulo = "";
            Descricao = null;
            Projeto = Projetos[0];
            DataInicio = dia.ToDateTime(TimeOnly.MinValue);
            TemPrazo = false;
            DataFimPrevista = DataInicio;
            EhRotina = false;
        }
        OnPropertyChanged(nameof(Editando));
        OnPropertyChanged(nameof(Cabecalho));
    }

    [RelayCommand]
    private Task SalvarAsync() => ExecutarAsync(async () =>
    {
        var dados = new DadosTarefa(Titulo, Descricao, Projeto?.Id, DateOnly.FromDateTime(DataInicio),
            TemPrazo ? DateOnly.FromDateTime(DataFimPrevista) : null);
        int tarefaId;
        if (_id is { } id) { await tarefas.EditarAsync(id, dados); tarefaId = id; }
        else tarefaId = await tarefas.CriarAsync(dados);

        // Tarefa nova: o horário vale para o dia de início. Edição: para o dia aberto na Agenda.
        var diaHorario = _id is null ? dados.DataInicio : _dia;
        if (TemHorario)
        {
            var ini = (int)Math.Round(Horario.TotalMinutes);
            var dur = Math.Min(Duracao?.Minutos ?? ServicoHorarios.DuracaoPadraoMin, 1440 - ini);
            await horarios.DefinirAsync(tarefaId, diaHorario, ini, Math.Max(ServicoHorarios.PassoMin, dur));
        }
        else if (_tinhaHorario)
        {
            await horarios.RemoverAsync(tarefaId, diaHorario);
        }
        WeakReferenceMessenger.Default.Send(new DadosAlterados("tarefa"));
        await nav.FecharAsync();
    });

    [RelayCommand]
    private Task CancelarAsync() => nav.FecharAsync();
}

public sealed partial class LancamentoTempoViewModel(
    ServicoRegistros registros, IRelogioCivil relogio, INavegacao nav, IDialogos dialogos, ILogger<LancamentoTempoViewModel> log)
    : VmBase(dialogos, log)
{
    private int _tarefaId;

    [ObservableProperty] private string _tituloTarefa = "";
    [ObservableProperty] private DateTime _dia;
    [ObservableProperty] private DateTime _diaMinimo;
    [ObservableProperty] private DateTime _diaMaximo;
    [ObservableProperty] private string _minutos = "";
    [ObservableProperty] private string? _observacao;

    public void Inicializar(Tarefa t, DateOnly dia)
    {
        ArgumentNullException.ThrowIfNull(t);
        _tarefaId = t.Id;
        TituloTarefa = t.Titulo;
        DiaMinimo = t.DataInicio.ToDateTime(TimeOnly.MinValue);
        var max = t.DataConclusao is { } c && c < relogio.Hoje ? c : relogio.Hoje;
        DiaMaximo = max.ToDateTime(TimeOnly.MinValue);
        var d = dia < t.DataInicio ? t.DataInicio : dia > max ? max : dia;
        Dia = d.ToDateTime(TimeOnly.MinValue);
        Minutos = "";
        Observacao = null;
    }

    [RelayCommand]
    private void Somar(string? valor)
    {
        _ = int.TryParse(Minutos, out var atual);
        Minutos = (atual + int.Parse(valor ?? "0", Fmt.PtBr)).ToString(Fmt.PtBr);
    }

    [RelayCommand]
    private Task SalvarAsync() => ExecutarAsync(async () =>
    {
        if (!int.TryParse(Minutos, out var m))
            throw new RegraDeNegocioException("registro.minutos", "Informe os minutos.");
        await registros.LancarManualAsync(_tarefaId, DateOnly.FromDateTime(Dia), m, Observacao);
        WeakReferenceMessenger.Default.Send(new DadosAlterados("registro"));
        await nav.FecharAsync();
    });

    [RelayCommand]
    private Task CancelarAsync() => nav.FecharAsync();
}

public sealed partial class DiaOpcao(DiasSemana bit, string sigla) : ObservableObject
{
    public DiasSemana Bit { get; } = bit;
    public string Sigla { get; } = sigla;
    [ObservableProperty] private bool _marcado;
}

public sealed partial class EditorRotinaViewModel(
    ServicoRotinas rotinas, ServicoProjetos projetos, IRelogioCivil relogio, INavegacao nav, IDialogos dialogos,
    ILogger<EditorRotinaViewModel> log) : VmBase(dialogos, log)
{
    private int? _id;

    public ObservableCollection<ProjetoOpcao> Projetos { get; } = new ColecaoObservavel<ProjetoOpcao>();
    public ObservableCollection<DiaOpcao> DiasOpcoes { get; } = new ColecaoObservavel<DiaOpcao>();

    [ObservableProperty] private string _titulo = "";
    [ObservableProperty] private string? _descricao;
    [ObservableProperty] private ProjetoOpcao? _projeto;
    [ObservableProperty] private DateTime _dataInicio;

    public bool Editando => _id is not null;
    public string Cabecalho => Editando ? "Editar rotina" : "Nova rotina";

    public async Task InicializarAsync(Rotina? r)
    {
        _id = r?.Id;
        var lista = await projetos.ListarAsync();
        Substituir(Projetos, lista.Select(p => new ProjetoOpcao(p.Id, p.Nome, Fmt.Cor(p.Cor)))
            .Prepend(new ProjetoOpcao(null, Rotulos.SemProjeto, Fmt.Cor(Rotulos.CorSemProjeto))));
        var dias = r?.Dias ?? DiasSemana.Uteis;
        DiasOpcoes.Clear();
        foreach (var (bit, sigla) in Fmt.DiasDaSemana)
            DiasOpcoes.Add(new DiaOpcao(bit, sigla) { Marcado = (dias & bit) != 0 });
        Titulo = r?.Titulo ?? "";
        Descricao = r?.Descricao;
        Projeto = Projetos.FirstOrDefault(p => p.Id == r?.ProjetoId) ?? Projetos[0];
        DataInicio = (r?.DataInicio ?? relogio.Hoje).ToDateTime(TimeOnly.MinValue);
        OnPropertyChanged(nameof(Editando));
        OnPropertyChanged(nameof(Cabecalho));
    }

    [RelayCommand]
    private void AlternarDia(DiaOpcao? d)
    {
        if (d is not null) d.Marcado = !d.Marcado;
    }

    [RelayCommand]
    private Task SalvarAsync() => ExecutarAsync(async () =>
    {
        var dias = DiasOpcoes.Where(d => d.Marcado).Aggregate(DiasSemana.Nenhum, (a, d) => a | d.Bit);
        var dados = new DadosRotina(Titulo, Descricao, Projeto?.Id, dias, DateOnly.FromDateTime(DataInicio));
        if (_id is { } id) await rotinas.EditarAsync(id, dados);
        else await rotinas.CriarAsync(dados);
        WeakReferenceMessenger.Default.Send(new DadosAlterados("rotina"));
        await nav.FecharAsync();
    });

    [RelayCommand]
    private Task CancelarAsync() => nav.FecharAsync();
}

public sealed partial class CorOpcao(string hex) : ObservableObject
{
    public string Hex { get; } = hex;
    public Color Cor { get; } = Color.FromArgb(hex);
    [ObservableProperty] private bool _selecionada;
}

public sealed partial class EditorProjetoViewModel(
    ServicoProjetos projetos, INavegacao nav, IDialogos dialogos, ILogger<EditorProjetoViewModel> log) : VmBase(dialogos, log)
{
    /// <summary>Paleta de projetos derivada da identidade (laranja/preto) + tons distinguíveis para gráficos.</summary>
    public static readonly string[] Paleta =
    [
        "#F26A1B", "#111111", "#FF9A57", "#5A5A5A", "#B84A0E", "#9C9C9C",
        "#2E7D9A", "#2E9E6A", "#C9A227", "#7A4FB0", "#D64541", "#3D5A80",
    ];

    private int? _id;
    public ObservableCollection<CorOpcao> Cores { get; } = [.. Paleta.Select(h => new CorOpcao(h))];

    [ObservableProperty] private string _nome = "";
    [ObservableProperty] private CorOpcao? _corSelecionada;

    public string Cabecalho => _id is null ? "Novo projeto" : "Editar projeto";

    public void Inicializar(Projeto? p)
    {
        _id = p?.Id;
        Nome = p?.Nome ?? "";
        var hex = p?.Cor ?? Paleta[0];
        if (Cores.All(c => !string.Equals(c.Hex, hex, StringComparison.OrdinalIgnoreCase))) Cores.Add(new CorOpcao(hex));
        Selecionar(Cores.First(c => string.Equals(c.Hex, hex, StringComparison.OrdinalIgnoreCase)));
        OnPropertyChanged(nameof(Cabecalho));
    }

    [RelayCommand]
    private void Selecionar(CorOpcao? c)
    {
        if (c is null) return;
        foreach (var o in Cores) o.Selecionada = o == c;
        CorSelecionada = c;
    }

    [RelayCommand]
    private Task SalvarAsync() => ExecutarAsync(async () =>
    {
        var cor = CorSelecionada?.Hex ?? Paleta[0];
        if (_id is { } id) await projetos.EditarAsync(id, Nome, cor);
        else await projetos.CriarAsync(Nome, cor);
        WeakReferenceMessenger.Default.Send(new DadosAlterados("projeto"));
        await nav.FecharAsync();
    });

    [RelayCommand]
    private Task CancelarAsync() => nav.FecharAsync();
}
