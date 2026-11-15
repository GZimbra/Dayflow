using Dayflow.Core.Data;
using Dayflow.Core.Data.Linhas;
using Dayflow.Core.Domain;
using Microsoft.Extensions.Logging;

namespace Dayflow.Core.Services;

public sealed record ItemEconomia(
    int TarefaId, string Titulo, string Projeto, string Cor, DateOnly DataConclusao, RelatorioConclusao Relatorio);

/// <summary>Pessoas = soma das pessoas informadas em cada relatório (a mesma pessoa pode contar em mais de um).</summary>
public sealed record EconomiaProjeto(int? ProjetoId, string Nome, string Cor, int Relatorios,
    int MinutosUnicos, double MinutosPorMes, int Pessoas = 0);

public sealed record RelatorioEconomia(Periodo Periodo, IReadOnlyList<ItemEconomia> Itens, IReadOnlyList<EconomiaProjeto> PorProjeto)
{
    public double MinutosPorMesTotal => PorProjeto.Sum(p => p.MinutosPorMes);
    public int MinutosUnicosTotal => PorProjeto.Sum(p => p.MinutosUnicos);
    public int PessoasTotal => PorProjeto.Sum(p => p.Pessoas);
    public int RelatoriosTotal => PorProjeto.Sum(p => p.Relatorios);
}

/// <summary>
/// Relatórios de conclusão. Só para tarefa CONCLUÍDA e com projeto.
/// A agregação por projeto é feita no SQLite (GROUP BY), com os mesmos fatores mensais do domínio.
/// </summary>
public sealed class ServicoConclusoes(BancoDayflow banco, IRelogioCivil relogio, ILogger<ServicoConclusoes> log)
{
    internal static readonly string SqlEconomiaPorProjeto = $"""
        SELECT t.ProjetoId AS ProjetoId, p.Nome AS Nome, p.Cor AS Cor, COUNT(*) AS Relatorios, SUM(rc.Pessoas) AS Pessoas,
               SUM(CASE WHEN rc.Frequencia = 0 THEN rc.MinutosEconomizados * rc.Pessoas ELSE 0 END) AS MinutosUnicos,
               SUM(rc.MinutosEconomizados * rc.Pessoas *
                   CASE rc.Frequencia WHEN 1 THEN {RelatorioConclusao.DiasUteisMes.ToString(System.Globalization.CultureInfo.InvariantCulture)}
                                      WHEN 2 THEN {RelatorioConclusao.SemanasMes.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}
                                      WHEN 3 THEN 1.0 ELSE 0 END) AS MinutosPorMes
        FROM RelatorioConclusao rc
        JOIN Tarefa t ON t.Id = rc.TarefaId
        LEFT JOIN Projeto p ON p.Id = t.ProjetoId
        WHERE t.Status = 2 AND t.DataConclusao BETWEEN ?1 AND ?2
        GROUP BY t.ProjetoId
        ORDER BY MinutosPorMes DESC, MinutosUnicos DESC
        """;

    internal const string SqlItens = """
        SELECT rc.*, t.Titulo AS Titulo, t.DataConclusao AS DataConclusao, p.Nome AS ProjetoNome, p.Cor AS ProjetoCor
        FROM RelatorioConclusao rc
        JOIN Tarefa t ON t.Id = rc.TarefaId
        LEFT JOIN Projeto p ON p.Id = t.ProjetoId
        WHERE t.Status = 2 AND t.DataConclusao BETWEEN ?1 AND ?2
        ORDER BY t.DataConclusao DESC, t.Id DESC
        """;

    /// <summary>True se a tarefa pode (e deve) receber relatório: concluída e com projeto.</summary>
    public static bool Elegivel(Tarefa t) => t is { Status: StatusTarefa.Concluida, ProjetoId: not null };

    public Task<RelatorioConclusao?> ObterAsync(int tarefaId, CancellationToken ct = default) =>
        banco.LerAsync(c => c.Find<RelatorioConclusaoLinha>(tarefaId) is { } l ? ParaDominio(l) : null, ct);

