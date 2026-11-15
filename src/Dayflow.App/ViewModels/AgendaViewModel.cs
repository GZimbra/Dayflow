using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Dayflow.App.Infra;
using Dayflow.App.Views;
using Dayflow.Core.Domain;
using Dayflow.Core.Services;
using Microsoft.Extensions.Logging;

namespace Dayflow.App.ViewModels;

public sealed partial class DiaCalendarioVm : ObservableObject
{
    [ObservableProperty] private DateOnly _data;
    [ObservableProperty] private bool _doMes;
    [ObservableProperty] private bool _ehHoje;
    public string Numero => Data.Day.ToString(Fmt.PtBr);
    partial void OnDataChanged(DateOnly value) => OnPropertyChanged(nameof(Numero));
    [ObservableProperty] private bool _selecionado;
    [ObservableProperty] private int _pendentes;
    public bool TemPendencia => Pendentes > 0;
    partial void OnPendentesChanged(int value) => OnPropertyChanged(nameof(TemPendencia));
}

public sealed partial class ItemTarefaVm : ObservableObject
{
    public ItemTarefaVm(ItemTarefaDia i, DateOnly dia)
    {
        Item = i;
        Dia = dia;
        Rodando = i.CronometroRodando;
    }

    public ItemTarefaDia Item { get; }
    public DateOnly Dia { get; }
    public Tarefa Tarefa => Item.Tarefa;
    public int Id => Tarefa.Id;
    public string Titulo => Tarefa.Titulo;
    public string? Descricao => Tarefa.Descricao;
    public Color CorProjeto => Fmt.Cor(Item.ProjetoCor, "#C9C9C6");
    public string Projeto => Item.ProjetoNome ?? Rotulos.SemProjeto;
    public bool Concluida => Tarefa.Status == StatusTarefa.Concluida;
    public bool Cancelada => Tarefa.Status == StatusTarefa.Cancelada;
    public bool Aberta => Tarefa.EstaAberta;
    public bool EhRotina => Tarefa.EhRotina;
    public bool Atrasada => Item.Atrasada;
    public bool MultiDia => !Tarefa.EhRotina && Tarefa.DataInicio < Dia;
    public string DiaDaTarefa => $"Dia {Dia.DayNumber - Tarefa.DataInicio.DayNumber + 1}";
    public bool TemTempo => Item.MinutosNoDia > 0;
    public string TempoNoDia => Fmt.Duracao(Item.MinutosNoDia);
    public string Prazo => Tarefa.DataFimPrevista is { } f && !Tarefa.EhRotina ? $"até {Fmt.Curto(f)}" : "";
    public bool TemPrazo => Prazo.Length > 0;
    public TextDecorations Decoracao => Aberta ? TextDecorations.None : TextDecorations.Strikethrough;
    public bool TemHorario => Item.TemHorario;
    public int InicioMin => Item.InicioPlanejadoMin ?? -1;
    public int DuracaoMin => Item.DuracaoPlanejadaMin ?? ServicoHorarios.DuracaoPadraoMin;
    public string Horario => Item.InicioPlanejadoMin is { } i
        ? $"{ServicoHorarios.Formatar(i)}–{ServicoHorarios.Formatar(Math.Min(1440, i + DuracaoMin))}"
        : "";
    public double Opacidade => Aberta ? 1 : 0.55;

    [ObservableProperty] private bool _rodando;
}

/// <summary>Uma linha da visão hora a hora.</summary>
public sealed class HoraVm(int hora, IReadOnlyList<ItemTarefaVm> itens, bool agora, bool passada)
{
    public int Hora { get; } = hora;
    public string Rotulo => $"{Hora:00}:00";
    public IReadOnlyList<ItemTarefaVm> Itens { get; } = itens;
    public bool Vazia => Itens.Count == 0;
    public bool Agora { get; } = agora;
    public double Opacidade => passada ? 0.6 : 1;
}

/// <summary>Número da semana ISO de uma linha do calendário (tocar seleciona a segunda-feira).</summary>
public sealed partial class SemanaVm : ObservableObject
{
    [ObservableProperty] private int _numero;
    [ObservableProperty] private DateOnly _segunda;
    [ObservableProperty] private bool _atual;
}

