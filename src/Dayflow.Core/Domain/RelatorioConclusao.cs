namespace Dayflow.Core.Domain;

/// <summary>Valores persistidos como INTEGER. Não renumerar.</summary>
public enum FrequenciaEconomia
{
    UnicaVez = 0,
    PorDia = 1,
    PorSemana = 2,
    PorMes = 3,
}

/// <summary>
/// Relatório opcional de uma tarefa concluída: o que foi feito, quem se beneficia
/// e a economia de tempo INFORMADA pelo usuário (o app não estima nada sozinho).
/// Economia por ocorrência = Minutos × Pessoas. Estimativa mensal usa fatores fixos e declarados.
/// </summary>
public sealed record RelatorioConclusao(
    int TarefaId,
    string? Resumo,
    string? Beneficiados,
    int MinutosEconomizados,
    FrequenciaEconomia Frequencia,
    int Pessoas,
    DateTime CriadoEm,
    DateTime AtualizadoEm)
{
    public const int ResumoMax = 4000;
    public const int BeneficiadosMax = 200;
    public const int MinutosMax = 100_000;
    public const int PessoasMax = 10_000;

    /// <summary>Dias úteis por mês e semanas por mês usados na estimativa mensal.</summary>
    public const double DiasUteisMes = 21;
    public const double SemanasMes = 52.0 / 12.0;

    public int MinutosPorOcorrencia => MinutosEconomizados * Pessoas;

    /// <summary>Null para "única vez" (não recorrente).</summary>
    public double? MinutosPorMes => FatorMensal(Frequencia) is { } f ? MinutosPorOcorrencia * f : null;

    public static double? FatorMensal(FrequenciaEconomia f) => f switch
    {
        FrequenciaEconomia.PorDia => DiasUteisMes,
        FrequenciaEconomia.PorSemana => SemanasMes,
        FrequenciaEconomia.PorMes => 1,
        _ => null,
    };

    public static RelatorioConclusao Criar(int tarefaId, string? resumo, string? beneficiados, int minutos,
        FrequenciaEconomia freq, int pessoas, DateTime criadoEm, DateTime agoraUtc)
    {
        var r = string.IsNullOrWhiteSpace(resumo) ? null : resumo.Trim();
        var b = string.IsNullOrWhiteSpace(beneficiados) ? null : beneficiados.Trim();
        if (r is { Length: > ResumoMax })
            throw new RegraDeNegocioException("conclusao.resumo", $"Resumo excede {ResumoMax} caracteres.");
        if (b is { Length: > BeneficiadosMax })
            throw new RegraDeNegocioException("conclusao.beneficiados", $"Campo 'quem se beneficia' excede {BeneficiadosMax} caracteres.");
        if (minutos is < 0 or > MinutosMax)
            throw new RegraDeNegocioException("conclusao.minutos", "Minutos economizados fora do intervalo.");
        if (pessoas is < 1 or > PessoasMax)
            throw new RegraDeNegocioException("conclusao.pessoas", "Número de pessoas deve ser pelo menos 1.");
        if (!Enum.IsDefined(freq))
            throw new RegraDeNegocioException("conclusao.frequencia", "Frequência inválida.");
        if (r is null && b is null && minutos == 0)
            throw new RegraDeNegocioException("conclusao.vazio", "Preencha ao menos um campo.");
        return new RelatorioConclusao(tarefaId, r, b, minutos, freq, pessoas, criadoEm, agoraUtc);
    }
}
