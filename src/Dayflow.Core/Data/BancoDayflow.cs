using Dayflow.Core.Data.Migracoes;
using Microsoft.Extensions.Logging;
using SQLite;

namespace Dayflow.Core.Data;

/// <summary>
/// Conexão única serializada. Leitura e escrita passam por um SemaphoreSlim:
/// app single-user, volume pequeno; evita SQLITE_BUSY e mantém transações simples.
/// Toda escrita roda em RunInTransaction (BEGIN IMMEDIATE semântico via savepoint do sqlite-net).
/// </summary>
public sealed class BancoDayflow : IAsyncDisposable, IDisposable
{
    private readonly SemaphoreSlim _trava = new(1, 1);
    private readonly SQLiteConnection _conn;
    private readonly ILogger<BancoDayflow> _log;

    public string Caminho { get; }

    public BancoDayflow(string caminho, ILoggerFactory logs)
    {
        ArgumentNullException.ThrowIfNull(logs);
        Caminho = caminho;
        _log = logs.CreateLogger<BancoDayflow>();
        _conn = new SQLiteConnection(caminho,
            SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.FullMutex,
            storeDateTimeAsTicks: true);
        _conn.ExecuteScalar<string>("PRAGMA journal_mode = WAL");
        _conn.Execute("PRAGMA foreign_keys = ON");
        _conn.Execute("PRAGMA synchronous = NORMAL");
        if (_conn.ExecuteScalar<int>("PRAGMA foreign_keys") != 1)
            throw new InvalidOperationException("SQLite sem suporte a foreign_keys.");
        new Migrador(logs.CreateLogger<Migrador>()).Aplicar(_conn);
    }

    public async Task<T> LerAsync<T>(Func<SQLiteConnection, T> leitura, CancellationToken ct = default)
    {
        await _trava.WaitAsync(ct).ConfigureAwait(false);
        try { return await Task.Run(() => leitura(_conn), ct).ConfigureAwait(false); }
        finally { _trava.Release(); }
    }

    public async Task<T> EscreverAsync<T>(Func<SQLiteConnection, T> escrita, CancellationToken ct = default)
    {
        await _trava.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                T resultado = default!;
                try
                {
                    _conn.RunInTransaction(() => resultado = escrita(_conn));
                }
                catch (SQLiteException ex)
                {
                    // Só o código. Mensagem do SQLite pode conter valores de colunas.
                    _log.LogError("Falha de escrita SQLite: {Resultado}", ex.Result);
                    throw;
                }
                return resultado;
            }, ct).ConfigureAwait(false);
        }
        finally { _trava.Release(); }
    }

    public Task EscreverAsync(Action<SQLiteConnection> escrita, CancellationToken ct = default) =>
        EscreverAsync(c => { escrita(c); return true; }, ct);

    public void Dispose()
    {
        _conn.Dispose();
        _trava.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
