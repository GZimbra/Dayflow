using Dayflow.Core.Data.Linhas;
using Dayflow.Core.Domain;

namespace Dayflow.Core.Data;

internal static class Mapeamento
{
    public static int Dia(DateOnly d) => d.DayNumber;
    public static int? Dia(DateOnly? d) => d?.DayNumber;
    public static DateOnly Dia(int n) => DateOnly.FromDayNumber(n);
    public static DateOnly? Dia(int? n) => n is { } v ? DateOnly.FromDayNumber(v) : null;
    public static long Utc(DateTime dt) =>
        dt.Kind == DateTimeKind.Utc ? dt.Ticks : throw new ArgumentException("Esperado UTC.", nameof(dt));
    public static DateTime Utc(long ticks) => new(ticks, DateTimeKind.Utc);

    public static Tarefa ParaDominio(this TarefaLinha l) => Tarefa.Reidratar(
        l.Id, l.ProjetoId, l.RotinaId, l.Titulo, l.Descricao, Dia(l.DataInicio), Dia(l.DataFimPrevista),
        (StatusTarefa)l.Status, Dia(l.DataConclusao), Utc(l.CriadoEm), Utc(l.AtualizadoEm));

    public static TarefaLinha ParaLinha(this Tarefa t) => new()
    {
        Id = t.Id, ProjetoId = t.ProjetoId, RotinaId = t.RotinaId, Titulo = t.Titulo, Descricao = t.Descricao,
        DataInicio = Dia(t.DataInicio), DataFimPrevista = Dia(t.DataFimPrevista), Status = (int)t.Status,
        DataConclusao = Dia(t.DataConclusao), CriadoEm = Utc(t.CriadoEm), AtualizadoEm = Utc(t.AtualizadoEm),
    };

    public static Rotina ParaDominio(this RotinaLinha l) => Rotina.Reidratar(l.Id, l.Titulo, l.Descricao, l.ProjetoId,
        (DiasSemana)l.DiasSemana, l.Ativa, Dia(l.DataInicio), Utc(l.CriadoEm), Utc(l.AtualizadoEm));

    public static RotinaLinha ParaLinha(this Rotina r) => new()
    {
        Id = r.Id, Titulo = r.Titulo, Descricao = r.Descricao, ProjetoId = r.ProjetoId, DiasSemana = (int)r.Dias,
        Ativa = r.Ativa, DataInicio = Dia(r.DataInicio), CriadoEm = Utc(r.CriadoEm), AtualizadoEm = Utc(r.AtualizadoEm),
    };

    public static Projeto ParaDominio(this ProjetoLinha l) => new(l.Id, l.Nome, l.Cor, l.Ativo);
    public static ProjetoLinha ParaLinha(this Projeto p) => new() { Id = p.Id, Nome = p.Nome, Cor = p.Cor, Ativo = p.Ativo };

    public static RegistroDiario ParaDominio(this RegistroDiarioLinha l) =>
        new(l.Id, l.TarefaId, Dia(l.Data), l.TempoGastoMin, l.SegundosResiduais, l.Observacao);
}
