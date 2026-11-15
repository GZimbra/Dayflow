using Microsoft.Extensions.Logging;
using SQLite;

namespace Dayflow.Core.Data.Migracoes;

/// <summary>
/// Migrações versionadas por PRAGMA user_version. Cada migração roda em transação própria
/// (DDL é transacional no SQLite), incluindo a atualização de user_version.
/// Nunca editar uma migração publicada: criar M00N nova.
/// </summary>
internal sealed class Migrador(ILogger<Migrador> log)
{
    internal static readonly IReadOnlyList<Migracao> Todas =
    [
        M001_Inicial.Criar(),
        M002_RelatorioConclusao.Criar(),
        M003_HorarioDia.Criar(),
    ];

    public static int VersaoAtual => Todas[^1].Versao;

    public void Aplicar(SQLiteConnection conn)
    {
        var atual = conn.ExecuteScalar<int>("PRAGMA user_version");
        if (atual > VersaoAtual)
            throw new InvalidOperationException(
                $"Banco na versão {atual}, app suporta até {VersaoAtual}. Downgrade não suportado.");

        foreach (var m in Todas.Where(m => m.Versao > atual).OrderBy(m => m.Versao))
        {
            conn.RunInTransaction(() =>
            {
                foreach (var sql in m.Sql) conn.Execute(sql);
                // PRAGMA não aceita parâmetro; valor é constante do código.
                conn.Execute($"PRAGMA user_version = {m.Versao}");
            });
            log.LogInformation("Migração {Versao} ({Nome}) aplicada", m.Versao, m.Nome);
        }

        var fk = conn.Query<FkViolacao>("PRAGMA foreign_key_check");
        if (fk.Count > 0)
            throw new InvalidOperationException($"foreign_key_check falhou: {fk.Count} violações.");
    }

    private sealed class FkViolacao
    {
        public string? table { get; set; }
    }
}
