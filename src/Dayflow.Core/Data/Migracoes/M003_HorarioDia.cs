namespace Dayflow.Core.Data.Migracoes;

// v3: horário planejado da tarefa em cada dia (visão hora a hora).
// Uma tarefa multi-dia pode ter horário diferente em cada dia, por isso a chave é (TarefaId, Data).
internal static class M003_HorarioDia
{
    public static Migracao Criar() => new(3, "horario_dia",
    [
        """
        CREATE TABLE HorarioDia (
            TarefaId   INTEGER NOT NULL REFERENCES Tarefa (Id) ON DELETE CASCADE,
            Data       INTEGER NOT NULL,                                        -- DayNumber (dia civil)
            InicioMin  INTEGER NOT NULL CHECK (InicioMin BETWEEN 0 AND 1439),   -- minutos desde 00:00
            DuracaoMin INTEGER NOT NULL DEFAULT 60 CHECK (DuracaoMin BETWEEN 15 AND 1440),
            PRIMARY KEY (TarefaId, Data),
            CHECK (InicioMin + DuracaoMin <= 1440)
        ) WITHOUT ROWID
        """,
        "CREATE INDEX IX_HorarioDia_Data_Inicio ON HorarioDia (Data, InicioMin)",
    ]);
}
