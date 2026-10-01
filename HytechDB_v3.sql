-- ============================================================
-- HytechDB — SCRIPT COMPLETO (v3)
-- Correções sobre a v2:
--   1. Todo texto agora é NVARCHAR (emoji em Materia.icone, textos de aula etc.)
--   2. Fluxo de rascunho/pendente/aprovado também para Aula e Desafio
--   3. Anti-farm: questão/desafio/aula nunca "des-concluem" nem creditam 2x
--   4. Certificado também é verificado quando o aluno conclui a ÚLTIMA QUESTÃO
--      (na v2 só era verificado ao concluir aula) e só conta conteúdo aprovado
--   5. Desbloqueio de matéria com chaves (Aluno_Materia + Aluno.chaves_gastas)
--   6. Streak idempotente (Aluno.ultima_atividade)
--   7. Senha renomeada p/ senha_hash; login bloqueia usuário inativo
--   8. Tickets: destinatário resolvido pela matéria, status 'fechado'
--   9. Procedures de cadastro, login, perfil, listagens e dashboard admin
--
-- ATENÇÃO: este script CRIA o banco do zero. Se o HytechDB já existe e você
-- aceita perder os dados de teste, descomente o bloco de DROP abaixo.
-- ============================================================

-- USE master;
-- GO
-- IF DB_ID('HytechDB') IS NOT NULL
-- BEGIN
--     ALTER DATABASE HytechDB SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
--     DROP DATABASE HytechDB;
-- END
-- GO

CREATE DATABASE HytechDB
GO
USE HytechDB
GO

-- ============================================================
-- 1. TABELAS
-- ============================================================

CREATE TABLE Usuario (
    id_usuario      INT IDENTITY(1,1) PRIMARY KEY,
    nome            NVARCHAR(100) NOT NULL,
    nickname        NVARCHAR(50)  NOT NULL UNIQUE,          -- sempre com '@' na frente
    email           NVARCHAR(100) NOT NULL UNIQUE,          -- sempre em minúsculas
    senha_hash      NVARCHAR(255) NOT NULL,                 -- hash (BCrypt etc.), NUNCA texto puro
    data_nascimento DATE NULL,
    status          NVARCHAR(20)  NOT NULL DEFAULT N'ativo',
    foto_url        NVARCHAR(MAX) NULL,                     -- caminho/URL (ou data-URL, se o front enviar base64)
    data_cadastro   DATETIME      NOT NULL DEFAULT GETDATE(),
    CONSTRAINT CK_Usuario_Status CHECK (status IN (N'ativo', N'inativo'))
);
GO

CREATE TABLE SistemaMetricas (
    id_metrica        INT IDENTITY(1,1) PRIMARY KEY,
    data              DATE NOT NULL,
    total_usuarios    INT,
    media_engajamento DECIMAL(5,2),
    taxa_conversao    DECIMAL(5,2)
);
GO

CREATE TABLE TrilhaAprendizagem (
    id_trilha   INT IDENTITY(1,1) PRIMARY KEY,
    nome_trilha NVARCHAR(100) NOT NULL,
    descricao   NVARCHAR(MAX)
);
GO

-- Um usuário só pode ter UM papel (UNIQUE em id_usuario nas três tabelas)
CREATE TABLE Professor (
    id_professor     INT IDENTITY(1,1) PRIMARY KEY,
    id_usuario       INT NOT NULL UNIQUE,
    especialidade    NVARCHAR(100),
    criador_conteudo BIT NOT NULL DEFAULT 1,
    CONSTRAINT FK_Professor_Usuario FOREIGN KEY (id_usuario) REFERENCES Usuario(id_usuario)
);
GO

CREATE TABLE Aluno (
    id_aluno          INT IDENTITY(1,1) PRIMARY KEY,
    id_usuario        INT NOT NULL UNIQUE,
    pontos_acumulados INT NOT NULL DEFAULT 0,      -- total ganho na vida (usado no ranking/nível)
    chaves_gastas     INT NOT NULL DEFAULT 0,      -- total gasto desbloqueando matérias
    streak_atual      INT NOT NULL DEFAULT 0,
    ultima_atividade  DATE NULL,
    chaves_saldo AS (pontos_acumulados - chaves_gastas),   -- saldo disponível p/ gastar
    CONSTRAINT FK_Aluno_Usuario FOREIGN KEY (id_usuario) REFERENCES Usuario(id_usuario)
);
GO

CREATE TABLE Admin (
    id_admin   INT IDENTITY(1,1) PRIMARY KEY,
    id_usuario INT NOT NULL UNIQUE,
    CONSTRAINT FK_Admin_Usuario FOREIGN KEY (id_usuario) REFERENCES Usuario(id_usuario)
);
GO

CREATE TABLE Materia (
    id_materia              INT IDENTITY(1,1) PRIMARY KEY,
    id_professor            INT NOT NULL,
    id_trilha               INT NOT NULL,
    titulo                  NVARCHAR(100) NOT NULL,
    tipo                    NVARCHAR(50),
    status_aprovacao        NVARCHAR(20) NOT NULL DEFAULT N'aprovado',
    icone                   NVARCHAR(20) NULL,                -- emoji
    ordem                   INT NOT NULL DEFAULT 1,           -- posição da matéria dentro da trilha
    chaves_para_desbloquear INT NOT NULL DEFAULT 0,           -- 0 = liberada por padrão
    CONSTRAINT FK_Materia_Professor FOREIGN KEY (id_professor) REFERENCES Professor(id_professor),
    CONSTRAINT FK_Materia_Trilha    FOREIGN KEY (id_trilha)    REFERENCES TrilhaAprendizagem(id_trilha)
);
GO

CREATE TABLE Aluno_Trilha (
    id_Alu_Tri  INT IDENTITY(1,1) PRIMARY KEY,
    id_aluno    INT NOT NULL,
    id_trilha   INT NOT NULL,
    progresso   DECIMAL(5,2) NOT NULL DEFAULT 0,
    data_inicio DATE NOT NULL DEFAULT CAST(GETDATE() AS DATE),
    CONSTRAINT FK_AlunoTrilha_Aluno  FOREIGN KEY (id_aluno)  REFERENCES Aluno(id_aluno),
    CONSTRAINT FK_AlunoTrilha_Trilha FOREIGN KEY (id_trilha) REFERENCES TrilhaAprendizagem(id_trilha),
    CONSTRAINT UQ_AlunoTrilha UNIQUE (id_aluno, id_trilha)
);
GO

-- NOVO: matérias que o aluno destravou gastando chaves
CREATE TABLE Aluno_Materia (
    id_Alu_Mat       INT IDENTITY(1,1) PRIMARY KEY,
    id_aluno         INT NOT NULL,
    id_materia       INT NOT NULL,
    chaves_gastas    INT NOT NULL DEFAULT 0,
    data_desbloqueio DATETIME NOT NULL DEFAULT GETDATE(),
    CONSTRAINT FK_AlunoMateria_Aluno   FOREIGN KEY (id_aluno)   REFERENCES Aluno(id_aluno),
    CONSTRAINT FK_AlunoMateria_Materia FOREIGN KEY (id_materia) REFERENCES Materia(id_materia),
    CONSTRAINT UQ_AlunoMateria UNIQUE (id_aluno, id_materia)
);
GO

CREATE TABLE TicketSuporte (
    id_ticket               INT IDENTITY(1,1) PRIMARY KEY,
    id_usuario_solicitante  INT NOT NULL,
    id_usuario_destinatario INT NOT NULL,
    id_materia              INT NULL,
    assunto                 NVARCHAR(150) NOT NULL,
    descricao               NVARCHAR(MAX),
    status                  NVARCHAR(20) NOT NULL DEFAULT N'aberto',
    data_abertura           DATETIME NOT NULL DEFAULT GETDATE(),
    CONSTRAINT FK_Ticket_Solicitante  FOREIGN KEY (id_usuario_solicitante)  REFERENCES Usuario(id_usuario),
    CONSTRAINT FK_Ticket_Destinatario FOREIGN KEY (id_usuario_destinatario) REFERENCES Usuario(id_usuario),
    CONSTRAINT FK_Ticket_Materia      FOREIGN KEY (id_materia) REFERENCES Materia(id_materia),
    CONSTRAINT CK_Ticket_Status CHECK (status IN (N'aberto', N'respondido', N'fechado'))
);
GO

CREATE TABLE TicketMensagem (
    id_mensagem          INT IDENTITY(1,1) PRIMARY KEY,
    id_ticket            INT NOT NULL,
    id_usuario_remetente INT NOT NULL,
    texto                NVARCHAR(MAX) NOT NULL,
    data_hora            DATETIME NOT NULL DEFAULT GETDATE(),
    CONSTRAINT FK_TicketMensagem_Ticket  FOREIGN KEY (id_ticket) REFERENCES TicketSuporte(id_ticket),
    CONSTRAINT FK_TicketMensagem_Usuario FOREIGN KEY (id_usuario_remetente) REFERENCES Usuario(id_usuario)
);
GO

