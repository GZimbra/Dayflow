namespace Dayflow.Core.Domain;

/// <summary>
/// Tarefa única no banco, mesmo quando atravessa vários dias.
/// Regra de visibilidade (carry-over) espelhada em SQL em <c>ConsultasSql.FiltroVisivelNoDia</c>:
///   DataInicio &lt;= dia AND (aberta OR DataConclusao &gt;= dia) AND (não é rotina OR DataInicio = dia)
/// Instância de rotina (RotinaId != null) vale só para o próprio dia: não acumula.
/// </summary>
public sealed class Tarefa
{
    public const int TituloMax = 200;
    public const int DescricaoMax = 4000;

    public int Id { get; init; }
    public int? ProjetoId { get; private set; }
    /// <summary>Rotina que gerou esta instância. Null = tarefa avulsa (com carry-over).</summary>
    public int? RotinaId { get; private set; }
    public string Titulo { get; private set; } = "";
    public string? Descricao { get; private set; }
    public DateOnly DataInicio { get; private set; }
    public DateOnly? DataFimPrevista { get; private set; }
    public StatusTarefa Status { get; private set; }
    /// <summary>Dia civil de encerramento (Concluida ou Cancelada). Null se aberta.</summary>
    public DateOnly? DataConclusao { get; private set; }
    public DateTime CriadoEm { get; private set; }
    public DateTime AtualizadoEm { get; private set; }

    private Tarefa() { }

    public static Tarefa Nova(string titulo, string? descricao, int? projetoId, DateOnly dataInicio,
        DateOnly? dataFimPrevista, DateTime agoraUtc)
    {
        GarantirUtc(agoraUtc);
        var t = new Tarefa { Status = StatusTarefa.Pendente, CriadoEm = agoraUtc, AtualizadoEm = agoraUtc };
        t.AplicarDados(titulo, descricao, projetoId, dataInicio, dataFimPrevista);
        return t;
    }

    /// <summary>Reidratação a partir do banco. Não valida transições.</summary>
    /// <summary>Instância diária de uma rotina.</summary>
    public static Tarefa DeRotina(Rotina rotina, DateOnly dia, DateTime agoraUtc)
    {
        ArgumentNullException.ThrowIfNull(rotina);
        var t = Nova(rotina.Titulo, rotina.Descricao, rotina.ProjetoId, dia, dia, agoraUtc);
        t.RotinaId = rotina.Id;
        return t;
    }

    public static Tarefa Reidratar(int id, int? projetoId, int? rotinaId, string titulo, string? descricao, DateOnly dataInicio,
        DateOnly? dataFimPrevista, StatusTarefa status, DateOnly? dataConclusao, DateTime criadoEm, DateTime atualizadoEm) =>
        new()
        {
            Id = id, ProjetoId = projetoId, RotinaId = rotinaId, Titulo = titulo, Descricao = descricao, DataInicio = dataInicio,
            DataFimPrevista = dataFimPrevista, Status = status, DataConclusao = dataConclusao,
            CriadoEm = DateTime.SpecifyKind(criadoEm, DateTimeKind.Utc),
            AtualizadoEm = DateTime.SpecifyKind(atualizadoEm, DateTimeKind.Utc),
        };

    public bool EstaAberta => Status.EhAberto();
    public bool EhRotina => RotinaId is not null;

    /// <summary>Aparece na lista do dia.</summary>
    public bool VisivelEm(DateOnly dia) =>
        DataInicio <= dia && (EstaAberta || DataConclusao >= dia) && (!EhRotina || DataInicio == dia);

    /// <summary>Ainda aberta ao fim do dia (usado na marcação do calendário).</summary>
    public bool PendenteAoFimDe(DateOnly dia) =>
        DataInicio <= dia && (EstaAberta || DataConclusao > dia) && (!EhRotina || DataInicio == dia);

    public bool AtrasadaEm(DateOnly dia) =>
        EstaAberta && DataFimPrevista is { } fim && fim < dia;

    /// <summary>
    /// Edição de dados. <paramref name="primeiroDiaComRegistro"/> impede mover DataInicio para depois
    /// de um dia já trabalhado; <paramref name="ultimoDiaComRegistro"/> idem para a conclusão.
    /// </summary>
    public void Editar(string titulo, string? descricao, int? projetoId, DateOnly dataInicio, DateOnly? dataFimPrevista,
        DateOnly? primeiroDiaComRegistro, DateTime agoraUtc)
    {
        GarantirUtc(agoraUtc);
        if (EhRotina && dataInicio != DataInicio)
            throw new RegraDeNegocioException("tarefa.rotina_data", "Instância de rotina não muda de dia.");
        if (primeiroDiaComRegistro is { } p && dataInicio > p)
            throw new RegraDeNegocioException("tarefa.inicio_apos_registro",
                "Data de início não pode ser posterior a um dia com tempo registrado.");
        if (DataConclusao is { } c && dataInicio > c)
            throw new RegraDeNegocioException("tarefa.inicio_apos_conclusao",
                "Data de início não pode ser posterior à data de encerramento.");
        if (EhRotina) dataFimPrevista = DataInicio;
        AplicarDados(titulo, descricao, projetoId, dataInicio, dataFimPrevista);
        AtualizadoEm = agoraUtc;
    }

