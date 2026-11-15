namespace Dayflow.Core.Domain;

/// <summary>
/// Configuração dos lembretes de hora em hora. Persistida pelo app (Preferences), não no SQLite.
/// Janela [Inicio, Fim] inclusiva, em horas cheias locais.
/// </summary>
public sealed record ConfiguracaoLembretes(
    bool Ativo,
    int HoraInicio = 8,
    int HoraFim = 18,
    DiasSemana Dias = DiasSemana.Uteis,
    DateOnly? SilenciadoAte = null)
{
    public void Validar()
    {
        if (HoraInicio is < 0 or > 23 || HoraFim is < 0 or > 23 || HoraFim < HoraInicio)
            throw new RegraDeNegocioException("lembrete.janela", "Janela de horário inválida.");
        if ((Dias & DiasSemana.Todos) == DiasSemana.Nenhum)
            throw new RegraDeNegocioException("lembrete.dias", "Escolha pelo menos um dia.");
    }
}

public static class AgendaLembretes
{
    /// <summary>
    /// Próximo disparo (UTC) estritamente depois de <paramref name="aposUtc"/>, na hora cheia local,
    /// dentro da janela e dos dias configurados. Horas inexistentes (DST) são puladas.
    /// Null se desativado.
    /// </summary>
    public static DateTime? Proximo(ConfiguracaoLembretes cfg, DateTime aposUtc, TimeZoneInfo fuso)
    {
        ArgumentNullException.ThrowIfNull(cfg);
        ArgumentNullException.ThrowIfNull(fuso);
        if (!cfg.Ativo) return null;
        cfg.Validar();

        var local = TimeZoneInfo.ConvertTimeFromUtc(aposUtc, fuso);
        var dia = DateOnly.FromDateTime(local);
        for (var i = 0; i < 15; i++, dia = dia.AddDays(1))
        {
            if ((cfg.Dias & dia.Bit()) == 0) continue;
            if (cfg.SilenciadoAte is { } s && dia <= s) continue;
            for (var h = cfg.HoraInicio; h <= cfg.HoraFim; h++)
            {
                var candidatoLocal = dia.ToDateTime(new TimeOnly(h, 0), DateTimeKind.Unspecified);
                if (fuso.IsInvalidTime(candidatoLocal)) continue;
                var utc = fuso.IsAmbiguousTime(candidatoLocal)
                    ? DateTime.SpecifyKind(candidatoLocal - fuso.GetAmbiguousTimeOffsets(candidatoLocal).Max(), DateTimeKind.Utc)
                    : TimeZoneInfo.ConvertTimeToUtc(candidatoLocal, fuso);
                if (utc > aposUtc) return utc;
            }
        }
        return null;
    }
}