public sealed partial class AgendaViewModel : VmBase, IRecipient<DadosAlterados>
{
    private readonly ServicoTarefas _tarefas;
    private readonly ServicoCronometro _cronometro;
    private readonly ServicoRelatorios _relatorios;
    private readonly ServicoRotinas _rotinas;
    private readonly IRelogioCivil _relogio;
    private readonly INavegacao _nav;
    private readonly ServicoHorarios _horarios;
    private IDispatcherTimer? _tique;
    private DateOnly _hojeVisto;
    private EstadoCronometro? _estado;

    public AgendaViewModel(ServicoTarefas tarefas, ServicoCronometro cronometro, ServicoRelatorios relatorios,
        ServicoRotinas rotinas, ServicoHorarios horarios, IRelogioCivil relogio, INavegacao nav, IDialogos dialogos,
        ILogger<AgendaViewModel> log)
        : base(dialogos, log)
    {
        _horarios = horarios;
        _tarefas = tarefas;
        _cronometro = cronometro;
        _relatorios = relatorios;
        _rotinas = rotinas;
        _relogio = relogio;
        _nav = nav;
        _hojeVisto = relogio.Hoje;
        DiaSelecionado = _hojeVisto;
        MesAtual = new DateOnly(_hojeVisto.Year, _hojeVisto.Month, 1);
        CronometroTempo = "00:00:00";
        CronometroTarefa = "";
        WeakReferenceMessenger.Default.Register(this);
    }

    public ObservableCollection<DiaCalendarioVm> Dias { get; } = new ColecaoObservavel<DiaCalendarioVm>();
    public ObservableCollection<ItemTarefaVm> Tarefas { get; } = new ColecaoObservavel<ItemTarefaVm>();
    public ObservableCollection<string> RotinasPrevistas { get; } = new ColecaoObservavel<string>();
    public ObservableCollection<HoraVm> Horas { get; } = new ColecaoObservavel<HoraVm>();
    public ObservableCollection<SemanaVm> Semanas { get; } = new ColecaoObservavel<SemanaVm>();

    /// <summary>Os 7 dias da semana do dia selecionado (mesmos objetos de <see cref="Dias"/>): faixa do calendário recolhido.</summary>
    public ObservableCollection<DiaCalendarioVm> DiasDaSemana { get; } = new ColecaoObservavel<DiaCalendarioVm>();

    /// <summary>Celular em pé: calendário mostra só a semana do dia selecionado (mais espaço para a lista do dia).</summary>
    [ObservableProperty] private bool _calendarioRecolhido;
    public bool MesExpandido => !CalendarioRecolhido;
    public string RotuloAlternarCalendario => CalendarioRecolhido ? "Ver mês" : "Só a semana";
    partial void OnCalendarioRecolhidoChanged(bool value)
    {
        OnPropertyChanged(nameof(MesExpandido));
        OnPropertyChanged(nameof(RotuloAlternarCalendario));
        AtualizarFaixaSemana();
    }

    [RelayCommand]
    private void AlternarCalendario() => CalendarioRecolhido = !CalendarioRecolhido;

    private void AtualizarFaixaSemana()
    {
        if (Dias.Count < 42) return;
        var idx = -1;
        for (var i = 0; i < Dias.Count; i++)
            if (Dias[i].Data == DiaSelecionado) { idx = i; break; }
        if (idx < 0) // mês trocado pelas setas sem escolher dia: primeira semana do mês
            for (var i = 0; i < Dias.Count; i++)
                if (Dias[i].DoMes) { idx = i; break; }
        var ini = Math.Max(0, idx) / 7 * 7;
        if (DiasDaSemana.Count == 7 && ReferenceEquals(DiasDaSemana[0], Dias[ini])) return; // mesma linha
        Substituir(DiasDaSemana, Dias.Skip(ini).Take(7));
    }

    /// <summary>Lista (padrão) ou visão hora a hora do dia.</summary>
    [ObservableProperty] private bool _modoHora;
    public bool ModoLista => !ModoHora;
    partial void OnModoHoraChanged(bool value)
    {
        OnPropertyChanged(nameof(ModoLista));
        if (value) MontarHorasSeNecessario();
    }
    public int SemanaIso => System.Globalization.ISOWeek.GetWeekOfYear(DiaSelecionado.ToDateTime(TimeOnly.MinValue));
    public string SemTitulo => $"Semana {SemanaIso}";