-- status_aprovacao: rascunho -> pendente -> aprovado | rejeitado
CREATE TABLE Aula (
    id_aula           INT IDENTITY(1,1) PRIMARY KEY,
    id_materia        INT NOT NULL,
    titulo            NVARCHAR(100) NOT NULL,
    conteudo          NVARCHAR(MAX),
    ordem             INT NULL,
    is_extra          BIT NOT NULL DEFAULT 0,        -- extra = opcional (não exigida p/ certificado)
    chaves_recompensa INT NOT NULL DEFAULT 0,
    exemplo_codigo    NVARCHAR(MAX) NULL,
    dica_titulo       NVARCHAR(150) NULL,
    dica_corpo        NVARCHAR(MAX) NULL,
    status_aprovacao  NVARCHAR(20) NOT NULL DEFAULT N'rascunho',
    CONSTRAINT FK_Aula_Materia FOREIGN KEY (id_materia) REFERENCES Materia(id_materia),
    CONSTRAINT CK_Aula_Status CHECK (status_aprovacao IN (N'rascunho', N'pendente', N'aprovado', N'rejeitado'))
);
GO

CREATE TABLE Aluno_Aula (
    id_Alu_Aula         INT IDENTITY(1,1) PRIMARY KEY,
    id_aluno            INT NOT NULL,
    id_aula             INT NOT NULL,
    progresso_concluido BIT NOT NULL DEFAULT 0,
    CONSTRAINT FK_AlunoAula_Aluno FOREIGN KEY (id_aluno) REFERENCES Aluno(id_aluno),
    CONSTRAINT FK_AlunoAula_Aula  FOREIGN KEY (id_aula)  REFERENCES Aula(id_aula),
    CONSTRAINT UQ_AlunoAula UNIQUE (id_aluno, id_aula)
);
GO

CREATE TABLE Questao (
    id_questao        INT IDENTITY(1,1) PRIMARY KEY,
    id_materia        INT NOT NULL,
    id_professor      INT NOT NULL,
    enunciado         NVARCHAR(MAX) NOT NULL,
    dificuldade       NVARCHAR(20) NOT NULL DEFAULT N'Fácil',
    chaves_recompensa INT NULL,                       -- se NULL, a trigger preenche via ChavesConfig
    status_aprovacao  NVARCHAR(20) NOT NULL DEFAULT N'rascunho',
    data_criacao      DATETIME NOT NULL DEFAULT GETDATE(),
    codigo_exemplo    NVARCHAR(MAX) NULL,
    CONSTRAINT FK_Questao_Materia   FOREIGN KEY (id_materia)   REFERENCES Materia(id_materia),
    CONSTRAINT FK_Questao_Professor FOREIGN KEY (id_professor) REFERENCES Professor(id_professor),
    CONSTRAINT CK_Questao_Dific  CHECK (dificuldade IN (N'Fácil', N'Médio', N'Difícil')),
    CONSTRAINT CK_Questao_Status CHECK (status_aprovacao IN (N'rascunho', N'pendente', N'aprovado', N'rejeitado'))
);
GO

CREATE TABLE Alternativa (
    id_alternativa INT IDENTITY(1,1) PRIMARY KEY,
    id_questao     INT NOT NULL,
    texto          NVARCHAR(255) NOT NULL,
    correta        BIT NOT NULL DEFAULT 0,
    CONSTRAINT FK_Alternativa_Questao FOREIGN KEY (id_questao) REFERENCES Questao(id_questao)
);
GO

CREATE TABLE Aluno_Questao (
    id_Alu_Que     INT IDENTITY(1,1) PRIMARY KEY,
    id_aluno       INT NOT NULL,
    id_questao     INT NOT NULL,
    concluida      BIT NOT NULL DEFAULT 0,
    data_conclusao DATETIME NULL,
    CONSTRAINT FK_AlunoQuestao_Aluno   FOREIGN KEY (id_aluno)   REFERENCES Aluno(id_aluno),
    CONSTRAINT FK_AlunoQuestao_Questao FOREIGN KEY (id_questao) REFERENCES Questao(id_questao),
    CONSTRAINT UQ_AlunoQuestao UNIQUE (id_aluno, id_questao)
);
GO

CREATE TABLE Desafio (
    id_desafio   INT IDENTITY(1,1) PRIMARY KEY,
    id_professor INT NOT NULL,
    id_materia   INT NOT NULL,
    titulo       NVARCHAR(150) NOT NULL,
    enunciado    NVARCHAR(MAX),
    dica         NVARCHAR(MAX),
    dificuldade  NVARCHAR(20) NOT NULL DEFAULT N'Fácil',
    status       NVARCHAR(20) NOT NULL DEFAULT N'rascunho',
    codigo_base  NVARCHAR(MAX) NULL,
    data_criacao DATETIME NOT NULL DEFAULT GETDATE(),
    CONSTRAINT FK_Desafio_Professor FOREIGN KEY (id_professor) REFERENCES Professor(id_professor),
    CONSTRAINT FK_Desafio_Materia   FOREIGN KEY (id_materia)   REFERENCES Materia(id_materia),
    CONSTRAINT CK_Desafio_Dific  CHECK (dificuldade IN (N'Fácil', N'Médio', N'Difícil')),
    CONSTRAINT CK_Desafio_Status CHECK (status IN (N'rascunho', N'pendente', N'aprovado', N'rejeitado'))
);
GO

CREATE TABLE Aluno_Desafio (
    id_Alu_Des     INT IDENTITY(1,1) PRIMARY KEY,
    id_aluno       INT NOT NULL,
    id_desafio     INT NOT NULL,
    concluido      BIT NOT NULL DEFAULT 0,
    data_conclusao DATETIME NULL,
    CONSTRAINT FK_AlunoDesafio_Aluno   FOREIGN KEY (id_aluno)   REFERENCES Aluno(id_aluno),
    CONSTRAINT FK_AlunoDesafio_Desafio FOREIGN KEY (id_desafio) REFERENCES Desafio(id_desafio),
    CONSTRAINT UQ_AlunoDesafio UNIQUE (id_aluno, id_desafio)
);
GO

-- Fila de moderação: tipo = 'aula' | 'questao' | 'desafio'
CREATE TABLE Aprovacao (
    id_aprovacao   INT IDENTITY(1,1) PRIMARY KEY,
    tipo           NVARCHAR(20) NOT NULL,
    id_aula        INT NULL,
    id_questao     INT NULL,
    id_desafio     INT NULL,
    id_professor   INT NOT NULL,
    status         NVARCHAR(20) NOT NULL DEFAULT N'pendente',
    data_submissao DATETIME NOT NULL DEFAULT GETDATE(),
    id_usuario_avaliador INT NULL,                 -- admin que decidiu
    data_avaliacao       DATETIME NULL,
    motivo_rejeicao      NVARCHAR(500) NULL,
    CONSTRAINT FK_Aprovacao_Aula      FOREIGN KEY (id_aula)      REFERENCES Aula(id_aula),
    CONSTRAINT FK_Aprovacao_Questao   FOREIGN KEY (id_questao)   REFERENCES Questao(id_questao),
    CONSTRAINT FK_Aprovacao_Desafio   FOREIGN KEY (id_desafio)   REFERENCES Desafio(id_desafio),
    CONSTRAINT FK_Aprovacao_Professor FOREIGN KEY (id_professor) REFERENCES Professor(id_professor),
    CONSTRAINT FK_Aprovacao_Avaliador FOREIGN KEY (id_usuario_avaliador) REFERENCES Usuario(id_usuario),
    CONSTRAINT CK_Aprovacao_Tipo   CHECK (tipo IN (N'aula', N'questao', N'desafio')),
    CONSTRAINT CK_Aprovacao_Status CHECK (status IN (N'pendente', N'aprovado', N'rejeitado'))
);
GO

CREATE TABLE Certificado (
    id_certificado INT IDENTITY(1,1) PRIMARY KEY,
    id_aluno       INT NOT NULL,
    id_materia     INT NOT NULL,
    data_emissao   DATETIME NOT NULL DEFAULT GETDATE(),
    CONSTRAINT FK_Certificado_Aluno   FOREIGN KEY (id_aluno)   REFERENCES Aluno(id_aluno),
    CONSTRAINT FK_Certificado_Materia FOREIGN KEY (id_materia) REFERENCES Materia(id_materia),
    CONSTRAINT UQ_Certificado UNIQUE (id_aluno, id_materia)
);
GO

CREATE TABLE LogAtividade (
    id_log     INT IDENTITY(1,1) PRIMARY KEY,
    id_usuario INT NULL,
    descricao  NVARCHAR(255),
    data_hora  DATETIME NOT NULL DEFAULT GETDATE(),
    CONSTRAINT FK_LogAtividade_Usuario FOREIGN KEY (id_usuario) REFERENCES Usuario(id_usuario)
);
GO

