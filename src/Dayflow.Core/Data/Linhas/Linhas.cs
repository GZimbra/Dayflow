using SQLite;

namespace Dayflow.Core.Data.Linhas;

// Linhas de persistência. sqlite-net-pcl não tem conversor de tipo, então:
//   DateOnly  -> INTEGER (DateOnly.DayNumber)
//   DateTime  -> INTEGER (Ticks UTC)
//   enum      -> INTEGER
// O esquema real é criado pelas migrações (CreateTable NÃO é usado: não gera FK nem CHECK).

[Table("Projeto")]
internal sealed class ProjetoLinha
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    public string Nome { get; set; } = "";
    public string Cor { get; set; } = "";
    public bool Ativo { get; set; }
}

[Table("Tarefa")]
internal sealed class TarefaLinha
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    public int? ProjetoId { get; set; }
    public int? RotinaId { get; set; }
    public string Titulo { get; set; } = "";
    public string? Descricao { get; set; }
    public int DataInicio { get; set; }
    public int? DataFimPrevista { get; set; }
    public int Status { get; set; }
    public int? DataConclusao { get; set; }
    public long CriadoEm { get; set; }
    public long AtualizadoEm { get; set; }
}

[Table("RegistroDiario")]
internal sealed class RegistroDiarioLinha
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    public int TarefaId { get; set; }
    public int Data { get; set; }
    public int TempoGastoMin { get; set; }
    public int SegundosResiduais { get; set; }
    public string? Observacao { get; set; }
}

[Table("CronometroAtivo")]
internal sealed class CronometroAtivoLinha
{
    [PrimaryKey] public int Id { get; set; } = 1;
    public int TarefaId { get; set; }
    public long InicioUtc { get; set; }
}

[Table("Rotina")]
internal sealed class RotinaLinha
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    public string Titulo { get; set; } = "";
    public string? Descricao { get; set; }
    public int? ProjetoId { get; set; }
    public int DiasSemana { get; set; }
    public bool Ativa { get; set; }
    public int DataInicio { get; set; }
    public long CriadoEm { get; set; }
    public long AtualizadoEm { get; set; }
}

[Table("RelatorioConclusao")]
internal sealed class RelatorioConclusaoLinha
{
    [PrimaryKey] public int TarefaId { get; set; }
    public string? Resumo { get; set; }
    public string? Beneficiados { get; set; }
    public int MinutosEconomizados { get; set; }
    public int Frequencia { get; set; }
    public int Pessoas { get; set; }
    public long CriadoEm { get; set; }
    public long AtualizadoEm { get; set; }
}

[Table("HorarioDia")]
internal sealed class HorarioDiaLinha
{
    public int TarefaId { get; set; }
    public int Data { get; set; }
    public int InicioMin { get; set; }
    public int DuracaoMin { get; set; }
}