    [RelayCommand]
    private void AlternarModo(string? modo) => ModoHora = modo == "hora";
    public IReadOnlyList<string> CabecalhoSemana { get; } = ["S", "T", "Q", "Q", "S", "S", "D"];

    [ObservableProperty] private DateOnly _mesAtual;
    [ObservableProperty] private DateOnly _diaSelecionado;
    [ObservableProperty] private bool _cronometroAtivo;
    [ObservableProperty] private string _cronometroTarefa;
    [ObservableProperty] private string _cronometroTempo;
    [ObservableProperty] private string _resumoTempo = "0 min";
    [ObservableProperty] private int _resumoAbertas;
    [ObservableProperty] private int _resumoConcluidas;
    [ObservableProperty] private bool _listaVazia;

    public string TituloMes => Fmt.MesAno(MesAtual);
    public string TituloDia => Fmt.DiaExtenso(DiaSelecionado);
    public string SemanaDia => DiaSelecionado == _relogio.Hoje ? $"Hoje · {Fmt.DiaSemana(DiaSelecionado)}" : Fmt.DiaSemana(DiaSelecionado);
    public bool EhFuturo => DiaSelecionado > _relogio.Hoje;
    public bool TemPrevistas => RotinasPrevistas.Count > 0;

    partial void OnMesAtualChanged(DateOnly value) => OnPropertyChanged(nameof(TituloMes));
    partial void OnDiaSelecionadoChanged(DateOnly value)
    {
        OnPropertyChanged(nameof(TituloDia));
        OnPropertyChanged(nameof(SemanaDia));
        OnPropertyChanged(nameof(EhFuturo));
        OnPropertyChanged(nameof(SemanaIso));
        OnPropertyChanged(nameof(SemTitulo));
        foreach (var d in Dias) d.Selecionado = d.Data == value;
        foreach (var w in Semanas) w.Atual = value >= w.Segunda && value < w.Segunda.AddDays(7);
        AtualizarFaixaSemana();
    }

    public void Receive(DadosAlterados message) => Invalidar(RecarregarAsync);

    [RelayCommand]
    public Task CarregarAsync() => ExecutarAsync(RecarregarInternoAsync);

    private Task RecarregarAsync() => ExecutarAsync(RecarregarInternoAsync);

    private async Task RecarregarInternoAsync()
    {
        await CarregarDiaInternoAsync();
        await CarregarCalendarioInternoAsync();
        await AtualizarCronometroAsync();
    }

    private async Task CarregarCalendarioInternoAsync()
    {
        var g = NovaGeracao("cal");
        var mes = MesAtual;
        var cal = await _relatorios.CalendarioAsync(mes.Year, mes.Month);
        if (!EhAtual(g, "cal")) return;
        var pend = cal.ToDictionary(c => c.Dia, c => c.Pendentes);
        var hoje = _relogio.Hoje;
        var primeiro = mes.AddDays(-(((int)mes.DayOfWeek + 6) % 7));
        // Grade fixa de 42 células: atualiza no lugar (sem recriar itens => sem piscar/reciclar errado ao girar a tela).
        while (Dias.Count < 42) Dias.Add(new DiaCalendarioVm());
        for (var i = 0; i < 42; i++)
        {
            var d = primeiro.AddDays(i);
            var c = Dias[i];
            c.Data = d;
            c.DoMes = d.Month == mes.Month;
            c.EhHoje = d == hoje;
            c.Selecionado = d == DiaSelecionado;
            c.Pendentes = pend.GetValueOrDefault(d);
        }
        while (Semanas.Count < 6) Semanas.Add(new SemanaVm());
        for (var w = 0; w < 6; w++)
        {
            var seg = primeiro.AddDays(7 * w);
            Semanas[w].Segunda = seg;
            Semanas[w].Numero = System.Globalization.ISOWeek.GetWeekOfYear(seg.ToDateTime(TimeOnly.MinValue));
            Semanas[w].Atual = DiaSelecionado >= seg && DiaSelecionado < seg.AddDays(7);
        }
        // Mesmos objetos, datas novas: força a faixa a reavaliar a linha.
        Substituir(DiasDaSemana, []);
        AtualizarFaixaSemana();
    }