CREATE TABLE ChavesConfig (
    id_config              INT IDENTITY(1,1) PRIMARY KEY,
    chaves_questao_facil   INT NOT NULL,
    chaves_questao_media   INT NOT NULL,
    chaves_questao_dificil INT NOT NULL,
    chaves_certificado     INT NOT NULL
);
GO
INSERT INTO ChavesConfig VALUES (5, 10, 15, 50);
GO

-- Índices para as consultas mais frequentes
CREATE INDEX IX_Log_Usuario_Data   ON LogAtividade(id_usuario, data_hora);
CREATE INDEX IX_Aula_Materia       ON Aula(id_materia, status_aprovacao);
CREATE INDEX IX_Questao_Materia    ON Questao(id_materia, status_aprovacao);
CREATE INDEX IX_Ticket_Solic       ON TicketSuporte(id_usuario_solicitante);
CREATE INDEX IX_Ticket_Dest        ON TicketSuporte(id_usuario_destinatario);
CREATE INDEX IX_Msg_Ticket         ON TicketMensagem(id_ticket, data_hora);
CREATE INDEX IX_Aprovacao_Status   ON Aprovacao(status, data_submissao);
GO

-- Visão: papel de cada usuário (a API usa isso para montar o JWT)
CREATE VIEW vw_UsuarioPapel AS
SELECT U.id_usuario, U.nome, U.nickname, U.email, U.status, U.foto_url,
       CASE WHEN AD.id_admin IS NOT NULL THEN N'admin'
            WHEN PR.id_professor IS NOT NULL THEN N'professor'
            WHEN AL.id_aluno IS NOT NULL THEN N'aluno' END AS papel,
       AL.id_aluno, PR.id_professor, AD.id_admin
FROM Usuario U
LEFT JOIN Admin AD     ON AD.id_usuario = U.id_usuario
LEFT JOIN Professor PR ON PR.id_usuario = U.id_usuario
LEFT JOIN Aluno AL     ON AL.id_usuario = U.id_usuario;
GO

-- ============================================================
-- 2. TRIGGERS
-- ============================================================

-- 2.0 Preenche chaves_recompensa da questão com o valor da ChavesConfig
CREATE OR ALTER TRIGGER TR_Questao_PreencheRecompensa
ON Questao AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @f INT, @m INT, @d INT;
    SELECT TOP 1 @f = chaves_questao_facil, @m = chaves_questao_media, @d = chaves_questao_dificil FROM ChavesConfig;

    UPDATE Q
    SET chaves_recompensa = CASE Q.dificuldade WHEN N'Fácil' THEN ISNULL(@f,5)
                                               WHEN N'Médio' THEN ISNULL(@m,10)
                                               ELSE ISNULL(@d,15) END
    FROM Questao Q
    INNER JOIN inserted I ON I.id_questao = Q.id_questao
    WHERE Q.chaves_recompensa IS NULL;
END
GO

-- 2.1 Crédito de chaves ao concluir questão (só na transição 0 -> 1)
CREATE OR ALTER TRIGGER TR_AlunoQuestao_CreditaChaves
ON Aluno_Questao AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @T TABLE (id_aluno INT, id_questao INT, id_materia INT, chaves INT);
    INSERT INTO @T
    SELECT I.id_aluno, I.id_questao, Q.id_materia, ISNULL(Q.chaves_recompensa, 0)
    FROM inserted I
    INNER JOIN Questao Q ON Q.id_questao = I.id_questao
    LEFT JOIN deleted D  ON D.id_Alu_Que = I.id_Alu_Que
    WHERE I.concluida = 1 AND (D.id_Alu_Que IS NULL OR D.concluida = 0);

    IF NOT EXISTS (SELECT 1 FROM @T) RETURN;

    UPDATE A SET A.pontos_acumulados = A.pontos_acumulados + X.total
    FROM Aluno A
    INNER JOIN (SELECT id_aluno, SUM(chaves) AS total FROM @T GROUP BY id_aluno) X ON X.id_aluno = A.id_aluno;

    INSERT INTO LogAtividade (id_usuario, descricao, data_hora)
    SELECT A.id_usuario, N'Questão ' + CAST(T.id_questao AS NVARCHAR) + N' concluída (+' + CAST(T.chaves AS NVARCHAR) + N' chaves)', GETDATE()
    FROM @T T INNER JOIN Aluno A ON A.id_aluno = T.id_aluno;

    -- a última questão pode ser o que faltava para o certificado
    CREATE TABLE #ParesCert (id_aluno INT, id_materia INT);
    INSERT INTO #ParesCert SELECT DISTINCT id_aluno, id_materia FROM @T;
    EXEC sp_ProcessarCertificados;
END
GO

-- 2.2 Crédito ao concluir desafio (valores vêm de ChavesConfig)
CREATE OR ALTER TRIGGER TR_AlunoDesafio_CreditaChaves
ON Aluno_Desafio AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @f INT, @m INT, @d INT;
    SELECT TOP 1 @f = chaves_questao_facil, @m = chaves_questao_media, @d = chaves_questao_dificil FROM ChavesConfig;
    SET @f = ISNULL(@f,5); SET @m = ISNULL(@m,10); SET @d = ISNULL(@d,15);

    DECLARE @T TABLE (id_aluno INT, id_desafio INT, dificuldade NVARCHAR(20), chaves INT);
    INSERT INTO @T
    SELECT I.id_aluno, I.id_desafio, Dsf.dificuldade,
           CASE Dsf.dificuldade WHEN N'Fácil' THEN @f WHEN N'Médio' THEN @m ELSE @d END
    FROM inserted I
    INNER JOIN Desafio Dsf ON Dsf.id_desafio = I.id_desafio
    LEFT JOIN deleted D    ON D.id_Alu_Des = I.id_Alu_Des
    WHERE I.concluido = 1 AND (D.id_Alu_Des IS NULL OR D.concluido = 0);

    IF NOT EXISTS (SELECT 1 FROM @T) RETURN;

    UPDATE A SET A.pontos_acumulados = A.pontos_acumulados + X.total
    FROM Aluno A
    INNER JOIN (SELECT id_aluno, SUM(chaves) AS total FROM @T GROUP BY id_aluno) X ON X.id_aluno = A.id_aluno;

    INSERT INTO LogAtividade (id_usuario, descricao, data_hora)
    SELECT A.id_usuario, N'Desafio ' + CAST(T.id_desafio AS NVARCHAR) + N' concluído (' + T.dificuldade + N', +' + CAST(T.chaves AS NVARCHAR) + N' chaves)', GETDATE()
    FROM @T T INNER JOIN Aluno A ON A.id_aluno = T.id_aluno;
END
GO

-- 2.3 Questão entra na fila quando passa a 'pendente' (insert OU update rascunho->pendente)
CREATE OR ALTER TRIGGER TR_Questao_GeraAprovacao
ON Questao AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @N TABLE (id_questao INT, id_professor INT, enunciado NVARCHAR(MAX));
    INSERT INTO @N
    SELECT I.id_questao, I.id_professor, I.enunciado
    FROM inserted I
    LEFT JOIN deleted D ON D.id_questao = I.id_questao
    WHERE I.status_aprovacao = N'pendente' AND (D.id_questao IS NULL OR D.status_aprovacao <> N'pendente');

    INSERT INTO Aprovacao (tipo, id_questao, id_professor, status, data_submissao)
    SELECT N'questao', id_questao, id_professor, N'pendente', GETDATE() FROM @N;

    INSERT INTO LogAtividade (id_usuario, descricao, data_hora)
    SELECT U.id_usuario, N'CONTEÚDO ENVIADO (questão): ' + LEFT(ISNULL(N.enunciado, N''), 60), GETDATE()
    FROM @N N
    INNER JOIN Professor P ON P.id_professor = N.id_professor
    INNER JOIN Usuario U   ON U.id_usuario = P.id_usuario;
END
GO

-- 2.4 Idem para aulas (antes toda aula criada entrava na fila, até rascunho)
CREATE OR ALTER TRIGGER TR_Aula_GeraAprovacao
ON Aula AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @N TABLE (id_aula INT, id_professor INT, titulo NVARCHAR(100));
    INSERT INTO @N
    SELECT I.id_aula, M.id_professor, I.titulo
    FROM inserted I
    INNER JOIN Materia M ON M.id_materia = I.id_materia
    LEFT JOIN deleted D  ON D.id_aula = I.id_aula
    WHERE I.status_aprovacao = N'pendente' AND (D.id_aula IS NULL OR D.status_aprovacao <> N'pendente');

    INSERT INTO Aprovacao (tipo, id_aula, id_professor, status, data_submissao)
    SELECT N'aula', id_aula, id_professor, N'pendente', GETDATE() FROM @N;

    INSERT INTO LogAtividade (id_usuario, descricao, data_hora)
    SELECT U.id_usuario, N'CONTEÚDO ENVIADO (aula): ' + LEFT(ISNULL(N.titulo, N''), 60), GETDATE()
    FROM @N N
    INNER JOIN Professor P ON P.id_professor = N.id_professor
    INNER JOIN Usuario U   ON U.id_usuario = P.id_usuario;
