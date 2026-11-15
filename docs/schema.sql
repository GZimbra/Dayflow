-- Dayflow — esquema SQLite v1 (PRAGMA user_version = 1)
-- Gerado a partir de src/Dayflow.Core/Data/Migracoes/M001_Inicial.cs (fonte da verdade).
-- Convenções: DateOnly -> INTEGER (DateOnly.DayNumber, dias desde 0001-01-01);
--             timestamps -> INTEGER (DateTime.Ticks em UTC); enums -> INTEGER.
-- Conexão: PRAGMA foreign_keys = ON; journal_mode = WAL; synchronous = NORMAL.
-- Migrações: cada versão roda em transação e atualiza PRAGMA user_version. Nunca editar migração publicada.

CREATE TABLE Projeto (
    Id    INTEGER PRIMARY KEY AUTOINCREMENT,
    Nome  TEXT    NOT NULL CHECK (length(trim(Nome)) BETWEEN 1 AND 80),
    Cor   TEXT    NOT NULL CHECK (Cor GLOB '#[0-9A-F][0-9A-F][0-9A-F][0-9A-F][0-9A-F][0-9A-F]'),
    Ativo INTEGER NOT NULL DEFAULT 1 CHECK (Ativo IN (0, 1))
);

CREATE UNIQUE INDEX UX_Projeto_Nome ON Projeto (Nome COLLATE NOCASE);

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
);

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
);

CREATE INDEX IX_Tarefa_ProjetoId ON Tarefa (ProjetoId);

CREATE INDEX IX_Tarefa_DataInicio ON Tarefa (DataInicio);

CREATE UNIQUE INDEX UX_Tarefa_RotinaId_DataInicio ON Tarefa (RotinaId, DataInicio) WHERE RotinaId IS NOT NULL;

CREATE INDEX IX_Tarefa_DataConclusao ON Tarefa (DataConclusao) WHERE DataConclusao IS NOT NULL;

CREATE TABLE RegistroDiario (
    Id                INTEGER PRIMARY KEY AUTOINCREMENT,
    TarefaId          INTEGER NOT NULL REFERENCES Tarefa (Id) ON DELETE CASCADE,
    Data              INTEGER NOT NULL,            -- DateOnly.DayNumber
    TempoGastoMin     INTEGER NOT NULL CHECK (TempoGastoMin BETWEEN 0 AND 1440),
    SegundosResiduais INTEGER NOT NULL DEFAULT 0 CHECK (SegundosResiduais BETWEEN 0 AND 59),
    Observacao        TEXT    NULL CHECK (Observacao IS NULL OR length(Observacao) <= 1000)
);

CREATE UNIQUE INDEX UX_RegistroDiario_Data_TarefaId ON RegistroDiario (Data, TarefaId);

CREATE INDEX IX_RegistroDiario_TarefaId ON RegistroDiario (TarefaId, Data);

CREATE TABLE OrdemDia (
    Data     INTEGER NOT NULL,
    TarefaId INTEGER NOT NULL REFERENCES Tarefa (Id) ON DELETE CASCADE,
    Posicao  INTEGER NOT NULL CHECK (Posicao >= 0),
    PRIMARY KEY (Data, TarefaId)
) WITHOUT ROWID;

CREATE INDEX IX_OrdemDia_TarefaId_Data ON OrdemDia (TarefaId, Data);

CREATE TABLE CronometroAtivo (
    Id        INTEGER PRIMARY KEY CHECK (Id = 1),  -- no máximo um cronômetro rodando
    TarefaId  INTEGER NOT NULL REFERENCES Tarefa (Id) ON DELETE CASCADE,
    InicioUtc INTEGER NOT NULL                     -- ticks UTC
);


-- v2 (M002_RelatorioConclusao): relatório de conclusão 1:1 com Tarefa (só concluída + com projeto; regra no serviço)
CREATE TABLE RelatorioConclusao (
    TarefaId            INTEGER PRIMARY KEY REFERENCES Tarefa (Id) ON DELETE CASCADE,
    Resumo              TEXT    NULL CHECK (Resumo IS NULL OR length(Resumo) <= 4000),
    Beneficiados        TEXT    NULL CHECK (Beneficiados IS NULL OR length(Beneficiados) <= 200),
    MinutosEconomizados INTEGER NOT NULL DEFAULT 0 CHECK (MinutosEconomizados BETWEEN 0 AND 100000),
    Frequencia          INTEGER NOT NULL DEFAULT 0 CHECK (Frequencia IN (0, 1, 2, 3)), -- única, dia útil, semana, mês
    Pessoas             INTEGER NOT NULL DEFAULT 1 CHECK (Pessoas BETWEEN 1 AND 10000),
    CriadoEm            INTEGER NOT NULL,      -- ticks UTC
    AtualizadoEm        INTEGER NOT NULL       -- ticks UTC
) WITHOUT ROWID;

-- ── M003 (user_version 3): horário planejado da tarefa no dia ─────────────────
CREATE TABLE HorarioDia (
    TarefaId    INTEGER NOT NULL REFERENCES Tarefa(Id) ON DELETE CASCADE,
    Data        INTEGER NOT NULL,              -- yyyymmdd
    InicioMin   INTEGER NOT NULL CHECK (InicioMin BETWEEN 0 AND 1439),
    DuracaoMin  INTEGER NOT NULL DEFAULT 60 CHECK (DuracaoMin BETWEEN 15 AND 1440),
    PRIMARY KEY (TarefaId, Data),
    CHECK (InicioMin + DuracaoMin <= 1440)
) WITHOUT ROWID;
CREATE INDEX IX_HorarioDia_Data_Inicio ON HorarioDia (Data, InicioMin);
