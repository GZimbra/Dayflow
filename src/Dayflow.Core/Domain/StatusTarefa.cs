namespace Dayflow.Core.Domain;

/// <summary>Valores persistidos como INTEGER. Não renumerar.</summary>
public enum StatusTarefa
{
    Pendente = 0,
    EmAndamento = 1,
    Concluida = 2,
    Cancelada = 3,
}

public static class StatusTarefaExtensions
{
    public static bool EhAberto(this StatusTarefa s) => s is StatusTarefa.Pendente or StatusTarefa.EmAndamento;
    public static bool EhFinal(this StatusTarefa s) => s is StatusTarefa.Concluida or StatusTarefa.Cancelada;
}