    private async Task CarregarDiaInternoAsync()
    {
        var g = NovaGeracao("dia");
        var dia = DiaSelecionado;
        var itens = await _tarefas.DoDiaAsync(dia);
        var previstas = await _rotinas.PrevistasAsync(dia);
        if (!EhAtual(g, "dia")) return;
        // Nada mudou no dia (ex.: só o cronômetro pausou): não recria os cartões, evita re-layout da lista inteira.
        var chave = string.Join('|', itens.Select(i => (i.Tarefa.Id, i.Tarefa.Status, i.Tarefa.AtualizadoEm.Ticks, i.ProjetoNome,
            i.ProjetoCor, i.MinutosNoDia, i.Atrasada, i.InicioPlanejadoMin, i.DuracaoPlanejadaMin)))
            + "#" + dia.DayNumber + "#" + string.Join('|', previstas.Select(r => r.Titulo));
        var mudou = chave != _chaveDia;
        _chaveDia = chave;
        if (mudou)
        {
            Substituir(Tarefas, itens.Select(i => new ItemTarefaVm(i, dia)));
            Substituir(RotinasPrevistas, previstas.Select(r => r.Titulo));
            _horasSujas = true;
        }
        foreach (var t in Tarefas) t.Rodando = t.Id == _estado?.TarefaId;
        if (ModoHora) MontarHorasSeNecessario();
        OnPropertyChanged(nameof(TemPrevistas));

        ResumoTempo = Fmt.Duracao(itens.Sum(i => i.MinutosNoDia));
        ResumoAbertas = itens.Count(i => i.Tarefa.EstaAberta);
        ResumoConcluidas = itens.Count(i => i.Tarefa.Status == StatusTarefa.Concluida && i.Tarefa.DataConclusao == dia);
        ListaVazia = Tarefas.Count == 0 && RotinasPrevistas.Count == 0;
        OnPropertyChanged(nameof(SemHorarioResumo));
    }

    private async Task AtualizarCronometroAsync()
    {
        var g = NovaGeracao("cron");
        var estado = await _cronometro.EstadoAsync();
        if (!EhAtual(g, "cron")) return;
        _estado = estado;
        CronometroAtivo = _estado is not null;
        if (_estado is not null)
        {
            CronometroTarefa = Tarefas.FirstOrDefault(t => t.Id == _estado.TarefaId)?.Titulo
                ?? (await _tarefas.ObterAsync(_estado.TarefaId)).Titulo;
            IniciarTique();
        }
        else
        {
            _tique?.Stop();
            CronometroTempo = "00:00:00";
        }
        foreach (var t in Tarefas) t.Rodando = t.Id == _estado?.TarefaId;
        Tique();
    }

    private void IniciarTique()
    {
        if (_tique is null)
        {
            _tique = Application.Current!.Dispatcher.CreateTimer();
            _tique.Interval = TimeSpan.FromSeconds(1);
            _tique.Tick += (_, _) => Tique();
        }
        if (!_tique.IsRunning) _tique.Start();
    }

    // Exibição apenas: a verdade é CronometroAtivo.InicioUtc no banco.
    private void Tique()
    {
        if (_estado is not null)
        {
            var dec = _relogio.AgoraUtc - _estado.InicioUtc;
            CronometroTempo = Fmt.Relogio(dec < TimeSpan.Zero ? TimeSpan.Zero : dec);
        }
        if (_relogio.Hoje != _hojeVisto)
        {
            // Virou o dia com o app aberto: recarrega (rotinas do novo dia, calendário, "hoje").
            var eraHoje = DiaSelecionado == _hojeVisto;
            _hojeVisto = _relogio.Hoje;
            if (eraHoje) DiaSelecionado = _hojeVisto;
            _ = RecarregarAsync();
        }
    }

    public void PararTique() => _tique?.Stop();

    /// <summary>Volta à tela sem dados novos: só religa o relógio do cronômetro e confere a virada do dia.</summary>
    public void Retomar()
    {
        if (_estado is not null) IniciarTique();
        Tique();
    }

    // ===== Visão hora a hora =====

    private string? _chaveDia;
    private bool _horasSujas = true;
    private (DateOnly, int) _horasMontadasEm;

