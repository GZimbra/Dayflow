namespace Dayflow.Core.Data.Migracoes;

// Fonte da verdade do esquema v1. docs/schema.sql é cópia legível; o teste
// EsquemaTests garante que índices e constraints existem após migrar.
internal static class M001_Inicial
{
    public static Migracao Criar() => new(1, "inicial",
    [
        """
        CREATE TABLE Projeto (
            Id    INTEGER PRIMARY KEY AUTOINCREMENT,
            Nome  TEXT    NOT NULL CHECK (length(trim(Nome)) BETWEEN 1 AND 80),
            Cor   TEXT    NOT NULL CHECK (Cor GLOB '#[0-9A-F][0-9A-F][0-9A-F][0-9A-F][0-9A-F][0-9A-F]'),
            Ativo INTEGER NOT NULL DEFAULT 1 CHECK (Ativo IN (0, 1))
        )
        """,
        "CREATE UNIQUE INDEX UX_Projeto_Nome ON Projeto (Nome COLLATE NOCASE)",
        """
        CREATE TABLE Rotina (
            Id           INTEGER PRIMARY KEY AUTOINCREMENT,
            Titulo       TEXT    NOT NULL CHECK (length(trim(Titulo)) BETWEEN 1 AND 200),
            Descricao    TEXT    NULL CHECK (Descricao IS NULL OR length(Descricao) <= 4000),
            ProjetoId    INTEGER NULL REFERENCES Projeto (Id) ON DELETE SET NULL,
            DiasSemana   INTEGER NOT NULL CHECK (DiasSemana BETWEEN 1 AND 127), -- bitmask, segunda = bit 0
            Ativa        INTEGER NOT NULL DEFAULT 1 CHECK (Ativa IN (0, 1)),
            DataInicio   INTEGER NOT NULL,              -- DateOnly.DayNumber
            CriadoEm     INTEGER NOT NULL,
            AtualizadoEm INTEGER NOT NULL
        )
        """,
        """
        CREATE TABLE Tarefa (
            Id              INTEGER PRIMARY KEY AUTOINCREMENT,
            ProjetoId       INTEGER NULL REFERENCES Projeto (Id) ON DELETE SET NULL,
            -- Sem ON DELETE: rotina com instâncias não pode ser apagada (só desativada),
            -- senão a instância viraria tarefa avulsa com carry-over.
            RotinaId        INTEGER NULL REFERENCES Rotina (Id),
            Titulo          TEXT    NOT NULL CHECK (length(trim(Titulo)) BETWEEN 1 AND 200),
            Descricao       TEXT    NULL CHECK (Descricao IS NULL OR length(Descricao) <= 4000),
            DataInicio      INTEGER NOT NULL,              -- DateOnly.DayNumber
            DataFimPrevista INTEGER NULL,                  -- DateOnly.DayNumber
            Status          INTEGER NOT NULL CHECK (Status IN (0, 1, 2, 3)),
            DataConclusao   INTEGER NULL,                  -- DateOnly.DayNumber (Concluida ou Cancelada)
            CriadoEm        INTEGER NOT NULL,              -- ticks UTC
            AtualizadoEm    INTEGER NOT NULL,              -- ticks UTC
            CHECK (DataFimPrevista IS NULL OR DataFimPrevista >= DataInicio),
            CHECK ((Status IN (2, 3)) = (DataConclusao IS NOT NULL)),
            CHECK (DataConclusao IS NULL OR DataConclusao >= DataInicio),
            CHECK (RotinaId IS NULL OR DataFimPrevista = DataInicio)
        )
        """,
        "CREATE INDEX IX_Tarefa_ProjetoId ON Tarefa (ProjetoId)",
        "CREATE INDEX IX_Tarefa_DataInicio ON Tarefa (DataInicio)",
        // Extra (não pedido): relatórios filtram concluídas por dia.
        // Uma instância por rotina por dia: materialização idempotente (INSERT OR IGNORE).
        "CREATE UNIQUE INDEX UX_Tarefa_RotinaId_DataInicio ON Tarefa (RotinaId, DataInicio) WHERE RotinaId IS NOT NULL",
        "CREATE INDEX IX_Tarefa_DataConclusao ON Tarefa (DataConclusao) WHERE DataConclusao IS NOT NULL",
        """
        CREATE TABLE RegistroDiario (
            Id                INTEGER PRIMARY KEY AUTOINCREMENT,
            TarefaId          INTEGER NOT NULL REFERENCES Tarefa (Id) ON DELETE CASCADE,
            Data              INTEGER NOT NULL,            -- DateOnly.DayNumber
            TempoGastoMin     INTEGER NOT NULL CHECK (TempoGastoMin BETWEEN 0 AND 1440),
            SegundosResiduais INTEGER NOT NULL DEFAULT 0 CHECK (SegundosResiduais BETWEEN 0 AND 59),
            Observacao        TEXT    NULL CHECK (Observacao IS NULL OR length(Observacao) <= 1000)
        )
        """,
        // Pedido: índice (Data, TarefaId). UNIQUE garante "um registro por dia trabalhado".
        "CREATE UNIQUE INDEX UX_RegistroDiario_Data_TarefaId ON RegistroDiario (Data, TarefaId)",
        // Extra: FK lookup no ON DELETE CASCADE e no histórico por tarefa.
        "CREATE INDEX IX_RegistroDiario_TarefaId ON RegistroDiario (TarefaId, Data)",
        """
        CREATE TABLE OrdemDia (
            Data     INTEGER NOT NULL,
            TarefaId INTEGER NOT NULL REFERENCES Tarefa (Id) ON DELETE CASCADE,
            Posicao  INTEGER NOT NULL CHECK (Posicao >= 0),
            PRIMARY KEY (Data, TarefaId)
        ) WITHOUT ROWID
        """,
        "CREATE INDEX IX_OrdemDia_TarefaId_Data ON OrdemDia (TarefaId, Data)",
        """
        CREATE TABLE CronometroAtivo (
            Id        INTEGER PRIMARY KEY CHECK (Id = 1),  -- no máximo um cronômetro rodando
            TarefaId  INTEGER NOT NULL REFERENCES Tarefa (Id) ON DELETE CASCADE,
            InicioUtc INTEGER NOT NULL                     -- ticks UTC
        )
        """,
    ]);
}
