using Dayflow.Core.Infra;
using Microsoft.Extensions.Logging;
using Xunit;

#pragma warning disable CA1873, CA1848 // teste de carga do logger

namespace Dayflow.Core.Tests;

public sealed class LogArquivoTests
{
    [Fact]
    public async Task Gravacao_em_segundo_plano_nao_perde_linhas_e_exportar_inclui_pendentes()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"dayflow-log-{Guid.NewGuid():N}");
        try
        {
            var p = new LogArquivoProvider(dir);
            var log = p.CreateLogger("Teste.Categoria");
            await Task.WhenAll(Enumerable.Range(0, 4).Select(t => Task.Run(() =>
            {
                for (var i = 0; i < 250; i++) log.Log(LogLevel.Information, default, $"linha {t}-{i}", null, (s, _) => s);
            })));
            var texto = p.Exportar();
            Assert.Equal(1000, texto.Split('\n').Count(l => l.Contains("linha ", StringComparison.Ordinal)));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}
