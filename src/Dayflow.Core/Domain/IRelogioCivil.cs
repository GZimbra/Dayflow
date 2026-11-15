namespace Dayflow.Core.Domain;

/// <summary>
/// Converte instantes UTC em dia civil no fuso do aparelho. Ponto único de verdade para "hoje".
/// </summary>
public interface IRelogioCivil
{
    DateTime AgoraUtc { get; }
    DateOnly Hoje { get; }
    TimeZoneInfo Fuso { get; }
    DateOnly DiaDe(DateTime utc);
    /// <summary>Primeiro instante UTC do dia civil (trata meia-noite inexistente/ambígua em DST).</summary>
    DateTime InicioDoDiaUtc(DateOnly dia);
}

public sealed class RelogioCivil(TimeProvider tempo) : IRelogioCivil
{
    public TimeZoneInfo Fuso => tempo.LocalTimeZone;
    public DateTime AgoraUtc => tempo.GetUtcNow().UtcDateTime;
    public DateOnly Hoje => DiaDe(AgoraUtc);

    public DateOnly DiaDe(DateTime utc)
    {
        if (utc.Kind != DateTimeKind.Utc) throw new ArgumentException("Esperado UTC.", nameof(utc));
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, Fuso));
    }

    public DateTime InicioDoDiaUtc(DateOnly dia)
    {
        var fuso = Fuso;
        var local = dia.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);

        // Meia-noite inexistente (ex.: início do horário de verão às 00:00): avança até o primeiro minuto válido.
        var guarda = 0;
        while (fuso.IsInvalidTime(local))
        {
            local = local.AddMinutes(1);
            if (++guarda > 24 * 60) throw new InvalidOperationException("Fuso sem horário válido no dia.");
        }

        if (fuso.IsAmbiguousTime(local))
        {
            // Primeira ocorrência = maior offset.
            var offset = fuso.GetAmbiguousTimeOffsets(local).Max();
            return DateTime.SpecifyKind(local - offset, DateTimeKind.Utc);
        }
        return TimeZoneInfo.ConvertTimeToUtc(local, fuso);
    }
}
