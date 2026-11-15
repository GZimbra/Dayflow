using Dayflow.Core.Data;
using Dayflow.Core.Data.Linhas;
using Dayflow.Core.Domain;
using Microsoft.Extensions.Logging;
using SQLite;

namespace Dayflow.Core.Services;

public sealed class ServicoTarefas(BancoDayflow banco, IRelogioCivil relogio, ILogger<ServicoTarefas> log)
{
    /// <summary>Lista do dia. Para hoje/passado, materializa antes as instâncias de rotina do dia.</summary>
    public async Task<IReadOnlyList<ItemTarefaDia>> DoDiaAsync(DateOnly dia, CancellationToken ct = default)
    {
        if (dia <= relogio.Hoje)
        {
            var criadas = await banco.EscreverAsync(c => ServicoRotinas.Materializar(c, dia, relogio), ct).ConfigureAwait(false);
            if (criadas > 0) log.LogInformation("{Qtd} instância(s) de rotina materializada(s)", criadas);
        }
        return await LerDoDia(dia, ct).ConfigureAwait(false);
    }

    private Task<IReadOnlyList<ItemTarefaDia>> LerDoDia(DateOnly dia, CancellationToken ct) =>
        banco.LerAsync<IReadOnlyList<ItemTarefaDia>>(c =>
        {
            var d = Mapeamento.Dia(dia);
            return c.Query<LinhaTarefaDia>(ConsultasSql.TarefasDoDia, d)
                .Select(l =>
                {
                    var t = l.ParaDominio();
                    return new ItemTarefaDia(t, l.ProjetoNome, l.ProjetoCor, l.MinutosNoDia, t.AtrasadaEm(dia), l.Rodando,
                        l.InicioMin, l.DuracaoMin);
                }).ToList();
        }, ct);

    public Task<Tarefa> ObterAsync(int id, CancellationToken ct = default) =>
        banco.LerAsync(c => NucleoEscrita.ObterTarefa(c, id).ParaDominio(), ct);

    public Task<int> CriarAsync(DadosTarefa dados, CancellationToken ct = default) =>
        banco.EscreverAsync(c =>
        {
            ArgumentNullException.ThrowIfNull(dados);
            GarantirProjetoAtivo(c, dados.ProjetoId);
            var t = Tarefa.Nova(dados.Titulo, dados.Descricao, dados.ProjetoId, dados.DataInicio, dados.DataFimPrevista, relogio.AgoraUtc);
            var linha = t.ParaLinha();
            c.Insert(linha);
            log.LogInformation("Tarefa {Id} criada", linha.Id);
            return linha.Id;
        }, ct);

    public Task EditarAsync(int id, DadosTarefa dados, CancellationToken ct = default) =>
        banco.EscreverAsync(c =>
        {
            ArgumentNullException.ThrowIfNull(dados);
            var t = NucleoEscrita.ObterTarefa(c, id).ParaDominio();
            if (dados.ProjetoId != t.ProjetoId) GarantirProjetoAtivo(c, dados.ProjetoId);
            var (primeiro, _) = NucleoEscrita.LimitesRegistro(c, id);
            t.Editar(dados.Titulo, dados.Descricao, dados.ProjetoId, dados.DataInicio, dados.DataFimPrevista, primeiro, relogio.AgoraUtc);
            NucleoEscrita.Salvar(c, t);
        }, ct);

    public Task ConcluirAsync(int id, CancellationToken ct = default) => Encerrar(id, concluir: true, ct);
    public Task CancelarAsync(int id, CancellationToken ct = default) => Encerrar(id, concluir: false, ct);

    public Task ReabrirAsync(int id, CancellationToken ct = default) =>
        banco.EscreverAsync(c =>
        {
            var t = NucleoEscrita.ObterTarefa(c, id).ParaDominio();
            t.Reabrir(relogio.AgoraUtc);
            NucleoEscrita.Salvar(c, t);
            log.LogInformation("Tarefa {Id} reaberta", id);
        }, ct);

    public Task VoltarParaPendenteAsync(int id, CancellationToken ct = default) =>
        banco.EscreverAsync(c =>
        {
            PausarSeFor(c, id);
            var t = NucleoEscrita.ObterTarefa(c, id).ParaDominio();
            t.VoltarParaPendente(relogio.AgoraUtc);
            NucleoEscrita.Salvar(c, t);
        }, ct);

