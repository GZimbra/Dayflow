namespace Dayflow.Core.Domain;

/// <summary>
/// Um registro por (Tarefa, Dia). Pausas e lançamentos manuais do mesmo dia são somados.
/// <see cref="SegundosResiduais"/> (0..59) acumula a fração que não fechou minuto, para não perder sessões curtas.
/// </summary>
public sealed record RegistroDiario(int Id, int TarefaId, DateOnly Data, int TempoGastoMin, int SegundosResiduais, string? Observacao)
{
    public const int MinutosMaxPorDia = 24 * 60;
    public const int ObservacaoMax = 1000;
}
