using Dayflow.Core.Domain;

namespace Dayflow.Core.Services;

public sealed record DadosTarefa(string Titulo, string? Descricao, int? ProjetoId, DateOnly DataInicio, DateOnly? DataFimPrevista);

public sealed record ItemTarefaDia(
    Tarefa Tarefa, string? ProjetoNome, string? ProjetoCor, int MinutosNoDia, bool Atrasada, bool CronometroRodando,
    int? InicioPlanejadoMin = null, int? DuracaoPlanejadaMin = null)
{
    public bool TemHorario => InicioPlanejadoMin is not null;
}

public sealed record EstadoCronometro(int TarefaId, DateTime InicioUtc, TimeSpan Decorrido);

public sealed record RegistroGerado(int TarefaId, DateOnly Dia, int SegundosAdicionados);

/// <summary>Intervalo fechado [Inicio, Fim] em dias civis.</summary>
public sealed record Periodo
{
    public const int MaxDias = 1100; // limita o CTE recursivo (~3 anos)

    public DateOnly Inicio { get; }
    public DateOnly Fim { get; }
    public int Dias => Fim.DayNumber - Inicio.DayNumber + 1;

    public Periodo(DateOnly inicio, DateOnly fim)
    {
        if (fim < inicio) throw new RegraDeNegocioException("periodo.invertido", "Fim do período anterior ao início.");
        if (fim.DayNumber - inicio.DayNumber + 1 > MaxDias)
            throw new RegraDeNegocioException("periodo.longo", $"Período máximo de {MaxDias} dias.");
        Inicio = inicio;
        Fim = fim;
    }

    public static Periodo DoDia(DateOnly dia) => new(dia, dia);

    /// <summary>Semana ISO: segunda a domingo.</summary>
    public static Periodo Semana(DateOnly dia)
    {
        var deslocamento = ((int)dia.DayOfWeek + 6) % 7; // segunda = 0
        var ini = dia.AddDays(-deslocamento);
        return new Periodo(ini, ini.AddDays(6));
    }

    public static Periodo Mes(int ano, int mes)
    {
        var ini = new DateOnly(ano, mes, 1);
        return new Periodo(ini, ini.AddMonths(1).AddDays(-1));
    }

    public static Periodo Mes(DateOnly dia) => Mes(dia.Year, dia.Month);
}

public static class Rotulos
{
    public const string SemProjeto = "Sem projeto";
    public const string CorSemProjeto = "#9AA3AE";
}

public sealed record HorasProjeto(int? ProjetoId, string Nome, string Cor, int Minutos);
public sealed record MinutosDia(DateOnly Dia, int Minutos);
public sealed record ConcluidasDia(DateOnly Dia, int Quantidade);
public sealed record TempoTarefaDia(int TarefaId, string Titulo, DateOnly Dia, int Minutos);
public sealed record DiaCalendario(DateOnly Dia, int Pendentes);

public sealed record TaxaConclusaoProjeto(int? ProjetoId, string Nome, string Cor, int Concluidas, int Canceladas, int Abertas)
{
    /// <summary>Concluídas / (concluídas + abertas). Canceladas fora do denominador.</summary>
    public double Taxa => Concluidas + Abertas == 0 ? 0 : (double)Concluidas / (Concluidas + Abertas);
}

public enum SituacaoNoPeriodo { EmAberto = 1, Concluida = 2, Cancelada = 3 }

public sealed record LinhaTarefaRelatorio(
    int TarefaId, string Titulo, string Projeto, StatusTarefa StatusAtual, SituacaoNoPeriodo Situacao,
    DateOnly? DataConclusao, int MinutosNoPeriodo);

public sealed record Relatorio(
    Periodo Periodo,
    int MinutosTotais,
    IReadOnlyList<LinhaTarefaRelatorio> Tarefas,
    IReadOnlyList<HorasProjeto> PorProjeto,
    IReadOnlyList<MinutosDia> PorDia)
{
    public IEnumerable<LinhaTarefaRelatorio> Concluidas => Tarefas.Where(t => t.Situacao == SituacaoNoPeriodo.Concluida);
    public IEnumerable<LinhaTarefaRelatorio> EmAberto => Tarefas.Where(t => t.Situacao == SituacaoNoPeriodo.EmAberto);
    public IEnumerable<LinhaTarefaRelatorio> Canceladas => Tarefas.Where(t => t.Situacao == SituacaoNoPeriodo.Cancelada);
}

public sealed record ResumoPendencias(DateOnly Dia, int Total, int Atrasadas, IReadOnlyList<string> Titulos);