    /// <summary>A grade de 24 horas só é montada quando visível e quando o dia ou a hora atual mudaram.</summary>
    private void MontarHorasSeNecessario()
    {
        var agora = TimeZoneInfo.ConvertTimeFromUtc(_relogio.AgoraUtc, _relogio.Fuso);
        var marca = (DiaSelecionado, agora.Hour);
        if (!_horasSujas && marca == _horasMontadasEm && Horas.Count == 24) return;
        _horasSujas = false;
        _horasMontadasEm = marca;
        MontarHoras();
    }

    private void MontarHoras()
    {
        var agora = TimeZoneInfo.ConvertTimeFromUtc(_relogio.AgoraUtc, _relogio.Fuso);
        var ehHoje = DiaSelecionado == _relogio.Hoje;
        var porHora = Tarefas.Where(t => t.TemHorario).ToLookup(t => t.InicioMin / 60);
        Substituir(Horas, Enumerable.Range(0, 24).Select(h => new HoraVm(h,
            porHora[h].OrderBy(t => t.InicioMin).ToList(),
            ehHoje && agora.Hour == h,
            DiaSelecionado < _relogio.Hoje || (ehHoje && h < agora.Hour))));
    }

    public string SemHorarioResumo => Tarefas.Count(t => t.Aberta && !t.TemHorario) is var n and > 0
        ? $"{n} tarefa(s) sem horário: toque numa hora para encaixar" : "Todas as tarefas abertas têm horário";

    /// <summary>Toque numa hora: encaixa uma tarefa do dia (sem horário primeiro) ou cria uma nova nesse horário.</summary>
    [RelayCommand]
    private async Task ToqueHoraAsync(HoraVm? h)
    {
        if (h is null) return;
        const string nova = "+ Nova tarefa neste horário";
        var semHorario = Tarefas.Where(t => t.Aberta && !t.TemHorario).ToList();
        var comHorario = Tarefas.Where(t => t.Aberta && t.TemHorario && t.InicioMin / 60 != h.Hora).ToList();
        var opcoes = new List<string> { nova };
        opcoes.AddRange(semHorario.Select(t => t.Titulo));
        opcoes.AddRange(comHorario.Select(t => $"{t.Titulo} (mover de {ServicoHorarios.Formatar(t.InicioMin)})"));
        var r = await Dialogos.EscolherAsync($"{h.Rotulo} · o que você vai fazer?", null, [.. opcoes]);
        if (r is null) return;
        if (r == nova)
        {
            await _nav.AbrirAsync<EditorTarefaPage, EditorTarefaViewModel>(vm => vm.InicializarAsync(null, DiaSelecionado, h.Hora * 60));
            return;
        }
        var idx = opcoes.IndexOf(r) - 1;
        var alvo = idx < semHorario.Count ? semHorario[idx] : comHorario[idx - semHorario.Count];
        var dur = alvo.TemHorario ? alvo.DuracaoMin : ServicoHorarios.DuracaoPadraoMin;
        await DefinirHorarioAsync(alvo, h.Hora * 60, dur);
    }

    /// <summary>Toque numa atividade já encaixada.</summary>
    [RelayCommand]
    private async Task ToqueItemHoraAsync(ItemTarefaVm? t)
    {
        if (t is null) return;
        var opcoes = new List<string> { "Mudar horário", "Duração" };
        if (t.Aberta) opcoes.Add(t.Rodando ? "Pausar cronômetro" : "Iniciar cronômetro");
        if (t.Aberta) opcoes.Add("Concluir");
        opcoes.Add("Editar");
        opcoes.Add("Tirar do horário");
        var r = await Dialogos.EscolherAsync($"{t.Titulo} · {t.Horario}", null, [.. opcoes]);
        switch (r)
        {
            case "Mudar horário":
                if (await EscolherInicioAsync(t.InicioMin) is { } ini) await DefinirHorarioAsync(t, ini, t.DuracaoMin);
                break;
            case "Duração":
                var d = await Dialogos.EscolherAsync("Duração", null, [.. EditorTarefaViewModel.Duracoes.Select(x => x.Nome)]);
                if (EditorTarefaViewModel.Duracoes.FirstOrDefault(x => x.Nome == d) is { } dur)
                    await DefinirHorarioAsync(t, t.InicioMin, Math.Min(dur.Minutos, 1440 - t.InicioMin));
                break;
            case "Iniciar cronômetro":
            case "Pausar cronômetro":
                await AlternarCronometroAsync(t);
                break;
            case "Concluir":
                await AlternarConclusaoAsync(t);
                break;
            case "Editar":
                await EditarAsync(t);
                break;
            case "Tirar do horário":
                await ExecutarAsync(async () => { await _horarios.RemoverAsync(t.Id, DiaSelecionado); Avisar(); });
                break;
        }
    }

