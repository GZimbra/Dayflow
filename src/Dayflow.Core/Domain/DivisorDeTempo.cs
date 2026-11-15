namespace Dayflow.Core.Domain;

/// <summary>Quebra um intervalo UTC em fatias por dia civil local (meia-noite e DST).</summary>
public static class DivisorDeTempo
{
    public readonly record struct Fatia(DateOnly Dia, TimeSpan Duracao);

    public static IReadOnlyList<Fatia> PorDia(DateTime inicioUtc, DateTime fimUtc, IRelogioCivil relogio)
    {
        ArgumentNullException.ThrowIfNull(relogio);
        if (inicioUtc.Kind != DateTimeKind.Utc || fimUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Intervalo deve ser UTC.");
        var fatias = new List<Fatia>();
        if (fimUtc <= inicioUtc) return fatias;

        var cursor = inicioUtc;
        while (cursor < fimUtc)
        {
            var dia = relogio.DiaDe(cursor);
            var proximo = relogio.InicioDoDiaUtc(dia.AddDays(1));
            var corte = proximo < fimUtc ? proximo : fimUtc;
            fatias.Add(new Fatia(dia, corte - cursor));
            cursor = corte;
        }
        return fatias;
    }
}
