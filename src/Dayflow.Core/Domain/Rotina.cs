namespace Dayflow.Core.Domain;

[Flags]
public enum DiasSemana
{
    Nenhum = 0,
    Segunda = 1 << 0,
    Terca = 1 << 1,
    Quarta = 1 << 2,
    Quinta = 1 << 3,
    Sexta = 1 << 4,
    Sabado = 1 << 5,
    Domingo = 1 << 6,
    Uteis = Segunda | Terca | Quarta | Quinta | Sexta,
    Todos = Uteis | Sabado | Domingo,
}

public static class DiasSemanaExtensions
{
    /// <summary>Bit do dia (segunda = bit 0). Mesmo mapeamento usado no SQL.</summary>
    public static DiasSemana Bit(this DateOnly d) => (DiasSemana)(1 << (((int)d.DayOfWeek + 6) % 7));
}

/// <summary>
/// Modelo de atividade recorrente. Cada dia em que ocorre gera UMA Tarefa (Tarefa.RotinaId),
/// que não passa para o dia seguinte. A rotina em si nunca aparece na lista.
/// </summary>
public sealed class Rotina
{
    public int Id { get; init; }
    public string Titulo { get; private set; } = "";
    public string? Descricao { get; private set; }
    public int? ProjetoId { get; private set; }
    public DiasSemana Dias { get; private set; }
    public bool Ativa { get; private set; }
    public DateOnly DataInicio { get; private set; }
    public DateTime CriadoEm { get; private set; }
    public DateTime AtualizadoEm { get; private set; }

    private Rotina() { }

    public static Rotina Nova(string titulo, string? descricao, int? projetoId, DiasSemana dias, DateOnly dataInicio, DateTime agoraUtc)
    {
        var r = new Rotina { Ativa = true, CriadoEm = agoraUtc, AtualizadoEm = agoraUtc };
        r.Aplicar(titulo, descricao, projetoId, dias, dataInicio);
        return r;
    }

    public static Rotina Reidratar(int id, string titulo, string? descricao, int? projetoId, DiasSemana dias, bool ativa,
        DateOnly dataInicio, DateTime criadoEm, DateTime atualizadoEm) => new()
    {
        Id = id, Titulo = titulo, Descricao = descricao, ProjetoId = projetoId, Dias = dias, Ativa = ativa,
        DataInicio = dataInicio, CriadoEm = DateTime.SpecifyKind(criadoEm, DateTimeKind.Utc),
        AtualizadoEm = DateTime.SpecifyKind(atualizadoEm, DateTimeKind.Utc),
    };

    public bool OcorreEm(DateOnly dia) => Ativa && dia >= DataInicio && (Dias & dia.Bit()) != 0;

    public void Editar(string titulo, string? descricao, int? projetoId, DiasSemana dias, DateOnly dataInicio, DateTime agoraUtc)
    {
        Aplicar(titulo, descricao, projetoId, dias, dataInicio);
        AtualizadoEm = agoraUtc;
    }

    public void DefinirAtiva(bool ativa, DateTime agoraUtc)
    {
        Ativa = ativa;
        AtualizadoEm = agoraUtc;
    }

    private void Aplicar(string titulo, string? descricao, int? projetoId, DiasSemana dias, DateOnly dataInicio)
    {
        // Reaproveita as validações de título/descrição da Tarefa.
        var t = Tarefa.Nova(titulo, descricao, projetoId, dataInicio, null, DateTime.UtcNow);
        if ((dias & DiasSemana.Todos) == DiasSemana.Nenhum)
            throw new RegraDeNegocioException("rotina.dias", "Escolha pelo menos um dia da semana.");
        Titulo = t.Titulo;
        Descricao = t.Descricao;
        ProjetoId = projetoId;
        Dias = dias & DiasSemana.Todos;
        DataInicio = dataInicio;
    }
}