    private async Task<int?> EscolherInicioAsync(int atual)
    {
        var horas = Enumerable.Range(0, 24).Select(h => $"{h:00}:00").ToArray();
        var h = await Dialogos.EscolherAsync("Começa em qual hora?", null, horas);
        if (h is null) return null;
        var hora = int.Parse(h[..2], Fmt.PtBr);
        var m = await Dialogos.EscolherAsync($"{h[..2]}:__", null, ":00", ":15", ":30", ":45");
        var min = m is null ? atual % 60 : int.Parse(m[1..], Fmt.PtBr);
        return hora * 60 + min;
    }

    private Task DefinirHorarioAsync(ItemTarefaVm t, int inicio, int duracao) => ExecutarAsync(async () =>
    {
        await _horarios.DefinirAsync(t.Id, DiaSelecionado, inicio, Math.Max(ServicoHorarios.PassoMin, Math.Min(duracao, 1440 - inicio)));
        Avisar();
    });

    [RelayCommand]
    private Task SelecionarDiaAsync(DiaCalendarioVm? d) => d is null ? Task.CompletedTask : ExecutarAsync(async () =>
    {
        DiaSelecionado = d.Data;
        if (!d.DoMes)
        {
            MesAtual = new DateOnly(d.Data.Year, d.Data.Month, 1);
            await CarregarCalendarioInternoAsync();
        }
        await CarregarDiaInternoAsync();
        foreach (var t in Tarefas) t.Rodando = t.Id == _estado?.TarefaId;
    });

    [RelayCommand]
    private Task SelecionarSemanaAsync(SemanaVm? w) => w is null ? Task.CompletedTask : ExecutarAsync(async () =>
    {
        DiaSelecionado = w.Segunda;
        var mes = new DateOnly(w.Segunda.Year, w.Segunda.Month, 1);
        if (mes != MesAtual)
        {
            MesAtual = mes;
            await CarregarCalendarioInternoAsync();
        }
        await CarregarDiaInternoAsync();
    });

    [RelayCommand]
    private Task MesAnteriorAsync() => TrocarMes(-1);

    [RelayCommand]
    private Task MesSeguinteAsync() => TrocarMes(1);

    private Task TrocarMes(int delta) => ExecutarAsync(async () =>
    {
        MesAtual = MesAtual.AddMonths(delta);
        await CarregarCalendarioInternoAsync();
    });

    /// <summary>Setas do calendário: semana a semana quando recolhido, mês a mês quando aberto.</summary>
    [RelayCommand]
    private Task AnteriorAsync() => CalendarioRecolhido ? TrocarSemana(-1) : TrocarMes(-1);

    [RelayCommand]
    private Task SeguinteAsync() => CalendarioRecolhido ? TrocarSemana(1) : TrocarMes(1);

    private Task TrocarSemana(int delta) => ExecutarAsync(async () =>
    {
        DiaSelecionado = DiaSelecionado.AddDays(7 * delta);
        var mes = new DateOnly(DiaSelecionado.Year, DiaSelecionado.Month, 1);
        if (mes != MesAtual)
        {
            MesAtual = mes;
            await CarregarCalendarioInternoAsync();
        }
        await CarregarDiaInternoAsync();
        foreach (var t in Tarefas) t.Rodando = t.Id == _estado?.TarefaId;
    });

    [RelayCommand]
    private Task IrParaHojeAsync() => ExecutarAsync(async () =>
    {
        var hoje = _relogio.Hoje;
        MesAtual = new DateOnly(hoje.Year, hoje.Month, 1);
        DiaSelecionado = hoje;
        await RecarregarInternoAsync();
    });

    [RelayCommand]
    private Task NovaTarefaAsync() =>
        _nav.AbrirAsync<EditorTarefaPage, EditorTarefaViewModel>(vm => vm.InicializarAsync(null, DiaSelecionado));

