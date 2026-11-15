namespace Dayflow.Core.Data.Migracoes;

// v2: relatório de conclusão (1:1 com Tarefa). Bancos v1 (APK de teste 1.0.0) migram sem perda.
internal static class M002_RelatorioConclusao
{
    public static Migracao Criar() => new(2, "relatorio_conclusao",
    [
        """
        CREATE TABLE RelatorioConclusao (
            TarefaId            INTEGER PRIMARY KEY REFERENCES Tarefa (Id) ON DELETE CASCADE,
            Resumo              TEXT    NULL CHECK (Resumo IS NULL OR length(Resumo) <= 4000),
            Beneficiados        TEXT    NULL CHECK (Beneficiados IS NULL OR length(Beneficiados) <= 200),
            MinutosEconomizados INTEGER NOT NULL DEFAULT 0 CHECK (MinutosEconomizados BETWEEN 0 AND 100000),
            Frequencia          INTEGER NOT NULL DEFAULT 0 CHECK (Frequencia IN (0, 1, 2, 3)), -- única, dia, semana, mês
            Pessoas             INTEGER NOT NULL DEFAULT 1 CHECK (Pessoas BETWEEN 1 AND 10000),
            CriadoEm            INTEGER NOT NULL,      -- ticks UTC
            AtualizadoEm        INTEGER NOT NULL       -- ticks UTC
        ) WITHOUT ROWID
        """,
    ]);
}