END
GO

-- 2.4b Idem para desafios
CREATE OR ALTER TRIGGER TR_Desafio_GeraAprovacao
ON Desafio AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @N TABLE (id_desafio INT, id_professor INT, titulo NVARCHAR(150));
    INSERT INTO @N
    SELECT I.id_desafio, I.id_professor, I.titulo
    FROM inserted I
    LEFT JOIN deleted D ON D.id_desafio = I.id_desafio
    WHERE I.status = N'pendente' AND (D.id_desafio IS NULL OR D.status <> N'pendente');

    INSERT INTO Aprovacao (tipo, id_desafio, id_professor, status, data_submissao)
    SELECT N'desafio', id_desafio, id_professor, N'pendente', GETDATE() FROM @N;

    INSERT INTO LogAtividade (id_usuario, descricao, data_hora)
    SELECT U.id_usuario, N'CONTEÚDO ENVIADO (desafio): ' + LEFT(ISNULL(N.titulo, N''), 60), GETDATE()
    FROM @N N
    INNER JOIN Professor P ON P.id_professor = N.id_professor
    INNER JOIN Usuario U   ON U.id_usuario = P.id_usuario;
END
GO

-- 2.5 Decisão do admin reflete no item de origem (questão, aula OU desafio) e loga
CREATE OR ALTER TRIGGER TR_Aprovacao_SincronizaStatus
ON Aprovacao AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT UPDATE(status) RETURN;

    DECLARE @Mud TABLE (tipo NVARCHAR(20), id_item INT, id_professor INT, status NVARCHAR(20));
    INSERT INTO @Mud
    SELECT I.tipo, COALESCE(I.id_questao, I.id_aula, I.id_desafio), I.id_professor, I.status
    FROM inserted I
    INNER JOIN deleted D ON D.id_aprovacao = I.id_aprovacao
    WHERE I.status <> D.status AND I.status IN (N'aprovado', N'rejeitado');

    UPDATE Q SET Q.status_aprovacao = M.status
    FROM Questao Q INNER JOIN @Mud M ON M.tipo = N'questao' AND M.id_item = Q.id_questao;

    UPDATE A SET A.status_aprovacao = M.status
    FROM Aula A INNER JOIN @Mud M ON M.tipo = N'aula' AND M.id_item = A.id_aula;

    UPDATE Dsf SET Dsf.status = M.status
    FROM Desafio Dsf INNER JOIN @Mud M ON M.tipo = N'desafio' AND M.id_item = Dsf.id_desafio;

    INSERT INTO LogAtividade (id_usuario, descricao, data_hora)
    SELECT U.id_usuario,
           UPPER(M.tipo) + CASE WHEN M.status = N'aprovado' THEN N' APROVADO(A): #' ELSE N' REJEITADO(A): #' END + CAST(M.id_item AS NVARCHAR),
           GETDATE()
    FROM @Mud M
    INNER JOIN Professor P ON P.id_professor = M.id_professor
    INNER JOIN Usuario U   ON U.id_usuario = P.id_usuario;
END
GO

-- 2.6 Mensagem no ticket: resposta de terceiros => 'respondido'; solicitante => 'aberto'
--     Ticket 'fechado' só reabre se o próprio solicitante escrever
CREATE OR ALTER TRIGGER TR_TicketMensagem_AtualizaStatus
ON TicketMensagem AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE T
    SET T.status = CASE WHEN I.id_usuario_remetente = T.id_usuario_solicitante THEN N'aberto' ELSE N'respondido' END
    FROM TicketSuporte T
    INNER JOIN inserted I ON I.id_ticket = T.id_ticket
    WHERE T.status <> N'fechado' OR I.id_usuario_remetente = T.id_usuario_solicitante;
END
GO

-- 2.7 Aula concluída: credita chaves da aula (quando houver) e verifica certificado
CREATE OR ALTER TRIGGER TR_AlunoAula_Conclusao
ON Aluno_Aula AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @T TABLE (id_aluno INT, id_aula INT, id_materia INT, chaves INT);
    INSERT INTO @T
    SELECT I.id_aluno, I.id_aula, Au.id_materia, Au.chaves_recompensa
    FROM inserted I
    INNER JOIN Aula Au  ON Au.id_aula = I.id_aula
    LEFT JOIN deleted D ON D.id_Alu_Aula = I.id_Alu_Aula
    WHERE I.progresso_concluido = 1 AND (D.id_Alu_Aula IS NULL OR D.progresso_concluido = 0);

    IF NOT EXISTS (SELECT 1 FROM @T) RETURN;

    UPDATE A SET A.pontos_acumulados = A.pontos_acumulados + X.total
    FROM Aluno A
    INNER JOIN (SELECT id_aluno, SUM(chaves) AS total FROM @T WHERE chaves > 0 GROUP BY id_aluno) X ON X.id_aluno = A.id_aluno;

    CREATE TABLE #ParesCert (id_aluno INT, id_materia INT);
    INSERT INTO #ParesCert SELECT DISTINCT id_aluno, id_materia FROM @T;
    EXEC sp_ProcessarCertificados;
END
GO

-- 2.8 Impede excluir matéria com aulas/questões/desafios
CREATE OR ALTER TRIGGER TR_Materia_ImpedeExclusao
ON Materia INSTEAD OF DELETE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM deleted D
               WHERE EXISTS (SELECT 1 FROM Aula A WHERE A.id_materia = D.id_materia)
                  OR EXISTS (SELECT 1 FROM Questao Q WHERE Q.id_materia = D.id_materia)
                  OR EXISTS (SELECT 1 FROM Desafio X WHERE X.id_materia = D.id_materia))
    BEGIN
        RAISERROR(N'Não é possível excluir uma matéria com aulas, questões ou desafios vinculados.', 16, 1);
        RETURN;
    END
    DELETE FROM Materia WHERE id_materia IN (SELECT id_materia FROM deleted);
END
GO

-- 2.9 / 2.10 Log de cadastro
CREATE OR ALTER TRIGGER TR_Aluno_LogCadastro
ON Aluno AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO LogAtividade (id_usuario, descricao, data_hora)
    SELECT U.id_usuario, N'CADASTRO: ' + U.nome + N' — Novo Aluno', GETDATE()
    FROM inserted I INNER JOIN Usuario U ON U.id_usuario = I.id_usuario;
END
GO

CREATE OR ALTER TRIGGER TR_Professor_LogCadastro
ON Professor AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO LogAtividade (id_usuario, descricao, data_hora)
    SELECT U.id_usuario, N'CADASTRO: ' + U.nome + N' — Novo Professor', GETDATE()
    FROM inserted I INNER JOIN Usuario U ON U.id_usuario = I.id_usuario;
END
GO

-- 2.11 Log de ativação/inativação
CREATE OR ALTER TRIGGER TR_Usuario_LogStatus
ON Usuario AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF UPDATE(status)
        INSERT INTO LogAtividade (id_usuario, descricao, data_hora)
        SELECT I.id_usuario,
               N'USUÁRIO ' + CASE WHEN I.status = N'ativo' THEN N'ATIVADO' ELSE N'INATIVADO' END + N': ' + I.nome,
               GETDATE()
        FROM inserted I INNER JOIN deleted D ON D.id_usuario = I.id_usuario
        WHERE I.status <> D.status;
END
GO

-- ============================================================
-- 3. STORED PROCEDURES
-- ============================================================

