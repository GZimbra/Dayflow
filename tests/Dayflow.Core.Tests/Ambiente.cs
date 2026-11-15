using Dayflow.Core.Data;
using Dayflow.Core.Domain;
using Dayflow.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dayflow.Core.Tests;

/// <summary>Banco SQLite real em arquivo temporário + relógio falso com fuso configurável.</summary>
public sealed class Ambiente : IAsyncDisposable
{
    public TempoManual Tempo { get; }
    public IRelogioCivil Relogio { get; }
    public BancoDayflow Banco { get; private set; }
    public ServicoTarefas Tarefas { get; private set; } = null!;
    public ServicoProjetos Projetos { get; private set; } = null!;
    public ServicoCronometro Cronometro { get; private set; } = null!;
    public ServicoRegistros Registros { get; private set; } = null!;
    public ServicoRelatorios Relatorios { get; private set; } = null!;
    public ServicoRotinas Rotinas { get; private set; } = null!;
    public ServicoLembretes Lembretes { get; private set; } = null!;
    public ServicoConclusoes Conclusoes { get; private set; } = null!;
    public ServicoHorarios Horarios { get; private set; } = null!;
    private readonly string _arquivo;

    public Ambiente(string fuso = "America/Sao_Paulo", DateTimeOffset? agora = null)
    {
        Tempo = new TempoManual(agora ?? new DateTimeOffset(2026, 3, 2, 12, 0, 0, TimeSpan.Zero),
            TimeZoneInfo.FindSystemTimeZoneById(fuso));
        Relogio = new RelogioCivil(Tempo);
        _arquivo = Path.Combine(Path.GetTempPath(), $"dayflow-test-{Guid.NewGuid():N}.db");
        Banco = Abrir();
    }

    private BancoDayflow Abrir()
    {
        var b = new BancoDayflow(_arquivo, NullLoggerFactory.Instance);
        Tarefas = new ServicoTarefas(b, Relogio, NullLogger<ServicoTarefas>.Instance);
        Projetos = new ServicoProjetos(b, NullLogger<ServicoProjetos>.Instance);
        Cronometro = new ServicoCronometro(b, Relogio, NullLogger<ServicoCronometro>.Instance);
        Registros = new ServicoRegistros(b, Relogio, NullLogger<ServicoRegistros>.Instance);
        Relatorios = new ServicoRelatorios(b);
        Rotinas = new ServicoRotinas(b, Relogio, NullLogger<ServicoRotinas>.Instance);
        Lembretes = new ServicoLembretes(Tarefas, Cronometro, Relogio);
        Conclusoes = new ServicoConclusoes(b, Relogio, NullLogger<ServicoConclusoes>.Instance);
        Horarios = new ServicoHorarios(b, NullLogger<ServicoHorarios>.Instance);
        return b;
    }

    /// <summary>Fecha e reabre o arquivo (simula kill do processo).</summary>
    public void Reabrir()
    {
        Banco.Dispose();
        Banco = Abrir();
    }

    /// <summary>Posiciona o relógio num horário LOCAL do fuso configurado.</summary>
    public void IrPara(int ano, int mes, int dia, int hora = 12, int minuto = 0, int segundo = 0)
    {
        var local = new DateTime(ano, mes, dia, hora, minuto, segundo, DateTimeKind.Unspecified);
        var utc = TimeZoneInfo.ConvertTimeToUtc(local, Tempo.LocalTimeZone);
        Tempo.Agora = (new DateTimeOffset(utc, TimeSpan.Zero));
    }

    public void IrParaUtc(DateTime utc) => Tempo.Agora = (new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)));

    public static DateOnly D(int a, int m, int d) => new(a, m, d);

    public Task<int> NovaTarefa(string titulo, DateOnly inicio, int? projeto = null, DateOnly? fimPrevisto = null) =>
        Tarefas.CriarAsync(new DadosTarefa(titulo, null, projeto, inicio, fimPrevisto));

    public async Task<List<int>> IdsDoDia(DateOnly dia) =>
        (await Tarefas.DoDiaAsync(dia)).Select(i => i.Tarefa.Id).ToList();

    public async ValueTask DisposeAsync()
    {
        await Banco.DisposeAsync();
        foreach (var f in new[] { _arquivo, _arquivo + "-wal", _arquivo + "-shm" })
            if (File.Exists(f)) File.Delete(f);
    }
}

/// <summary>FakeTimeProvider não permite voltar no tempo; os testes precisam (relógio retrocedendo).</summary>
public sealed class TempoManual(DateTimeOffset agora, TimeZoneInfo fuso) : TimeProvider
{
    public DateTimeOffset Agora { get; set; } = agora;
    public override DateTimeOffset GetUtcNow() => Agora.ToUniversalTime();
    public override TimeZoneInfo LocalTimeZone => fuso;
}
