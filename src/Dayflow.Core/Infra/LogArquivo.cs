using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Dayflow.Core.Infra;

/// <summary>
/// Logger local em arquivo com rotação por tamanho. Substitui telemetria/crash reporting.
/// Regra: mensagens só com IDs, contagens e códigos. Nunca título/descrição/observação.
/// Exceções gravam apenas tipo + HResult + stack (sem Message, que pode conter dados).
/// </summary>
public sealed class LogArquivoProvider : ILoggerProvider
{
    private readonly string _dir;
    private readonly long _maxBytes;
    private readonly int _arquivos;
    private readonly LogLevel _minimo;
    private readonly object _trava = new();
    private readonly ConcurrentDictionary<string, Logger> _loggers = new();

    public LogArquivoProvider(string diretorio, LogLevel minimo = LogLevel.Information, long maxBytes = 512 * 1024, int arquivos = 3)
    {
        _dir = diretorio;
        _minimo = minimo;
        _maxBytes = maxBytes;
        _arquivos = arquivos;
        Directory.CreateDirectory(_dir);
    }

    public string ArquivoAtual => Path.Combine(_dir, "dayflow.log");

    public ILogger CreateLogger(string categoryName) => _loggers.GetOrAdd(categoryName, n => new Logger(this, n));

    /// <summary>Concatena os logs (mais antigo primeiro) para exportação pelo usuário.</summary>
    public string Exportar()
    {
        lock (_trava)
        {
            GravarPendentes();
            var sb = new StringBuilder();
            for (var i = _arquivos - 1; i >= 0; i--)
            {
                var p = i == 0 ? ArquivoAtual : $"{ArquivoAtual}.{i}";
                if (File.Exists(p)) sb.Append(File.ReadAllText(p));
            }
            return sb.ToString();
        }
    }

    public void Dispose() { }

    // Gravação fora da thread chamadora: log no meio de uma ação da UI não espera disco.
    private readonly ConcurrentQueue<string> _fila = new();
    private int _gravando;

    private void Escrever(string linha)
    {
        _fila.Enqueue(linha);
        if (Interlocked.CompareExchange(ref _gravando, 1, 0) == 0)
            ThreadPool.UnsafeQueueUserWorkItem(_ => Descarregar(), null);
    }

    private void Descarregar()
    {
        try
        {
            lock (_trava) GravarPendentes();
        }
        finally
        {
            Volatile.Write(ref _gravando, 0);
            // Chegou linha entre o fim da gravação e a liberação: reagenda.
            if (!_fila.IsEmpty && Interlocked.CompareExchange(ref _gravando, 1, 0) == 0)
                ThreadPool.UnsafeQueueUserWorkItem(_ => Descarregar(), null);
        }
    }

    /// <summary>Chamar com _trava. Junta as linhas pendentes numa única escrita.</summary>
    private void GravarPendentes()
    {
        if (_fila.IsEmpty) return;
        var sb = new StringBuilder();
        while (_fila.TryDequeue(out var l)) sb.Append(l);
        try
        {
            var fi = new FileInfo(ArquivoAtual);
            if (fi.Exists && fi.Length > _maxBytes) Rotacionar();
            File.AppendAllText(ArquivoAtual, sb.ToString());
        }
        catch (IOException) { /* log nunca derruba o app */ }
        catch (UnauthorizedAccessException) { }
    }

    private void Rotacionar()
    {
        var ultimo = $"{ArquivoAtual}.{_arquivos - 1}";
        if (File.Exists(ultimo)) File.Delete(ultimo);
        for (var i = _arquivos - 2; i >= 1; i--)
        {
            var de = $"{ArquivoAtual}.{i}";
            if (File.Exists(de)) File.Move(de, $"{ArquivoAtual}.{i + 1}");
        }
        File.Move(ArquivoAtual, $"{ArquivoAtual}.1");
    }

    private sealed class Logger(LogArquivoProvider p, string categoria) : ILogger
    {
        private readonly string _cat = categoria[(categoria.LastIndexOf('.') + 1)..];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= p._minimo && logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            var sb = new StringBuilder()
                .Append(DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture))
                .Append(' ').Append(logLevel.ToString()[..4].ToUpperInvariant())
                .Append(' ').Append(_cat).Append(": ").Append(formatter(state, exception));
            if (exception is not null)
                sb.Append(" | ").Append(exception.GetType().FullName).Append(" HResult=0x").Append(exception.HResult.ToString("X8", CultureInfo.InvariantCulture))
                  .AppendLine().Append(exception.StackTrace);
            sb.AppendLine();
            p.Escrever(sb.ToString());
        }
    }
}