    [RelayCommand]
    private Task EditarAsync(ItemTarefaVm? t) => t is null ? Task.CompletedTask :
        _nav.AbrirAsync<EditorTarefaPage, EditorTarefaViewModel>(vm => vm.InicializarAsync(t.Id, DiaSelecionado));

    [RelayCommand]
    private Task LancarTempoAsync(ItemTarefaVm? t) => t is null ? Task.CompletedTask :
        _nav.AbrirAsync<LancamentoTempoPage, LancamentoTempoViewModel>(vm =>
        {
            vm.Inicializar(t.Tarefa, DiaSelecionado > _relogio.Hoje ? _relogio.Hoje : DiaSelecionado);
            return Task.CompletedTask;
        });

    [RelayCommand]
    private async Task AlternarConclusaoAsync(ItemTarefaVm? t)
    {
        if (t is null) return;
        var concluiu = false;
        await ExecutarAsync(async () =>
        {
            if (t.Aberta) { await _tarefas.ConcluirAsync(t.Id); concluiu = true; }
            else await _tarefas.ReabrirAsync(t.Id);
            Avisar();
        });
        // Só tarefa de projeto recebe relatório de conclusão (regra em ServicoConclusoes.Elegivel).
        if (concluiu && t.Tarefa.ProjetoId is not null &&
            await Dialogos.ConfirmarAsync("Tarefa concluída",
                "Quer registrar um relatório de conclusão? Explique o que foi feito e quanto tempo isso poupa de outras pessoas.",
                "Registrar"))
            await AbrirConclusaoAsync(t);
    }

    private Task AbrirConclusaoAsync(ItemTarefaVm t) =>
        _nav.AbrirAsync<EditorConclusaoPage, EditorConclusaoViewModel>(vm => vm.InicializarAsync(t.Id, t.Projeto));

    [RelayCommand]
    private Task AlternarCronometroAsync(ItemTarefaVm? t) => t is null ? Task.CompletedTask : ExecutarAsync(async () =>
    {
        if (t.Rodando) await _cronometro.PausarAsync();
        else await _cronometro.IniciarAsync(t.Id);
        Avisar();
    });

    [RelayCommand]
    private Task PausarCronometroAsync() => ExecutarAsync(async () =>
    {
        await _cronometro.PausarAsync();
        Avisar();
    });

    [RelayCommand]
    private async Task MaisAcoesAsync(ItemTarefaVm? t)
    {
        if (t is null) return;
        var opcoes = new List<string> { "Editar", "Lançar tempo" };
        if (t.Concluida && t.Tarefa.ProjetoId is not null) opcoes.Add("Relatório de conclusão");
        if (t.Aberta) opcoes.Add("Cancelar tarefa"); else opcoes.Add("Reabrir");
        var excluir = t.EhRotina ? null : "Excluir";
        var r = await Dialogos.EscolherAsync(t.Titulo, excluir, [.. opcoes]);
        switch (r)
        {
            case "Editar": await EditarAsync(t); break;
            case "Lançar tempo": await LancarTempoAsync(t); break;
            case "Relatório de conclusão": await AbrirConclusaoAsync(t); break;
            case "Cancelar tarefa":
                await ExecutarAsync(async () => { await _tarefas.CancelarAsync(t.Id); Avisar(); });
                break;
            case "Reabrir":
                await ExecutarAsync(async () => { await _tarefas.ReabrirAsync(t.Id); Avisar(); });
                break;
            case "Excluir":
                if (await Dialogos.ConfirmarAsync("Excluir tarefa", "A tarefa e todo o tempo registrado nela serão apagados.", "Excluir"))
                    await ExecutarAsync(async () => { await _tarefas.ExcluirAsync(t.Id); Avisar(); });
                break;
        }
    }

    /// <summary>Chamado pela View após arrastar (CollectionView.ReorderCompleted).</summary>
    [RelayCommand]
    private Task ReordenarAsync() => ExecutarAsync(async () =>
    {
        await _tarefas.ReordenarAsync(DiaSelecionado, Tarefas.Select(t => t.Id).ToList());
    });

    private static void Avisar() => WeakReferenceMessenger.Default.Send(new DadosAlterados("agenda"));
}