-- 3.0 (interna) Emite certificados para os pares em #ParesCert (criada pelas triggers).
--     Regras: matéria tem >= 1 aula aprovada; todas as aulas aprovadas NÃO-extras e todas as
--     questões aprovadas da matéria estão concluídas; 1 certificado por aluno/matéria.
CREATE OR ALTER PROCEDURE sp_ProcessarCertificados
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @chaves INT;
    SELECT TOP 1 @chaves = chaves_certificado FROM ChavesConfig;
    SET @chaves = ISNULL(@chaves, 50);

    DECLARE @Novos TABLE (id_aluno INT, id_materia INT);

    INSERT INTO Certificado (id_aluno, id_materia, data_emissao)
    OUTPUT inserted.id_aluno, inserted.id_materia INTO @Novos
    SELECT P.id_aluno, P.id_materia, GETDATE()
    FROM #ParesCert P
    WHERE NOT EXISTS (SELECT 1 FROM Certificado C WHERE C.id_aluno = P.id_aluno AND C.id_materia = P.id_materia)
      AND EXISTS (SELECT 1 FROM Aula Au WHERE Au.id_materia = P.id_materia
                    AND Au.status_aprovacao = N'aprovado' AND Au.is_extra = 0)
      AND NOT EXISTS (
            SELECT 1 FROM Aula Au2
            LEFT JOIN Aluno_Aula AA ON AA.id_aula = Au2.id_aula AND AA.id_aluno = P.id_aluno AND AA.progresso_concluido = 1
            WHERE Au2.id_materia = P.id_materia AND Au2.status_aprovacao = N'aprovado'
              AND Au2.is_extra = 0 AND AA.id_Alu_Aula IS NULL)
      AND NOT EXISTS (
            SELECT 1 FROM Questao Q2
            LEFT JOIN Aluno_Questao AQ ON AQ.id_questao = Q2.id_questao AND AQ.id_aluno = P.id_aluno AND AQ.concluida = 1
            WHERE Q2.id_materia = P.id_materia AND Q2.status_aprovacao = N'aprovado' AND AQ.id_Alu_Que IS NULL);

    UPDATE A SET A.pontos_acumulados = A.pontos_acumulados + @chaves
    FROM Aluno A INNER JOIN @Novos N ON N.id_aluno = A.id_aluno;

    INSERT INTO LogAtividade (id_usuario, descricao, data_hora)
    SELECT A.id_usuario, N'CERTIFICADO EMITIDO: matéria #' + CAST(N.id_materia AS NVARCHAR) + N' (+' + CAST(@chaves AS NVARCHAR) + N' chaves)', GETDATE()
    FROM @Novos N INNER JOIN Aluno A ON A.id_aluno = N.id_aluno;
END
GO

-- 3.1 Cadastro público de aluno (Usuario + Aluno em uma transação).
--     A API gera o hash da senha antes de chamar.
CREATE OR ALTER PROCEDURE sp_CadastrarAluno
    @nome NVARCHAR(100), @nickname NVARCHAR(50), @email NVARCHAR(100),
    @senha_hash NVARCHAR(255), @data_nascimento DATE = NULL
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    SET @email = LOWER(LTRIM(RTRIM(@email)));
    SET @nickname = LTRIM(RTRIM(@nickname));
    IF LEFT(@nickname,1) <> N'@' SET @nickname = N'@' + @nickname;

    IF EXISTS (SELECT 1 FROM Usuario WHERE email = @email)
    BEGIN RAISERROR(N'Este e-mail já está cadastrado.', 16, 1); RETURN; END
    IF EXISTS (SELECT 1 FROM Usuario WHERE nickname = @nickname)
    BEGIN RAISERROR(N'Este nome de usuário já está em uso.', 16, 1); RETURN; END

    BEGIN TRAN;
        INSERT INTO Usuario (nome, nickname, email, senha_hash, data_nascimento)
        VALUES (@nome, @nickname, @email, @senha_hash, @data_nascimento);
        DECLARE @id_usuario INT = SCOPE_IDENTITY();
        INSERT INTO Aluno (id_usuario) VALUES (@id_usuario);
        DECLARE @id_aluno INT = SCOPE_IDENTITY();
    COMMIT;

    SELECT @id_usuario AS id_usuario, @id_aluno AS id_aluno;
END
GO

-- 3.1b Cadastro de professor (usado pelo desktop/admin)
CREATE OR ALTER PROCEDURE sp_CadastrarProfessor
    @nome NVARCHAR(100), @nickname NVARCHAR(50), @email NVARCHAR(100),
    @senha_hash NVARCHAR(255), @especialidade NVARCHAR(100) = NULL, @data_nascimento DATE = NULL
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    SET @email = LOWER(LTRIM(RTRIM(@email)));
    SET @nickname = LTRIM(RTRIM(@nickname));
    IF LEFT(@nickname,1) <> N'@' SET @nickname = N'@' + @nickname;

    IF EXISTS (SELECT 1 FROM Usuario WHERE email = @email)
    BEGIN RAISERROR(N'Este e-mail já está cadastrado.', 16, 1); RETURN; END
    IF EXISTS (SELECT 1 FROM Usuario WHERE nickname = @nickname)
    BEGIN RAISERROR(N'Este nome de usuário já está em uso.', 16, 1); RETURN; END

    BEGIN TRAN;
        INSERT INTO Usuario (nome, nickname, email, senha_hash, data_nascimento)
        VALUES (@nome, @nickname, @email, @senha_hash, @data_nascimento);
        DECLARE @id_usuario INT = SCOPE_IDENTITY();
        INSERT INTO Professor (id_usuario, especialidade, criador_conteudo) VALUES (@id_usuario, @especialidade, 1);
        DECLARE @id_prof INT = SCOPE_IDENTITY();
    COMMIT;

    SELECT @id_usuario AS id_usuario, @id_prof AS id_professor;
END
GO

-- 3.1c Cadastro de admin (rodar manualmente / seed; não exposto no site)
CREATE OR ALTER PROCEDURE sp_CadastrarAdmin
    @nome NVARCHAR(100), @nickname NVARCHAR(50), @email NVARCHAR(100), @senha_hash NVARCHAR(255)
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    SET @email = LOWER(LTRIM(RTRIM(@email)));
    IF LEFT(@nickname,1) <> N'@' SET @nickname = N'@' + @nickname;
    IF EXISTS (SELECT 1 FROM Usuario WHERE email = @email OR nickname = @nickname)
    BEGIN RAISERROR(N'E-mail ou nickname já cadastrado.', 16, 1); RETURN; END

    BEGIN TRAN;
        INSERT INTO Usuario (nome, nickname, email, senha_hash) VALUES (@nome, @nickname, @email, @senha_hash);
        DECLARE @id_usuario INT = SCOPE_IDENTITY();
        INSERT INTO Admin (id_usuario) VALUES (@id_usuario);
    COMMIT;
    SELECT @id_usuario AS id_usuario;
END
GO

-- 3.2 Login: devolve o hash e o papel; a API confere a senha e rejeita se status = 'inativo'.
--     (devolver o usuário inativo permite mostrar a mensagem certa: "conta inativa")
CREATE OR ALTER PROCEDURE sp_ObterUsuarioParaLogin
    @email NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT V.id_usuario, V.nome, V.nickname, V.email, V.status, V.foto_url, V.papel,
           V.id_aluno, V.id_professor, V.id_admin, U.senha_hash
    FROM vw_UsuarioPapel V INNER JOIN Usuario U ON U.id_usuario = V.id_usuario
    WHERE V.email = LOWER(LTRIM(RTRIM(@email)));
END
GO

-- 3.3 Registra login (e atualiza o streak se for aluno)
CREATE OR ALTER PROCEDURE sp_RegistrarLogin
    @id_usuario INT
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO LogAtividade (id_usuario, descricao, data_hora)
    SELECT id_usuario, N'LOGIN: ' + nome, GETDATE() FROM Usuario WHERE id_usuario = @id_usuario;

    DECLARE @id_aluno INT = (SELECT id_aluno FROM Aluno WHERE id_usuario = @id_usuario);
    IF @id_aluno IS NOT NULL EXEC sp_AtualizarStreak @id_aluno;
END
GO

-- 3.4 Streak idempotente: só avança 1x por dia
CREATE OR ALTER PROCEDURE sp_AtualizarStreak
    @id_aluno INT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @hoje DATE = CAST(GETDATE() AS DATE);
    UPDATE Aluno
    SET streak_atual = CASE WHEN ultima_atividade = DATEADD(DAY, -1, @hoje) THEN streak_atual + 1 ELSE 1 END,
        ultima_atividade = @hoje
    WHERE id_aluno = @id_aluno
      AND (ultima_atividade IS NULL OR ultima_atividade < @hoje);
END
GO

-- 3.5 Aluno responde questão. Nunca regride 'concluida' => sem farm de chaves.
CREATE OR ALTER PROCEDURE sp_ResponderQuestao
    @id_aluno INT, @id_questao INT, @id_alternativa INT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @acertou BIT;

    IF NOT EXISTS (SELECT 1 FROM Questao WHERE id_questao = @id_questao AND status_aprovacao = N'aprovado')
    BEGIN RAISERROR(N'Questão indisponível.', 16, 1); RETURN; END

    SELECT @acertou = correta FROM Alternativa WHERE id_alternativa = @id_alternativa AND id_questao = @id_questao;
    IF @acertou IS NULL
    BEGIN RAISERROR(N'Alternativa não pertence a essa questão.', 16, 1); RETURN; END

    DECLARE @ja_concluida BIT = ISNULL((SELECT concluida FROM Aluno_Questao WHERE id_aluno = @id_aluno AND id_questao = @id_questao), 0);

    IF NOT EXISTS (SELECT 1 FROM Aluno_Questao WHERE id_aluno = @id_aluno AND id_questao = @id_questao)
        INSERT INTO Aluno_Questao (id_aluno, id_questao, concluida, data_conclusao)
        VALUES (@id_aluno, @id_questao, @acertou, CASE WHEN @acertou = 1 THEN GETDATE() END);
    ELSE IF @acertou = 1 AND @ja_concluida = 0
        UPDATE Aluno_Questao SET concluida = 1, data_conclusao = GETDATE()
        WHERE id_aluno = @id_aluno AND id_questao = @id_questao;

    EXEC sp_AtualizarStreak @id_aluno;

    SELECT @acertou AS acertou, @ja_concluida AS ja_estava_concluida;