    public Task SalvarAsync(int tarefaId, string? resumo, string? beneficiados, int minutos, FrequenciaEconomia freq,
        int pessoas, CancellationToken ct = default) =>
        banco.EscreverAsync(c =>
        {
            var t = NucleoEscrita.ObterTarefa(c, tarefaId).ParaDominio();
            if (!Elegivel(t))
                throw new RegraDeNegocioException("conclusao.inelegivel", "Relatório de conclusão é só para tarefa concluída de um projeto.");
            var atual = c.Find<RelatorioConclusaoLinha>(tarefaId);
            var agora = relogio.AgoraUtc;
            var r = RelatorioConclusao.Criar(tarefaId, resumo, beneficiados, minutos, freq, pessoas,
                atual is null ? agora : Mapeamento.Utc(atual.CriadoEm), agora);
            c.InsertOrReplace(new RelatorioConclusaoLinha
            {
                TarefaId = tarefaId, Resumo = r.Resumo, Beneficiados = r.Beneficiados, MinutosEconomizados = r.MinutosEconomizados,
                Frequencia = (int)r.Frequencia, Pessoas = r.Pessoas, CriadoEm = Mapeamento.Utc(r.CriadoEm), AtualizadoEm = Mapeamento.Utc(r.AtualizadoEm),
            });
            log.LogInformation("Relatório de conclusão salvo para tarefa {Id}", tarefaId);
        }, ct);

    public Task RemoverAsync(int tarefaId, CancellationToken ct = default) =>
        banco.EscreverAsync(c => { c.Delete<RelatorioConclusaoLinha>(tarefaId); }, ct);

    public Task<RelatorioEconomia> EconomiaAsync(Periodo p, CancellationToken ct = default) =>
        banco.LerAsync(c =>
        {
            ArgumentNullException.ThrowIfNull(p);
            var (i, f) = (Mapeamento.Dia(p.Inicio), Mapeamento.Dia(p.Fim));
            var itens = c.Query<LinhaItem>(SqlItens, i, f).Select(l => new ItemEconomia(
                l.TarefaId, l.Titulo, l.ProjetoNome ?? Rotulos.SemProjeto, l.ProjetoCor ?? Rotulos.CorSemProjeto,
                Mapeamento.Dia(l.DataConclusao), ParaDominio(l))).ToList();
            var porProjeto = c.Query<LinhaProjeto>(SqlEconomiaPorProjeto, i, f).Select(l => new EconomiaProjeto(
                l.ProjetoId, l.Nome ?? Rotulos.SemProjeto, l.Cor ?? Rotulos.CorSemProjeto, l.Relatorios, l.MinutosUnicos, l.MinutosPorMes, l.Pessoas)).ToList();
            return new RelatorioEconomia(p, itens, porProjeto);
        }, ct);

    private static RelatorioConclusao ParaDominio(RelatorioConclusaoLinha l) => new(
        l.TarefaId, l.Resumo, l.Beneficiados, l.MinutosEconomizados, (FrequenciaEconomia)l.Frequencia, l.Pessoas,
        Mapeamento.Utc(l.CriadoEm), Mapeamento.Utc(l.AtualizadoEm));

    private sealed class LinhaItem
    {
        public int TarefaId { get; set; }
        public string? Resumo { get; set; }
        public string? Beneficiados { get; set; }
        public int MinutosEconomizados { get; set; }
        public int Frequencia { get; set; }
        public int Pessoas { get; set; }
        public long CriadoEm { get; set; }
        public long AtualizadoEm { get; set; }
        public string Titulo { get; set; } = "";
        public int DataConclusao { get; set; }
        public string? ProjetoNome { get; set; }
        public string? ProjetoCor { get; set; }

        public static implicit operator RelatorioConclusaoLinha(LinhaItem l) => new()
        {
            TarefaId = l.TarefaId, Resumo = l.Resumo, Beneficiados = l.Beneficiados, MinutosEconomizados = l.MinutosEconomizados,
            Frequencia = l.Frequencia, Pessoas = l.Pessoas, CriadoEm = l.CriadoEm, AtualizadoEm = l.AtualizadoEm,
        };
    }

    private sealed class LinhaProjeto
    {
        public int? ProjetoId { get; set; }
        public string? Nome { get; set; }
        public string? Cor { get; set; }
        public int Relatorios { get; set; }
        public int Pessoas { get; set; }
        public int MinutosUnicos { get; set; }
        public double MinutosPorMes { get; set; }
    }
}