    /// <summary>
    /// Marca EmAndamento. Se a tarefa estava agendada para o futuro, puxa DataInicio para hoje
    /// (começar a trabalhar nela hoje significa que ela começou hoje).
    /// </summary>
    public void Iniciar(DateOnly hoje, DateTime agoraUtc)
    {
        GarantirUtc(agoraUtc);
        if (!EstaAberta)
            throw new RegraDeNegocioException("tarefa.encerrada", "Tarefa encerrada. Reabra antes de iniciar.");
        if (DataInicio > hoje)
        {
            if (EhRotina) throw new RegraDeNegocioException("tarefa.rotina_futura", "Rotina de dia futuro não pode ser iniciada.");
            DataInicio = hoje;
        }
        if (DataFimPrevista is { } f && f < DataInicio) DataFimPrevista = DataInicio;
        Status = StatusTarefa.EmAndamento;
        AtualizadoEm = agoraUtc;
    }

    public void VoltarParaPendente(DateTime agoraUtc)
    {
        GarantirUtc(agoraUtc);
        if (Status != StatusTarefa.EmAndamento)
            throw new RegraDeNegocioException("tarefa.transicao", "Só tarefas em andamento podem voltar a pendente.");
        Status = StatusTarefa.Pendente;
        AtualizadoEm = agoraUtc;
    }

    public void Concluir(DateOnly hoje, DateOnly? ultimoDiaComRegistro, DateTime agoraUtc) =>
        Encerrar(StatusTarefa.Concluida, hoje, ultimoDiaComRegistro, agoraUtc);

    public void Cancelar(DateOnly hoje, DateOnly? ultimoDiaComRegistro, DateTime agoraUtc) =>
        Encerrar(StatusTarefa.Cancelada, hoje, ultimoDiaComRegistro, agoraUtc);

    public void Reabrir(DateTime agoraUtc)
    {
        GarantirUtc(agoraUtc);
        if (EstaAberta)
            throw new RegraDeNegocioException("tarefa.transicao", "Tarefa já está aberta.");
        Status = StatusTarefa.Pendente;
        DataConclusao = null;
        AtualizadoEm = agoraUtc;
    }

    private void Encerrar(StatusTarefa final, DateOnly hoje, DateOnly? ultimoDiaComRegistro, DateTime agoraUtc)
    {
        GarantirUtc(agoraUtc);
        if (!EstaAberta)
            throw new RegraDeNegocioException("tarefa.transicao", "Tarefa já está encerrada.");
        // Encerrar hoje uma tarefa agendada para o futuro: ela passa a começar hoje.
        if (DataInicio > hoje) DataInicio = hoje;
        if (DataFimPrevista is { } f && f < DataInicio) DataFimPrevista = DataInicio;
        // Registro em dia futuro não deveria existir (lançamento manual bloqueia), mas protege o CHECK.
        var dia = ultimoDiaComRegistro is { } u && u > hoje ? u : hoje;
        Status = final;
        DataConclusao = dia;
        AtualizadoEm = agoraUtc;
    }

    private void AplicarDados(string titulo, string? descricao, int? projetoId, DateOnly dataInicio, DateOnly? dataFimPrevista)
    {
        var t = (titulo ?? "").Trim();
        if (t.Length is 0 or > TituloMax)
            throw new RegraDeNegocioException("tarefa.titulo", $"Título deve ter de 1 a {TituloMax} caracteres.");
        var d = string.IsNullOrWhiteSpace(descricao) ? null : descricao.Trim();
        if (d is { Length: > DescricaoMax })
            throw new RegraDeNegocioException("tarefa.descricao", $"Descrição excede {DescricaoMax} caracteres.");
        if (dataFimPrevista is { } fim && fim < dataInicio)
            throw new RegraDeNegocioException("tarefa.fim_prevista", "Data fim prevista anterior à data de início.");
        if (projetoId is <= 0)
            throw new RegraDeNegocioException("tarefa.projeto", "Projeto inválido.");

        Titulo = t;
        Descricao = d;
        ProjetoId = projetoId;
        DataInicio = dataInicio;
        DataFimPrevista = dataFimPrevista;
    }

    private static void GarantirUtc(DateTime dt)
    {
        if (dt.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Timestamp deve ser UTC.", nameof(dt));
    }
}