END
GO

-- 3.6 Conclui aula (idempotente)
CREATE OR ALTER PROCEDURE sp_ConcluirAula
    @id_aluno INT, @id_aula INT
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM Aula WHERE id_aula = @id_aula AND status_aprovacao = N'aprovado')
    BEGIN RAISERROR(N'Aula indisponível.', 16, 1); RETURN; END

    IF NOT EXISTS (SELECT 1 FROM Aluno_Aula WHERE id_aluno = @id_aluno AND id_aula = @id_aula)
        INSERT INTO Aluno_Aula (id_aluno, id_aula, progresso_concluido) VALUES (@id_aluno, @id_aula, 1);
    ELSE
        UPDATE Aluno_Aula SET progresso_concluido = 1
        WHERE id_aluno = @id_aluno AND id_aula = @id_aula AND progresso_concluido = 0;

    EXEC sp_AtualizarStreak @id_aluno;
END
GO

-- 3.7 Conclui desafio (idempotente; só desafio aprovado)
CREATE OR ALTER PROCEDURE sp_ConcluirDesafio
    @id_aluno INT, @id_desafio INT
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM Desafio WHERE id_desafio = @id_desafio AND status = N'aprovado')
    BEGIN RAISERROR(N'Desafio indisponível.', 16, 1); RETURN; END

    IF NOT EXISTS (SELECT 1 FROM Aluno_Desafio WHERE id_aluno = @id_aluno AND id_desafio = @id_desafio)
        INSERT INTO Aluno_Desafio (id_aluno, id_desafio, concluido, data_conclusao) VALUES (@id_aluno, @id_desafio, 1, GETDATE());
    ELSE
        UPDATE Aluno_Desafio SET concluido = 1, data_conclusao = GETDATE()
        WHERE id_aluno = @id_aluno AND id_desafio = @id_desafio AND concluido = 0;

    EXEC sp_AtualizarStreak @id_aluno;
END
GO

-- 3.8 Desbloqueia matéria gastando chaves (botão "Usar X chaves para desbloquear")
CREATE OR ALTER PROCEDURE sp_DesbloquearMateria
    @id_aluno INT, @id_materia INT
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    DECLARE @custo INT, @trilha INT, @ordem INT;
    SELECT @custo = chaves_para_desbloquear, @trilha = id_trilha, @ordem = ordem FROM Materia WHERE id_materia = @id_materia;
    IF @custo IS NULL BEGIN RAISERROR(N'Matéria não encontrada.', 16, 1); RETURN; END

    IF @custo = 0 OR EXISTS (SELECT 1 FROM Aluno_Materia WHERE id_aluno = @id_aluno AND id_materia = @id_materia)
    BEGIN SELECT CAST(1 AS BIT) AS desbloqueada, 0 AS chaves_gastas; RETURN; END

    -- exige certificado da matéria anterior da mesma trilha
    DECLARE @anterior INT = (SELECT TOP 1 id_materia FROM Materia
                             WHERE id_trilha = @trilha AND ordem < @ordem ORDER BY ordem DESC);
    IF @anterior IS NOT NULL AND NOT EXISTS (SELECT 1 FROM Certificado WHERE id_aluno = @id_aluno AND id_materia = @anterior)
    BEGIN RAISERROR(N'Conclua a matéria anterior antes de desbloquear esta.', 16, 1); RETURN; END

    BEGIN TRAN;
        UPDATE Aluno SET chaves_gastas = chaves_gastas + @custo
        WHERE id_aluno = @id_aluno AND (pontos_acumulados - chaves_gastas) >= @custo;
        IF @@ROWCOUNT = 0
        BEGIN ROLLBACK; RAISERROR(N'Chaves insuficientes.', 16, 1); RETURN; END

        INSERT INTO Aluno_Materia (id_aluno, id_materia, chaves_gastas) VALUES (@id_aluno, @id_materia, @custo);

        INSERT INTO LogAtividade (id_usuario, descricao, data_hora)
        SELECT id_usuario, N'MATÉRIA DESBLOQUEADA: #' + CAST(@id_materia AS NVARCHAR) + N' (−' + CAST(@custo AS NVARCHAR) + N' chaves)', GETDATE()
        FROM Aluno WHERE id_aluno = @id_aluno;
    COMMIT;

    SELECT CAST(1 AS BIT) AS desbloqueada, @custo AS chaves_gastas;
END
GO

-- 3.9 Matérias da trilha para o aluno (com flag de desbloqueio e certificado)
CREATE OR ALTER PROCEDURE sp_ListarMateriasAluno
    @id_aluno INT, @id_trilha INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT M.id_materia, M.titulo, M.icone, M.ordem, M.chaves_para_desbloquear,
           CAST(CASE WHEN M.chaves_para_desbloquear = 0 OR AM.id_Alu_Mat IS NOT NULL THEN 1 ELSE 0 END AS BIT) AS desbloqueada,
           CAST(CASE WHEN C.id_certificado IS NOT NULL THEN 1 ELSE 0 END AS BIT) AS concluida
    FROM Materia M
    LEFT JOIN Aluno_Materia AM ON AM.id_materia = M.id_materia AND AM.id_aluno = @id_aluno
    LEFT JOIN Certificado C    ON C.id_materia  = M.id_materia AND C.id_aluno  = @id_aluno
    WHERE M.id_trilha = @id_trilha AND M.status_aprovacao = N'aprovado'
    ORDER BY M.ordem;
END
GO

-- 3.10 Professor: cadastra questão + alternativas em uma transação.
--      @alternativas_json = [{"texto":"...","correta":true}, ...]
--      @enviar = 1 => status 'pendente' (entra na fila); 0 => 'rascunho'
CREATE OR ALTER PROCEDURE sp_CadastrarQuestao
    @id_professor INT, @id_materia INT, @enunciado NVARCHAR(MAX), @dificuldade NVARCHAR(20),
    @codigo_exemplo NVARCHAR(MAX) = NULL, @alternativas_json NVARCHAR(MAX), @enviar BIT = 0
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;

    DECLARE @alts TABLE (texto NVARCHAR(255), correta BIT);
    INSERT INTO @alts SELECT texto, correta FROM OPENJSON(@alternativas_json)
        WITH (texto NVARCHAR(255) '$.texto', correta BIT '$.correta');

    IF (SELECT COUNT(*) FROM @alts) < 2
    BEGIN RAISERROR(N'Informe pelo menos 2 alternativas.', 16, 1); RETURN; END
    IF (SELECT COUNT(*) FROM @alts WHERE correta = 1) <> 1
    BEGIN RAISERROR(N'Marque exatamente 1 alternativa correta.', 16, 1); RETURN; END

    BEGIN TRAN;
        INSERT INTO Questao (id_materia, id_professor, enunciado, dificuldade, codigo_exemplo, status_aprovacao)
        VALUES (@id_materia, @id_professor, @enunciado, @dificuldade, @codigo_exemplo,
                CASE WHEN @enviar = 1 THEN N'pendente' ELSE N'rascunho' END);
        DECLARE @id INT = SCOPE_IDENTITY();
        INSERT INTO Alternativa (id_questao, texto, correta) SELECT @id, texto, ISNULL(correta,0) FROM @alts;
    COMMIT;

    SELECT @id AS id_questao;
END
GO

-- 3.11 Professor envia um rascunho (aula | questao | desafio) para aprovação
CREATE OR ALTER PROCEDURE sp_EnviarParaAprovacao
    @tipo NVARCHAR(20), @id_item INT, @id_professor INT
AS
BEGIN
    SET NOCOUNT ON;
    IF @tipo = N'questao'
        UPDATE Questao SET status_aprovacao = N'pendente'
        WHERE id_questao = @id_item AND id_professor = @id_professor AND status_aprovacao IN (N'rascunho', N'rejeitado');
    ELSE IF @tipo = N'aula'
        UPDATE Au SET status_aprovacao = N'pendente'
        FROM Aula Au INNER JOIN Materia M ON M.id_materia = Au.id_materia
        WHERE Au.id_aula = @id_item AND M.id_professor = @id_professor AND Au.status_aprovacao IN (N'rascunho', N'rejeitado');
    ELSE IF @tipo = N'desafio'
        UPDATE Desafio SET status = N'pendente'
        WHERE id_desafio = @id_item AND id_professor = @id_professor AND status IN (N'rascunho', N'rejeitado');
    ELSE
    BEGIN RAISERROR(N'Tipo inválido.', 16, 1); RETURN; END

    IF @@ROWCOUNT = 0 RAISERROR(N'Item não encontrado, não é seu, ou já foi enviado.', 16, 1);
END
GO

