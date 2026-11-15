namespace Dayflow.Core.Data.Migracoes;

/// <summary>Migração = lista de statements SQL (sqlite-net executa um statement por chamada).</summary>
internal sealed record Migracao(int Versao, string Nome, IReadOnlyList<string> Sql);