    /// <summary>Exclusão física com cascade de registros/ordem/cronômetro.</summary>
    public Task ExcluirAsync(int id, CancellationToken ct = default) =>
        banco.EscreverAsync(c =>
        {
            var linha = NucleoEscrita.ObterTarefa(c, id);
            if (linha.RotinaId is not null)
                throw new RegraDeNegocioException("tarefa.rotina_excluir",
                    "Instância de rotina não é excluída (seria recriada). Cancele-a ou desative a rotina.");
            c.Delete<TarefaLinha>(id);
            log.LogInformation("Tarefa {Id} excluída", id);
        }, ct);

    /// <summary>
    /// Persiste a ordem do dia. Precisa conter exatamente as tarefas visíveis no dia.
    /// </summary>
    public Task ReordenarAsync(DateOnly dia, IReadOnlyList<int> idsEmOrdem, CancellationToken ct = default) =>
        banco.EscreverAsync(c =>
        {
            ArgumentNullException.ThrowIfNull(idsEmOrdem);
            var d = Mapeamento.Dia(dia);
            var visiveis = c.QueryScalars<int>(
                $"SELECT t.Id FROM Tarefa t WHERE {ConsultasSql.FiltroVisivelNoDia}", d).ToHashSet();
            if (idsEmOrdem.Count != idsEmOrdem.Distinct().Count() || !visiveis.SetEquals(idsEmOrdem))
                throw new RegraDeNegocioException("ordem.divergente", "A lista mudou. Recarregue antes de reordenar.");

            c.Execute("DELETE FROM OrdemDia WHERE Data = ?", d);
            for (var i = 0; i < idsEmOrdem.Count; i++)
                c.Execute("INSERT INTO OrdemDia (Data, TarefaId, Posicao) VALUES (?, ?, ?)", d, idsEmOrdem[i], i);
        }, ct);

    private Task Encerrar(int id, bool concluir, CancellationToken ct) =>
        banco.EscreverAsync(c =>
        {
            PausarSeFor(c, id);
            var t = NucleoEscrita.ObterTarefa(c, id).ParaDominio();
            var (_, ultimo) = NucleoEscrita.LimitesRegistro(c, id);
            if (concluir) t.Concluir(relogio.Hoje, ultimo, relogio.AgoraUtc);
            else t.Cancelar(relogio.Hoje, ultimo, relogio.AgoraUtc);
            NucleoEscrita.Salvar(c, t);
            log.LogInformation("Tarefa {Id} encerrada como {Status}", id, t.Status);
        }, ct);

    private void PausarSeFor(SQLiteConnection c, int tarefaId)
    {
        var ativo = c.Find<CronometroAtivoLinha>(1);
        if (ativo?.TarefaId == tarefaId) NucleoEscrita.PausarCronometro(c, relogio, log);
    }

    private static void GarantirProjetoAtivo(SQLiteConnection c, int? projetoId)
    {
        if (projetoId is null) return;
        var ativo = c.ExecuteScalar<int?>("SELECT Ativo FROM Projeto WHERE Id = ?", projetoId.Value);
        if (ativo is null) throw new RegraDeNegocioException("projeto.inexistente", "Projeto não encontrado.");
        if (ativo == 0) throw new RegraDeNegocioException("projeto.arquivado", "Projeto arquivado não recebe tarefas novas.");
    }

    internal sealed class LinhaTarefaDia
    {
        public int Id { get; set; }
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
        public string? ProjetoNome { get; set; }
        public string? ProjetoCor { get; set; }
        public int MinutosNoDia { get; set; }
        public bool Rodando { get; set; }
        public int? InicioMin { get; set; }
        public int? DuracaoMin { get; set; }

        public Tarefa ParaDominio() => new TarefaLinha
        {
            Id = Id, ProjetoId = ProjetoId, RotinaId = RotinaId, Titulo = Titulo, Descricao = Descricao, DataInicio = DataInicio,
            DataFimPrevista = DataFimPrevista, Status = Status, DataConclusao = DataConclusao,
            CriadoEm = CriadoEm, AtualizadoEm = AtualizadoEm,
        }.ParaDominio();
    }
}