-- 3.12 Admin aprova/rejeita (dispara TR_Aprovacao_SincronizaStatus)
CREATE OR ALTER PROCEDURE sp_AvaliarAprovacao
    @id_aprovacao INT, @aprovado BIT, @id_usuario_admin INT, @motivo NVARCHAR(500) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM Admin WHERE id_usuario = @id_usuario_admin)
    BEGIN RAISERROR(N'Apenas administradores podem avaliar.', 16, 1); RETURN; END

    UPDATE Aprovacao
    SET status = CASE WHEN @aprovado = 1 THEN N'aprovado' ELSE N'rejeitado' END,
        id_usuario_avaliador = @id_usuario_admin, data_avaliacao = GETDATE(),
        motivo_rejeicao = CASE WHEN @aprovado = 0 THEN @motivo END
    WHERE id_aprovacao = @id_aprovacao AND status = N'pendente';

    IF @@ROWCOUNT = 0 RAISERROR(N'Aprovação não encontrada ou já avaliada.', 16, 1);
END
GO

-- 3.13 Fila do admin
CREATE OR ALTER PROCEDURE sp_ListarAprovacoesPendentes
AS
BEGIN
    SET NOCOUNT ON;
    SELECT A.id_aprovacao, A.tipo, A.data_submissao, U.nome AS professor,
           COALESCE(Au.titulo, LEFT(Q.enunciado, 100), D.titulo) AS titulo,
           COALESCE(M1.titulo, M2.titulo, M3.titulo) AS materia
    FROM Aprovacao A
    INNER JOIN Professor P ON P.id_professor = A.id_professor
    INNER JOIN Usuario U   ON U.id_usuario = P.id_usuario
    LEFT JOIN Aula Au    ON Au.id_aula = A.id_aula       LEFT JOIN Materia M1 ON M1.id_materia = Au.id_materia
    LEFT JOIN Questao Q  ON Q.id_questao = A.id_questao  LEFT JOIN Materia M2 ON M2.id_materia = Q.id_materia
    LEFT JOIN Desafio D  ON D.id_desafio = A.id_desafio  LEFT JOIN Materia M3 ON M3.id_materia = D.id_materia
    WHERE A.status = N'pendente'
    ORDER BY A.data_submissao;
END
GO

-- 3.14 Tickets. Se @id_usuario_destinatario for NULL, vai para o professor dono da matéria.
CREATE OR ALTER PROCEDURE sp_AbrirTicket
    @id_usuario_solicitante INT, @assunto NVARCHAR(150), @descricao NVARCHAR(MAX),
    @id_materia INT = NULL, @id_usuario_destinatario INT = NULL
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;

    IF @id_usuario_destinatario IS NULL AND @id_materia IS NOT NULL
        SELECT @id_usuario_destinatario = P.id_usuario
        FROM Materia M INNER JOIN Professor P ON P.id_professor = M.id_professor
        WHERE M.id_materia = @id_materia;

    IF @id_usuario_destinatario IS NULL
    BEGIN RAISERROR(N'Não foi possível definir o professor destinatário.', 16, 1); RETURN; END

    BEGIN TRAN;
        INSERT INTO TicketSuporte (id_usuario_solicitante, id_usuario_destinatario, id_materia, assunto, descricao, status, data_abertura)
        VALUES (@id_usuario_solicitante, @id_usuario_destinatario, @id_materia, @assunto, @descricao, N'aberto', GETDATE());
        DECLARE @id_ticket INT = SCOPE_IDENTITY();
        INSERT INTO TicketMensagem (id_ticket, id_usuario_remetente, texto, data_hora)
        VALUES (@id_ticket, @id_usuario_solicitante, @descricao, GETDATE());
    COMMIT;

    SELECT @id_ticket AS id_ticket;
END
GO

CREATE OR ALTER PROCEDURE sp_ResponderTicket
    @id_ticket INT, @id_usuario_remetente INT, @texto NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM TicketSuporte
                   WHERE id_ticket = @id_ticket
                     AND @id_usuario_remetente IN (id_usuario_solicitante, id_usuario_destinatario))
       AND NOT EXISTS (SELECT 1 FROM Admin WHERE id_usuario = @id_usuario_remetente)
    BEGIN RAISERROR(N'Você não participa deste ticket.', 16, 1); RETURN; END

    INSERT INTO TicketMensagem (id_ticket, id_usuario_remetente, texto, data_hora)
    VALUES (@id_ticket, @id_usuario_remetente, @texto, GETDATE());
END
GO

-- "Encerrar Ticket" do professor
CREATE OR ALTER PROCEDURE sp_FecharTicket
    @id_ticket INT, @id_usuario INT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE TicketSuporte SET status = N'fechado'
    WHERE id_ticket = @id_ticket
      AND (id_usuario_destinatario = @id_usuario OR EXISTS (SELECT 1 FROM Admin WHERE id_usuario = @id_usuario));
    IF @@ROWCOUNT = 0 RAISERROR(N'Ticket não encontrado ou sem permissão.', 16, 1);
END
GO

CREATE OR ALTER PROCEDURE sp_ListarTicketsUsuario
    @id_usuario INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT T.id_ticket, T.assunto, T.status, T.data_abertura, M.titulo AS materia,
           US.nome AS solicitante, UD.nome AS destinatario
    FROM TicketSuporte T
    INNER JOIN Usuario US ON US.id_usuario = T.id_usuario_solicitante
    INNER JOIN Usuario UD ON UD.id_usuario = T.id_usuario_destinatario
    LEFT JOIN Materia M   ON M.id_materia = T.id_materia
    WHERE @id_usuario IN (T.id_usuario_solicitante, T.id_usuario_destinatario)
    ORDER BY T.data_abertura DESC;
END
GO

CREATE OR ALTER PROCEDURE sp_MensagensTicket
    @id_ticket INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Msg.id_mensagem, Msg.id_usuario_remetente, U.nome, Msg.texto, Msg.data_hora,
           CASE WHEN Msg.id_usuario_remetente = T.id_usuario_solicitante THEN N'aluno' ELSE N'professor' END AS remetente
    FROM TicketMensagem Msg
    INNER JOIN TicketSuporte T ON T.id_ticket = Msg.id_ticket
    INNER JOIN Usuario U ON U.id_usuario = Msg.id_usuario_remetente
    WHERE Msg.id_ticket = @id_ticket
    ORDER BY Msg.data_hora, Msg.id_mensagem;
END
GO

-- 3.15 Ranking (empates dividem a posição) e posição individual
CREATE OR ALTER PROCEDURE sp_RankingAlunos
    @top INT = 10
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP (@top) *
    FROM (
        SELECT RANK() OVER (ORDER BY A.pontos_acumulados DESC) AS posicao,
               U.nome, U.nickname, U.foto_url, A.pontos_acumulados, A.streak_atual,
               (SELECT COUNT(*) FROM Aluno_Aula x WHERE x.id_aluno = A.id_aluno AND x.progresso_concluido = 1)
             + (SELECT COUNT(*) FROM Aluno_Questao y WHERE y.id_aluno = A.id_aluno AND y.concluida = 1) AS fases_concluidas
        FROM Aluno A INNER JOIN Usuario U ON U.id_usuario = A.id_usuario
        WHERE U.status = N'ativo'
    ) R
    ORDER BY posicao, nome;
END
GO

CREATE OR ALTER PROCEDURE sp_PosicaoRankingAluno
    @id_aluno INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT 1 + COUNT(*) AS posicao
    FROM Aluno A INNER JOIN Usuario U ON U.id_usuario = A.id_usuario
    WHERE U.status = N'ativo'
      AND A.pontos_acumulados > (SELECT pontos_acumulados FROM Aluno WHERE id_aluno = @id_aluno);
END
GO

-- 3.16 Perfil do aluno (cabeçalho/dropdown/tela de perfil). Streak "efetivo": zera se faltou ontem.
CREATE OR ALTER PROCEDURE sp_ObterPerfilAluno
    @id_aluno INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT U.nome, U.nickname, U.email, U.foto_url, U.data_cadastro,
           A.pontos_acumulados, A.chaves_saldo,
           CASE WHEN A.ultima_atividade >= DATEADD(DAY,-1,CAST(GETDATE() AS DATE)) THEN A.streak_atual ELSE 0 END AS streak_atual,
           (SELECT COUNT(*) FROM Aluno_Aula x WHERE x.id_aluno = A.id_aluno AND x.progresso_concluido = 1)
         + (SELECT COUNT(*) FROM Aluno_Questao y WHERE y.id_aluno = A.id_aluno AND y.concluida = 1) AS fases_concluidas,
           (SELECT COUNT(*) FROM Aluno_Questao y WHERE y.id_aluno = A.id_aluno AND y.concluida = 1) AS questoes_concluidas,
           (SELECT COUNT(*) FROM Certificado c WHERE c.id_aluno = A.id_aluno) AS certificados,
           1 + (SELECT COUNT(*) FROM Aluno A2 INNER JOIN Usuario U2 ON U2.id_usuario = A2.id_usuario
                WHERE U2.status = N'ativo' AND A2.pontos_acumulados > A.pontos_acumulados) AS posicao_ranking
    FROM Aluno A INNER JOIN Usuario U ON U.id_usuario = A.id_usuario
    WHERE A.id_aluno = @id_aluno;
END
GO

-- 3.17 Edição de perfil (aluno ou professor). Parâmetros NULL = não alterar.
CREATE OR ALTER PROCEDURE sp_AtualizarPerfil
    @id_usuario INT, @nome NVARCHAR(100) = NULL, @nickname NVARCHAR(50) = NULL,
    @email NVARCHAR(100) = NULL, @senha_hash NVARCHAR(255) = NULL,
    @foto_url NVARCHAR(MAX) = NULL, @especialidade NVARCHAR(100) = NULL
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    IF @email IS NOT NULL SET @email = LOWER(LTRIM(RTRIM(@email)));
    IF @nickname IS NOT NULL AND LEFT(@nickname,1) <> N'@' SET @nickname = N'@' + @nickname;

    IF @email IS NOT NULL AND EXISTS (SELECT 1 FROM Usuario WHERE email = @email AND id_usuario <> @id_usuario)
    BEGIN RAISERROR(N'Este e-mail já está em uso.', 16, 1); RETURN; END
    IF @nickname IS NOT NULL AND EXISTS (SELECT 1 FROM Usuario WHERE nickname = @nickname AND id_usuario <> @id_usuario)
    BEGIN RAISERROR(N'Este nome de usuário já está em uso.', 16, 1); RETURN; END

    UPDATE Usuario
    SET nome = ISNULL(@nome, nome), nickname = ISNULL(@nickname, nickname), email = ISNULL(@email, email),
        senha_hash = ISNULL(@senha_hash, senha_hash), foto_url = ISNULL(@foto_url, foto_url)
    WHERE id_usuario = @id_usuario;

    IF @especialidade IS NOT NULL
        UPDATE Professor SET especialidade = @especialidade WHERE id_usuario = @id_usuario;
END
GO

-- 3.18 Progresso do aluno na trilha (conta só conteúdo aprovado e não-extra)
CREATE OR ALTER PROCEDURE sp_ProgressoTrilhaAluno
    @id_aluno INT, @id_trilha INT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @ta INT, @ac INT, @tq INT, @qc INT, @pct DECIMAL(5,2);

    SELECT @ta = COUNT(*) FROM Aula Au INNER JOIN Materia M ON M.id_materia = Au.id_materia
    WHERE M.id_trilha = @id_trilha AND Au.status_aprovacao = N'aprovado' AND Au.is_extra = 0;

    SELECT @ac = COUNT(*) FROM Aluno_Aula AA INNER JOIN Aula Au ON Au.id_aula = AA.id_aula
    INNER JOIN Materia M ON M.id_materia = Au.id_materia
    WHERE M.id_trilha = @id_trilha AND AA.id_aluno = @id_aluno AND AA.progresso_concluido = 1
      AND Au.status_aprovacao = N'aprovado' AND Au.is_extra = 0;

    SELECT @tq = COUNT(*) FROM Questao Q INNER JOIN Materia M ON M.id_materia = Q.id_materia
    WHERE M.id_trilha = @id_trilha AND Q.status_aprovacao = N'aprovado';

    SELECT @qc = COUNT(*) FROM Aluno_Questao AQ INNER JOIN Questao Q ON Q.id_questao = AQ.id_questao
    INNER JOIN Materia M ON M.id_materia = Q.id_materia
    WHERE M.id_trilha = @id_trilha AND AQ.id_aluno = @id_aluno AND AQ.concluida = 1 AND Q.status_aprovacao = N'aprovado';

    SET @pct = CASE WHEN (@ta + @tq) = 0 THEN 0
                    ELSE CAST((@ac + @qc) AS DECIMAL(9,2)) / (@ta + @tq) * 100 END;

    IF EXISTS (SELECT 1 FROM Aluno_Trilha WHERE id_aluno = @id_aluno AND id_trilha = @id_trilha)
        UPDATE Aluno_Trilha SET progresso = @pct WHERE id_aluno = @id_aluno AND id_trilha = @id_trilha;
    ELSE
        INSERT INTO Aluno_Trilha (id_aluno, id_trilha, progresso) VALUES (@id_aluno, @id_trilha, @pct);

    SELECT @ta AS total_aulas, @ac AS aulas_concluidas, @tq AS total_questoes, @qc AS questoes_concluidas,
           @pct AS percentual_progresso;
END
GO

-- 3.19 Admin: ativar/inativar usuário (dispara TR_Usuario_LogStatus)
CREATE OR ALTER PROCEDURE sp_AlterarStatusUsuario
    @id_usuario INT, @novo_status NVARCHAR(20)
AS
BEGIN
    SET NOCOUNT ON;
    IF @novo_status NOT IN (N'ativo', N'inativo')
    BEGIN RAISERROR(N'Status inválido.', 16, 1); RETURN; END
    IF EXISTS (SELECT 1 FROM Admin WHERE id_usuario = @id_usuario)
    BEGIN RAISERROR(N'Não é possível alterar o status de um administrador.', 16, 1); RETURN; END
    UPDATE Usuario SET status = @novo_status WHERE id_usuario = @id_usuario;
END
GO

CREATE OR ALTER PROCEDURE sp_ListarUsuarios
    @papel NVARCHAR(20) = NULL      -- 'aluno' | 'professor' | 'admin' | NULL (todos)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT id_usuario, nome, nickname, email, status, papel
    FROM vw_UsuarioPapel
    WHERE @papel IS NULL OR papel = @papel
    ORDER BY nome;
END
GO

-- 3.20 Admin: configuração de gamificação (loga quem alterou)
CREATE OR ALTER PROCEDURE sp_AtualizarConfiguracaoPlataforma
    @chaves_questao_facil INT, @chaves_questao_media INT, @chaves_questao_dificil INT,
    @chaves_certificado INT, @id_usuario_admin INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    IF @chaves_questao_facil < 0 OR @chaves_questao_media < 0 OR @chaves_questao_dificil < 0 OR @chaves_certificado < 0
    BEGIN RAISERROR(N'Valores não podem ser negativos.', 16, 1); RETURN; END

    IF EXISTS (SELECT 1 FROM ChavesConfig)
        UPDATE ChavesConfig SET chaves_questao_facil = @chaves_questao_facil, chaves_questao_media = @chaves_questao_media,
                                chaves_questao_dificil = @chaves_questao_dificil, chaves_certificado = @chaves_certificado;
    ELSE
        INSERT INTO ChavesConfig VALUES (@chaves_questao_facil, @chaves_questao_media, @chaves_questao_dificil, @chaves_certificado);

    INSERT INTO LogAtividade (id_usuario, descricao, data_hora)
    VALUES (@id_usuario_admin, N'CONFIGURAÇÃO ATUALIZADA: Pontuação de gamificação', GETDATE());
END
GO

-- 3.21 Dashboard do admin + snapshot diário em SistemaMetricas
CREATE OR ALTER PROCEDURE sp_DashboardAdmin
AS
BEGIN
    SET NOCOUNT ON;
    SELECT (SELECT COUNT(*) FROM Aluno)     AS total_alunos,
           (SELECT COUNT(*) FROM Professor) AS total_professores,
           (SELECT COUNT(*) FROM Aprovacao WHERE status = N'pendente') AS aprovacoes_pendentes,
           (SELECT COUNT(*) FROM TicketSuporte WHERE status <> N'fechado') AS tickets_abertos,
           (SELECT COUNT(*) FROM Certificado) AS certificados_emitidos;

    SELECT TOP 20 L.id_log, U.nome, L.descricao, L.data_hora
    FROM LogAtividade L LEFT JOIN Usuario U ON U.id_usuario = L.id_usuario
    ORDER BY L.data_hora DESC, L.id_log DESC;
END
GO

CREATE OR ALTER PROCEDURE sp_RegistrarMetricasDiarias
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @alunos INT = (SELECT COUNT(*) FROM Aluno);
    DECLARE @ativos7 INT = (SELECT COUNT(*) FROM Aluno WHERE ultima_atividade >= DATEADD(DAY,-7,CAST(GETDATE() AS DATE)));
    DECLARE @comCert INT = (SELECT COUNT(DISTINCT id_aluno) FROM Certificado);

    INSERT INTO SistemaMetricas (data, total_usuarios, media_engajamento, taxa_conversao)
    VALUES (CAST(GETDATE() AS DATE), (SELECT COUNT(*) FROM Usuario),
            CASE WHEN @alunos = 0 THEN 0 ELSE CAST(@ativos7 AS DECIMAL(9,2)) / @alunos * 100 END,
            CASE WHEN @alunos = 0 THEN 0 ELSE CAST(@comCert AS DECIMAL(9,2)) / @alunos * 100 END);
END
GO
