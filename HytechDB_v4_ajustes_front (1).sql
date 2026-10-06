-- ============================================================
-- HytechDB — SCRIPT v4 (ajustes derivados da leitura do front
-- NN-hytech_v17_2.html). Rodar DEPOIS do v3. Idempotente.
--
-- Lacunas encontradas no front x banco e tratadas aqui:
--   1. Rascunho: front tem status 'rascunho' (aula/questão/desafio)
--   2. Autor da aula: vários professores enviam conteúdo para a
--      mesma matéria, mas a trigger usava Materia.id_professor
--   3. Matérias são da plataforma (Programação, Design, BD, Redes),
--      não de um professor -> Materia.id_professor passa a aceitar NULL
--   4. Desbloqueio de matéria com chaves (gasta chaves) -> Aluno_Materia
--   5. foto_url era VARCHAR(255), mas o front envia Data URL (base64)
--   6. Desafio também passa por aprovação
--   7. Tickets: front não escolhe professor -> roteamento por matéria;
--      ticket 'fechado' não pode ser reaberto/respondido
--   8. Admin: criar/editar/excluir usuário, métricas, logs
--   9. Perfil: editar nome/nickname/e-mail/senha/foto/especialidade
--  10. Painel do aluno: chaves, saldo, posição no ranking, progresso
--  11. Painel do professor: desempenho da turma (taxa de acerto)
-- ============================================================

USE HytechDB
GO

-- ============================================================
-- PARTE 1 — ESTRUTURA
-- ============================================================

-- 1.1 Foto de perfil como Data URL cabe em VARCHAR(MAX), não em 255
ALTER TABLE Usuario ALTER COLUMN foto_url VARCHAR(MAX) NULL;
GO

-- 1.2 Matéria pertence à plataforma; ordem dentro da trilha
ALTER TABLE Materia ALTER COLUMN id_professor INT NULL;
GO
IF COL_LENGTH('Materia', 'ordem') IS NULL
    ALTER TABLE Materia ADD ordem INT NULL;
GO

-- 1.2b Emojis (💻 🎨 🗄️ 🌐) não cabem em VARCHAR: vira '?'. Usar NVARCHAR.
ALTER TABLE Materia ALTER COLUMN icone NVARCHAR(20) NULL;
GO

-- 1.3 Aula: autor próprio + tópico livre (campo "topico" do formulário)
IF COL_LENGTH('Aula', 'id_professor') IS NULL
BEGIN
    ALTER TABLE Aula ADD id_professor INT NULL
        CONSTRAINT FK_Aula_Professor FOREIGN KEY REFERENCES Professor(id_professor);
    EXEC('UPDATE Au SET Au.id_professor = M.id_professor
          FROM Aula Au INNER JOIN Materia M ON M.id_materia = Au.id_materia');
END
GO
IF COL_LENGTH('Aula', 'topico') IS NULL
    ALTER TABLE Aula ADD topico VARCHAR(150) NULL;
GO

-- 1.4 Status 'rascunho' passa a ser válido em Aula
IF OBJECT_ID('CK_Aula_StatusAprovacao') IS NOT NULL
    ALTER TABLE Aula DROP CONSTRAINT CK_Aula_StatusAprovacao;
GO
ALTER TABLE Aula ADD CONSTRAINT CK_Aula_StatusAprovacao
    CHECK (status_aprovacao IN ('rascunho', 'pendente', 'aprovado', 'rejeitado'));
GO

-- 1.5 Aprovação também de desafios
IF COL_LENGTH('Aprovacao', 'id_desafio') IS NULL
    ALTER TABLE Aprovacao ADD id_desafio INT NULL
        CONSTRAINT FK_Aprovacao_Desafio FOREIGN KEY REFERENCES Desafio(id_desafio);
GO

-- 1.6 Tentativas por questão (taxa de acerto do painel do professor)
IF COL_LENGTH('Aluno_Questao', 'tentativas') IS NULL
    ALTER TABLE Aluno_Questao ADD tentativas INT NOT NULL
        CONSTRAINT DF_AlunoQuestao_Tentativas DEFAULT 0;
GO

-- 1.7 Só uma alternativa correta por questão
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_Alternativa_UmaCorreta' AND object_id = OBJECT_ID('Alternativa'))
    CREATE UNIQUE INDEX UX_Alternativa_UmaCorreta ON Alternativa(id_questao) WHERE correta = 1;
GO

-- 1.8 Matérias desbloqueadas pelo aluno (gasta chaves)
IF OBJECT_ID('Aluno_Materia') IS NULL
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

-- 1.9 Dados-base: 1 trilha e as 4 matérias do front
--     (custos de desbloqueio iguais aos de MATERIAS_DATA)
IF NOT EXISTS (SELECT 1 FROM TrilhaAprendizagem)
    INSERT INTO TrilhaAprendizagem (nome_trilha, descricao)
    VALUES ('Trilha HYTECH', 'Trilha principal de aprendizagem da plataforma');
GO
DECLARE @trilha INT = (SELECT TOP 1 id_trilha FROM TrilhaAprendizagem ORDER BY id_trilha);
IF NOT EXISTS (SELECT 1 FROM Materia WHERE titulo = 'Programação')
    INSERT INTO Materia (id_professor, id_trilha, titulo, tipo, status_aprovacao, icone, chaves_para_desbloquear, ordem)
    VALUES (NULL, @trilha, 'Programação',    'trilha', 'aprovado', N'💻',  0, 1);
IF NOT EXISTS (SELECT 1 FROM Materia WHERE titulo = 'Design')
    INSERT INTO Materia (id_professor, id_trilha, titulo, tipo, status_aprovacao, icone, chaves_para_desbloquear, ordem)
    VALUES (NULL, @trilha, 'Design',         'trilha', 'aprovado', N'🎨', 19, 2);
IF NOT EXISTS (SELECT 1 FROM Materia WHERE titulo = 'Banco de Dados')
    INSERT INTO Materia (id_professor, id_trilha, titulo, tipo, status_aprovacao, icone, chaves_para_desbloquear, ordem)
    VALUES (NULL, @trilha, 'Banco de Dados', 'trilha', 'aprovado', N'🗄️', 38, 3);
IF NOT EXISTS (SELECT 1 FROM Materia WHERE titulo = 'Redes')
    INSERT INTO Materia (id_professor, id_trilha, titulo, tipo, status_aprovacao, icone, chaves_para_desbloquear, ordem)
    VALUES (NULL, @trilha, 'Redes',          'trilha', 'aprovado', N'🌐', 57, 4);
GO

-- ============================================================
-- PARTE 2 — FUNÇÃO AUXILIAR
-- ============================================================

-- Saldo gastável = total ganho (pontos_acumulados) - chaves gastas em desbloqueios.
-- pontos_acumulados NUNCA diminui: é ele que define ranking e nível.
CREATE OR ALTER FUNCTION fn_SaldoChaves (@id_aluno INT)
RETURNS INT
AS
BEGIN
    RETURN ISNULL((SELECT pontos_acumulados FROM Aluno WHERE id_aluno = @id_aluno), 0)
         - ISNULL((SELECT SUM(chaves_gastas) FROM Aluno_Materia WHERE id_aluno = @id_aluno), 0);
END
GO

-- ============================================================
-- PARTE 3 — TRIGGERS
-- ============================================================

-- 3.1 Aula nova pendente -> fila de aprovação, usando o AUTOR da aula
CREATE OR ALTER TRIGGER TR_Aula_GeraAprovacao
ON Aula
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO Aprovacao (tipo, id_aula, id_professor, status, data_submissao)
    SELECT 'aula', I.id_aula, ISNULL(I.id_professor, M.id_professor), 'pendente', GETDATE()
    FROM inserted I
    INNER JOIN Materia M ON M.id_materia = I.id_materia
    WHERE I.status_aprovacao = 'pendente'
      AND ISNULL(I.id_professor, M.id_professor) IS NOT NULL;

    INSERT INTO LogAtividade (id_usuario, descricao, data_hora)
    SELECT U.id_usuario, 'CONTEÚDO ENVIADO (aula): ' + LEFT(ISNULL(I.titulo, ''), 60), GETDATE()
    FROM inserted I
    INNER JOIN Materia M   ON M.id_materia = I.id_materia
    INNER JOIN Professor P ON P.id_professor = ISNULL(I.id_professor, M.id_professor)
    INNER JOIN Usuario U   ON U.id_usuario = P.id_usuario
    WHERE I.status_aprovacao = 'pendente';
END
GO

-- 3.2 Desafio novo pendente -> fila de aprovação
CREATE OR ALTER TRIGGER TR_Desafio_GeraAprovacao
ON Desafio
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO Aprovacao (tipo, id_desafio, id_professor, status, data_submissao)
    SELECT 'desafio', I.id_desafio, I.id_professor, 'pendente', GETDATE()
    FROM inserted I
    WHERE I.status = 'pendente';

    INSERT INTO LogAtividade (id_usuario, descricao, data_hora)
    SELECT U.id_usuario, 'CONTEÚDO ENVIADO (desafio): ' + LEFT(ISNULL(I.titulo, ''), 60), GETDATE()
    FROM inserted I
    INNER JOIN Professor P ON P.id_professor = I.id_professor
    INNER JOIN Usuario U   ON U.id_usuario = P.id_usuario
    WHERE I.status = 'pendente';
END
GO

-- 3.3 Decisão do admin reflete em Questao, Aula e Desafio, e loga
CREATE OR ALTER TRIGGER TR_Aprovacao_SincronizaStatus
ON Aprovacao
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @mudou TABLE (id_aprovacao INT, tipo VARCHAR(20), status VARCHAR(20),
                          id_aula INT, id_questao INT, id_desafio INT, id_professor INT);

    INSERT INTO @mudou
    SELECT I.id_aprovacao, I.tipo, I.status, I.id_aula, I.id_questao, I.id_desafio, I.id_professor
    FROM inserted I
    INNER JOIN deleted D ON D.id_aprovacao = I.id_aprovacao
    WHERE I.status <> D.status;

    UPDATE Q SET Q.status_aprovacao = M.status
    FROM Questao Q INNER JOIN @mudou M ON M.id_questao = Q.id_questao WHERE M.tipo = 'questao';

    UPDATE Au SET Au.status_aprovacao = M.status
    FROM Aula Au INNER JOIN @mudou M ON M.id_aula = Au.id_aula WHERE M.tipo = 'aula';

    UPDATE Ds SET Ds.status = M.status
    FROM Desafio Ds INNER JOIN @mudou M ON M.id_desafio = Ds.id_desafio WHERE M.tipo = 'desafio';

    INSERT INTO LogAtividade (id_usuario, descricao, data_hora)
    SELECT U.id_usuario,
           UPPER(M.tipo) + CASE WHEN M.status = 'aprovado' THEN ' APROVADO(A): #' ELSE ' REJEITADO(A): #' END
               + CAST(COALESCE(M.id_questao, M.id_aula, M.id_desafio) AS VARCHAR),
           GETDATE()
    FROM @mudou M
    INNER JOIN Professor P ON P.id_professor = M.id_professor
    INNER JOIN Usuario U   ON U.id_usuario = P.id_usuario
    WHERE M.status IN ('aprovado', 'rejeitado');
END
GO

-- 3.4 Mensagem em ticket: não mexe em ticket 'fechado'
CREATE OR ALTER TRIGGER TR_TicketMensagem_AtualizaStatus
ON TicketMensagem
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE T
    SET T.status = CASE WHEN I.id_usuario_remetente = T.id_usuario_solicitante
                         THEN 'aberto' ELSE 'respondido' END
    FROM TicketSuporte T
    INNER JOIN inserted I ON I.id_ticket = T.id_ticket
    WHERE ISNULL(T.status, '') <> 'fechado';
END
GO

-- ============================================================
-- PARTE 4 — PROCEDURES: ALUNO
-- ============================================================

-- 4.1 Responder questão — agora conta tentativas e devolve as chaves
--     realmente ganhas (o front não precisa mais fixar "+5")
CREATE OR ALTER PROCEDURE sp_ResponderQuestao
    @id_aluno       INT,
    @id_questao     INT,
    @id_alternativa INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @acertou BIT, @ja_concluida BIT = 0, @id_materia INT;

    SELECT @id_materia = id_materia FROM Questao
    WHERE id_questao = @id_questao AND status_aprovacao = 'aprovado';

    IF @id_materia IS NULL
    BEGIN
        RAISERROR('Questão inexistente ou ainda não aprovada.', 16, 1);
        RETURN;
    END

    SELECT @acertou = correta FROM Alternativa
    WHERE id_alternativa = @id_alternativa AND id_questao = @id_questao;

    IF @acertou IS NULL
    BEGIN
        RAISERROR('Alternativa não pertence a essa questão.', 16, 1);
        RETURN;
    END

    DECLARE @antes INT = (SELECT pontos_acumulados FROM Aluno WHERE id_aluno = @id_aluno);
    DECLARE @cert_antes INT = (SELECT COUNT(*) FROM Certificado WHERE id_aluno = @id_aluno AND id_materia = @id_materia);

    BEGIN TRAN;

    IF EXISTS (SELECT 1 FROM Aluno_Questao WITH (UPDLOCK, HOLDLOCK)
               WHERE id_aluno = @id_aluno AND id_questao = @id_questao AND concluida = 1)
        SET @ja_concluida = 1;
    ELSE IF EXISTS (SELECT 1 FROM Aluno_Questao WITH (UPDLOCK, HOLDLOCK)
                    WHERE id_aluno = @id_aluno AND id_questao = @id_questao)
        UPDATE Aluno_Questao
        SET concluida = @acertou, tentativas = tentativas + 1,
            data_conclusao = CASE WHEN @acertou = 1 THEN GETDATE() ELSE NULL END
        WHERE id_aluno = @id_aluno AND id_questao = @id_questao;
    ELSE
        INSERT INTO Aluno_Questao (id_aluno, id_questao, concluida, tentativas, data_conclusao)
        VALUES (@id_aluno, @id_questao, @acertou, 1, CASE WHEN @acertou = 1 THEN GETDATE() ELSE NULL END);

    COMMIT;

    DECLARE @depois INT = (SELECT pontos_acumulados FROM Aluno WHERE id_aluno = @id_aluno);
    DECLARE @cert_depois INT = (SELECT COUNT(*) FROM Certificado WHERE id_aluno = @id_aluno AND id_materia = @id_materia);

    SELECT @acertou AS acertou, @ja_concluida AS ja_concluida,
           @depois - @antes AS chaves_ganhas,
           CAST(CASE WHEN @cert_depois > @cert_antes THEN 1 ELSE 0 END AS BIT) AS certificado_emitido,
           dbo.fn_SaldoChaves(@id_aluno) AS saldo_chaves;
END
GO

-- 4.2 Concluir aula/tópico — também devolve chaves ganhas e certificado
CREATE OR ALTER PROCEDURE sp_ConcluirAula
    @id_aluno INT,
    @id_aula  INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @id_materia INT;
    SELECT @id_materia = id_materia FROM Aula
    WHERE id_aula = @id_aula AND status_aprovacao = 'aprovado';

    IF @id_materia IS NULL
    BEGIN
        RAISERROR('Aula inexistente ou ainda não aprovada.', 16, 1);
        RETURN;
    END

    DECLARE @antes INT = (SELECT pontos_acumulados FROM Aluno WHERE id_aluno = @id_aluno);
    DECLARE @cert_antes INT = (SELECT COUNT(*) FROM Certificado WHERE id_aluno = @id_aluno AND id_materia = @id_materia);

    BEGIN TRAN;

    IF EXISTS (SELECT 1 FROM Aluno_Aula WITH (UPDLOCK, HOLDLOCK)
               WHERE id_aluno = @id_aluno AND id_aula = @id_aula)
        UPDATE Aluno_Aula SET progresso_concluido = 1
        WHERE id_aluno = @id_aluno AND id_aula = @id_aula AND progresso_concluido = 0;
    ELSE
        INSERT INTO Aluno_Aula (id_aluno, id_aula, progresso_concluido)
        VALUES (@id_aluno, @id_aula, 1);

    COMMIT;

    DECLARE @depois INT = (SELECT pontos_acumulados FROM Aluno WHERE id_aluno = @id_aluno);
    DECLARE @cert_depois INT = (SELECT COUNT(*) FROM Certificado WHERE id_aluno = @id_aluno AND id_materia = @id_materia);

    SELECT @depois - @antes AS chaves_ganhas,
           CAST(CASE WHEN @cert_depois > @cert_antes THEN 1 ELSE 0 END AS BIT) AS certificado_emitido,
           dbo.fn_SaldoChaves(@id_aluno) AS saldo_chaves;
END
GO

-- 4.3 Desbloquear matéria gastando chaves (equivale a desbloquearProximaMateria)
CREATE OR ALTER PROCEDURE sp_DesbloquearMateria
    @id_aluno   INT,
    @id_materia INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @custo INT, @ordem INT, @trilha INT;
    SELECT @custo = chaves_para_desbloquear, @ordem = ordem, @trilha = id_trilha
    FROM Materia WHERE id_materia = @id_materia;

    IF @custo IS NULL
    BEGIN RAISERROR('Matéria inexistente.', 16, 1); RETURN; END

    IF EXISTS (SELECT 1 FROM Aluno_Materia WHERE id_aluno = @id_aluno AND id_materia = @id_materia)
    BEGIN RAISERROR('Matéria já desbloqueada.', 16, 1); RETURN; END

    -- a matéria anterior da trilha precisa estar concluída (certificado emitido)
    DECLARE @anterior INT = (SELECT TOP 1 id_materia FROM Materia
                             WHERE id_trilha = @trilha AND ordem < @ordem ORDER BY ordem DESC);
    IF @anterior IS NOT NULL
       AND NOT EXISTS (SELECT 1 FROM Certificado WHERE id_aluno = @id_aluno AND id_materia = @anterior)
    BEGIN RAISERROR('Conclua a matéria anterior antes de desbloquear esta.', 16, 1); RETURN; END

    IF dbo.fn_SaldoChaves(@id_aluno) < @custo
    BEGIN RAISERROR('Chaves insuficientes para desbloquear esta matéria.', 16, 1); RETURN; END

    INSERT INTO Aluno_Materia (id_aluno, id_materia, chaves_gastas)
    VALUES (@id_aluno, @id_materia, @custo);

    INSERT INTO LogAtividade (id_usuario, descricao, data_hora)
    SELECT U.id_usuario, 'MATÉRIA DESBLOQUEADA: #' + CAST(@id_materia AS VARCHAR) + ' (-' + CAST(@custo AS VARCHAR) + ' chaves)', GETDATE()
    FROM Aluno A INNER JOIN Usuario U ON U.id_usuario = A.id_usuario WHERE A.id_aluno = @id_aluno;

    SELECT dbo.fn_SaldoChaves(@id_aluno) AS saldo_chaves;
END
GO

-- 4.4 Resumo do aluno: chaves, saldo, streak, posição no ranking, fases
CREATE OR ALTER PROCEDURE sp_ResumoAluno
    @id_aluno INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @pontos INT = (SELECT pontos_acumulados FROM Aluno WHERE id_aluno = @id_aluno);

    SELECT A.pontos_acumulados AS chaves_total,
           dbo.fn_SaldoChaves(A.id_aluno) AS saldo_chaves,
           A.streak_atual,
           1 + (SELECT COUNT(*) FROM Aluno A2
                INNER JOIN Usuario U2 ON U2.id_usuario = A2.id_usuario
                WHERE U2.status = 'ativo' AND A2.pontos_acumulados > @pontos) AS posicao_ranking,
           (SELECT COUNT(*) FROM Aluno_Aula x WHERE x.id_aluno = A.id_aluno AND x.progresso_concluido = 1)
         + (SELECT COUNT(*) FROM Aluno_Questao y WHERE y.id_aluno = A.id_aluno AND y.concluida = 1) AS fases_concluidas,
           (SELECT COUNT(*) FROM Certificado c WHERE c.id_aluno = A.id_aluno) AS certificados
    FROM Aluno A
    WHERE A.id_aluno = @id_aluno;
END
GO

-- 4.5 Estado de progresso para reidratar o front após o login (4 result sets)
CREATE OR ALTER PROCEDURE sp_ProgressoAluno
    @id_aluno INT
AS
BEGIN
    SET NOCOUNT ON;

    -- 1) aulas/tópicos concluídos
    SELECT id_aula FROM Aluno_Aula WHERE id_aluno = @id_aluno AND progresso_concluido = 1;
    -- 2) questões concluídas
    SELECT id_questao FROM Aluno_Questao WHERE id_aluno = @id_aluno AND concluida = 1;
    -- 3) matérias liberadas (a primeira da trilha e as de custo 0 já nascem liberadas)
    SELECT M.id_materia, M.titulo, M.ordem,
           CAST(CASE WHEN AM.id_Alu_Mat IS NOT NULL OR M.chaves_para_desbloquear = 0 THEN 1 ELSE 0 END AS BIT) AS desbloqueada
    FROM Materia M
    LEFT JOIN Aluno_Materia AM ON AM.id_materia = M.id_materia AND AM.id_aluno = @id_aluno
    ORDER BY M.ordem;
    -- 4) certificados
    SELECT id_materia, data_emissao FROM Certificado WHERE id_aluno = @id_aluno;
END
GO

-- ============================================================
-- PARTE 5 — PROCEDURES: PROFESSOR (conteúdo, questões, desafios)
-- ============================================================

-- 5.1 Salvar aula (rascunho ou enviar p/ aprovação). id_aula NULL = nova.
CREATE OR ALTER PROCEDURE sp_SalvarAula
    @id_aula        INT = NULL,
    @id_professor   INT,
    @id_materia     INT,
    @titulo         VARCHAR(100),
    @topico         VARCHAR(150) = NULL,
    @conteudo       VARCHAR(MAX) = NULL,
    @exemplo_codigo VARCHAR(MAX) = NULL,
    @dica_corpo     VARCHAR(MAX) = NULL,
    @enviar         BIT = 0
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    DECLARE @status VARCHAR(20) = CASE WHEN @enviar = 1 THEN 'pendente' ELSE 'rascunho' END;

    IF @id_aula IS NULL
    BEGIN
        INSERT INTO Aula (id_materia, id_professor, titulo, topico, conteudo, exemplo_codigo, dica_corpo, status_aprovacao)
        VALUES (@id_materia, @id_professor, @titulo, @topico, @conteudo, @exemplo_codigo, @dica_corpo, @status);
        SET @id_aula = SCOPE_IDENTITY();   -- a trigger cria a Aprovacao se for 'pendente'
    END
    ELSE
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM Aula WHERE id_aula = @id_aula AND id_professor = @id_professor
                       AND status_aprovacao IN ('rascunho', 'rejeitado'))
        BEGIN RAISERROR('Só é possível editar rascunhos ou conteúdos rejeitados do próprio professor.', 16, 1); RETURN; END

        UPDATE Aula SET id_materia = @id_materia, titulo = @titulo, topico = @topico, conteudo = @conteudo,
                        exemplo_codigo = @exemplo_codigo, dica_corpo = @dica_corpo
        WHERE id_aula = @id_aula;

        IF @enviar = 1 EXEC sp_EnviarParaAprovacao 'aula', @id_aula, @id_professor;
    END

    SELECT @id_aula AS id_aula;
END
GO

-- 5.2 Salvar questão com alternativas (JSON: ["texto A","texto B",...]) e
--     índice (0-based) da correta — equivale a _coletarAlternativas/_coletarGabarito
CREATE OR ALTER PROCEDURE sp_SalvarQuestao
    @id_questao        INT = NULL,
    @id_professor      INT,
    @id_materia        INT,
    @enunciado         VARCHAR(MAX),
    @dificuldade       VARCHAR(20),
    @codigo_exemplo    VARCHAR(MAX) = NULL,
    @alternativas_json NVARCHAR(MAX),
    @indice_correta    INT = -1,
    @enviar            BIT = 0
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @alts TABLE (idx INT, texto NVARCHAR(255));
    INSERT INTO @alts (idx, texto)
    SELECT CAST([key] AS INT), [value] FROM OPENJSON(@alternativas_json) WHERE LEN(LTRIM(RTRIM([value]))) > 0;

    IF @enviar = 1
    BEGIN
        IF (SELECT COUNT(*) FROM @alts) < 2
        BEGIN RAISERROR('Preencha pelo menos 2 alternativas.', 16, 1); RETURN; END
        IF NOT EXISTS (SELECT 1 FROM @alts WHERE idx = @indice_correta)
        BEGIN RAISERROR('Marque a alternativa correta.', 16, 1); RETURN; END
    END

    DECLARE @status VARCHAR(20) = CASE WHEN @enviar = 1 THEN 'pendente' ELSE 'rascunho' END;

    BEGIN TRAN;

    IF @id_questao IS NULL
    BEGIN
        -- chaves_recompensa fica NULL de propósito: segue ChavesConfig pela dificuldade
        INSERT INTO Questao (id_materia, id_professor, enunciado, dificuldade, status_aprovacao, data_criacao, codigo_exemplo)
        VALUES (@id_materia, @id_professor, @enunciado, @dificuldade, @status, GETDATE(), @codigo_exemplo);
        SET @id_questao = SCOPE_IDENTITY();   -- a trigger cria a Aprovacao se for 'pendente'
    END
    ELSE
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM Questao WHERE id_questao = @id_questao AND id_professor = @id_professor
                       AND status_aprovacao IN ('rascunho', 'rejeitado'))
        BEGIN ROLLBACK; RAISERROR('Só é possível editar rascunhos ou questões rejeitadas do próprio professor.', 16, 1); RETURN; END

        UPDATE Questao SET id_materia = @id_materia, enunciado = @enunciado, dificuldade = @dificuldade,
                           codigo_exemplo = @codigo_exemplo
        WHERE id_questao = @id_questao;
        DELETE FROM Alternativa WHERE id_questao = @id_questao;
    END

    INSERT INTO Alternativa (id_questao, texto, correta)
    SELECT @id_questao, texto, CASE WHEN idx = @indice_correta THEN 1 ELSE 0 END FROM @alts;

    IF @enviar = 1 AND EXISTS (SELECT 1 FROM Questao WHERE id_questao = @id_questao AND status_aprovacao <> 'pendente')
        EXEC sp_EnviarParaAprovacao 'questao', @id_questao, @id_professor;

    COMMIT;
    SELECT @id_questao AS id_questao;
END
GO

-- 5.3 Salvar desafio (rascunho ou enviar p/ aprovação)
CREATE OR ALTER PROCEDURE sp_SalvarDesafio
    @id_desafio   INT = NULL,
    @id_professor INT,
    @id_materia   INT,
    @titulo       VARCHAR(150),
    @enunciado    VARCHAR(MAX),
    @dificuldade  VARCHAR(20),
    @codigo_base  VARCHAR(MAX) = NULL,
    @dica         VARCHAR(MAX) = NULL,
    @enviar       BIT = 0
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    DECLARE @status VARCHAR(20) = CASE WHEN @enviar = 1 THEN 'pendente' ELSE 'rascunho' END;

    IF @id_desafio IS NULL
    BEGIN
        INSERT INTO Desafio (id_professor, id_materia, titulo, enunciado, dica, dificuldade, status, codigo_base, data_criacao)
        VALUES (@id_professor, @id_materia, @titulo, @enunciado, @dica, @dificuldade, @status, @codigo_base, GETDATE());
        SET @id_desafio = SCOPE_IDENTITY();   -- a trigger cria a Aprovacao se for 'pendente'
    END
    ELSE
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM Desafio WHERE id_desafio = @id_desafio AND id_professor = @id_professor
                       AND status IN ('rascunho', 'rejeitado'))
        BEGIN RAISERROR('Só é possível editar rascunhos ou desafios rejeitados do próprio professor.', 16, 1); RETURN; END

        UPDATE Desafio SET id_materia = @id_materia, titulo = @titulo, enunciado = @enunciado,
                           dificuldade = @dificuldade, codigo_base = @codigo_base, dica = @dica
        WHERE id_desafio = @id_desafio;

        IF @enviar = 1 EXEC sp_EnviarParaAprovacao 'desafio', @id_desafio, @id_professor;
    END

    SELECT @id_desafio AS id_desafio;
END
GO

-- 5.4 Enviar um rascunho (ou item rejeitado) para a fila de aprovação
CREATE OR ALTER PROCEDURE sp_EnviarParaAprovacao
    @tipo         VARCHAR(20),   -- 'aula' | 'questao' | 'desafio'
    @id           INT,
    @id_professor INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRAN;

    IF @tipo = 'aula'
    BEGIN
        UPDATE Aula SET status_aprovacao = 'pendente'
        WHERE id_aula = @id AND id_professor = @id_professor AND status_aprovacao IN ('rascunho', 'rejeitado');
        IF @@ROWCOUNT = 0 BEGIN ROLLBACK; RAISERROR('Aula não encontrada ou não está em rascunho.', 16, 1); RETURN; END
        INSERT INTO Aprovacao (tipo, id_aula, id_professor, status, data_submissao) VALUES ('aula', @id, @id_professor, 'pendente', GETDATE());
    END
    ELSE IF @tipo = 'questao'
    BEGIN
        IF (SELECT COUNT(*) FROM Alternativa WHERE id_questao = @id) < 2
           OR NOT EXISTS (SELECT 1 FROM Alternativa WHERE id_questao = @id AND correta = 1)
        BEGIN ROLLBACK; RAISERROR('A questão precisa de 2+ alternativas e uma correta.', 16, 1); RETURN; END
        UPDATE Questao SET status_aprovacao = 'pendente'
        WHERE id_questao = @id AND id_professor = @id_professor AND status_aprovacao IN ('rascunho', 'rejeitado');
        IF @@ROWCOUNT = 0 BEGIN ROLLBACK; RAISERROR('Questão não encontrada ou não está em rascunho.', 16, 1); RETURN; END
        INSERT INTO Aprovacao (tipo, id_questao, id_professor, status, data_submissao) VALUES ('questao', @id, @id_professor, 'pendente', GETDATE());
    END
    ELSE IF @tipo = 'desafio'
    BEGIN
        UPDATE Desafio SET status = 'pendente'
        WHERE id_desafio = @id AND id_professor = @id_professor AND status IN ('rascunho', 'rejeitado');
        IF @@ROWCOUNT = 0 BEGIN ROLLBACK; RAISERROR('Desafio não encontrado ou não está em rascunho.', 16, 1); RETURN; END
        INSERT INTO Aprovacao (tipo, id_desafio, id_professor, status, data_submissao) VALUES ('desafio', @id, @id_professor, 'pendente', GETDATE());
    END
    ELSE BEGIN ROLLBACK; RAISERROR('Tipo inválido.', 16, 1); RETURN; END

    INSERT INTO LogAtividade (id_usuario, descricao, data_hora)
    SELECT U.id_usuario, 'CONTEÚDO ENVIADO (' + @tipo + '): #' + CAST(@id AS VARCHAR), GETDATE()
    FROM Professor P INNER JOIN Usuario U ON U.id_usuario = P.id_usuario WHERE P.id_professor = @id_professor;

    COMMIT;
END
GO

-- 5.5 Excluir conteúdo do professor (não permite excluir o que já foi aprovado)
CREATE OR ALTER PROCEDURE sp_ExcluirConteudoProfessor
    @tipo         VARCHAR(20),   -- 'aula' | 'questao' | 'desafio'
    @id           INT,
    @id_professor INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRAN;

    IF @tipo = 'aula'
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM Aula WHERE id_aula = @id AND id_professor = @id_professor AND status_aprovacao <> 'aprovado')
        BEGIN ROLLBACK; RAISERROR('Aula não encontrada ou já aprovada.', 16, 1); RETURN; END
        DELETE FROM Aprovacao WHERE id_aula = @id;
        DELETE FROM Aula WHERE id_aula = @id;
    END
    ELSE IF @tipo = 'questao'
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM Questao WHERE id_questao = @id AND id_professor = @id_professor AND status_aprovacao <> 'aprovado')
        BEGIN ROLLBACK; RAISERROR('Questão não encontrada ou já aprovada.', 16, 1); RETURN; END
        DELETE FROM Aprovacao WHERE id_questao = @id;
        DELETE FROM Alternativa WHERE id_questao = @id;
        DELETE FROM Questao WHERE id_questao = @id;
    END
    ELSE IF @tipo = 'desafio'
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM Desafio WHERE id_desafio = @id AND id_professor = @id_professor AND status <> 'aprovado')
        BEGIN ROLLBACK; RAISERROR('Desafio não encontrado ou já aprovado.', 16, 1); RETURN; END
        DELETE FROM Aprovacao WHERE id_desafio = @id;
        DELETE FROM Desafio WHERE id_desafio = @id;
    END
    ELSE BEGIN ROLLBACK; RAISERROR('Tipo inválido.', 16, 1); RETURN; END

    COMMIT;
END
GO

-- 5.6 Desempenho da turma (tela "Alunos" do professor). Filtro de matéria opcional.
CREATE OR ALTER PROCEDURE sp_DesempenhoAlunos
    @id_materia INT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    SELECT U.nome, U.email, A.pontos_acumulados AS chaves,
           (SELECT COUNT(*) FROM Aluno_Aula x INNER JOIN Aula Au ON Au.id_aula = x.id_aula
            WHERE x.id_aluno = A.id_aluno AND x.progresso_concluido = 1
              AND (@id_materia IS NULL OR Au.id_materia = @id_materia))
         + (SELECT COUNT(*) FROM Aluno_Questao y INNER JOIN Questao Q ON Q.id_questao = y.id_questao
            WHERE y.id_aluno = A.id_aluno AND y.concluida = 1
              AND (@id_materia IS NULL OR Q.id_materia = @id_materia)) AS fases,
           ISNULL((SELECT CAST(100.0 * SUM(CASE WHEN y.concluida = 1 THEN 1 ELSE 0 END) / NULLIF(SUM(y.tentativas), 0) AS INT)
                   FROM Aluno_Questao y INNER JOIN Questao Q ON Q.id_questao = y.id_questao
                   WHERE y.id_aluno = A.id_aluno AND (@id_materia IS NULL OR Q.id_materia = @id_materia)), 0) AS acerto,
           (SELECT COUNT(*) FROM Certificado c WHERE c.id_aluno = A.id_aluno
              AND (@id_materia IS NULL OR c.id_materia = @id_materia)) AS certs,
           A.ultima_atividade AS ultima
    FROM Aluno A
    INNER JOIN Usuario U ON U.id_usuario = A.id_usuario
    WHERE U.status = 'ativo'
    ORDER BY U.nome;
END
GO

-- ============================================================
-- PARTE 6 — PROCEDURES: TICKETS
-- ============================================================

-- 6.1 Abrir ticket pela matéria (o front não escolhe o professor):
--     destino = professor ativo cuja especialidade = título da matéria, com
--     menos tickets abertos; se não houver, o primeiro admin ativo.
CREATE OR ALTER PROCEDURE sp_AbrirTicketPorMateria
    @id_usuario_solicitante INT,
    @id_materia             INT,
    @assunto                VARCHAR(150),
    @descricao              VARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @titulo VARCHAR(100) = (SELECT titulo FROM Materia WHERE id_materia = @id_materia);
    IF @titulo IS NULL BEGIN RAISERROR('Matéria inexistente.', 16, 1); RETURN; END

    DECLARE @destino INT =
        (SELECT TOP 1 P.id_usuario
         FROM Professor P INNER JOIN Usuario U ON U.id_usuario = P.id_usuario
         WHERE P.especialidade = @titulo AND U.status = 'ativo'
         ORDER BY (SELECT COUNT(*) FROM TicketSuporte T
                   WHERE T.id_usuario_destinatario = P.id_usuario AND T.status <> 'fechado'), P.id_professor);

    IF @destino IS NULL
        SET @destino = (SELECT TOP 1 Ad.id_usuario FROM Admin Ad
                        INNER JOIN Usuario U ON U.id_usuario = Ad.id_usuario
                        WHERE U.status = 'ativo' ORDER BY Ad.id_admin);

    IF @destino IS NULL BEGIN RAISERROR('Nenhum professor ou admin disponível para receber o ticket.', 16, 1); RETURN; END

    EXEC sp_AbrirTicket @id_usuario_solicitante, @destino, @assunto, @descricao, @id_materia;
END
GO

-- 6.2 Responder: não aceita ticket fechado
CREATE OR ALTER PROCEDURE sp_ResponderTicket
    @id_ticket            INT,
    @id_usuario_remetente INT,
    @texto                VARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM TicketSuporte
                   WHERE id_ticket = @id_ticket
                     AND (id_usuario_solicitante = @id_usuario_remetente
                          OR id_usuario_destinatario = @id_usuario_remetente))
       AND NOT EXISTS (SELECT 1 FROM Admin WHERE id_usuario = @id_usuario_remetente)
    BEGIN RAISERROR('Usuário não participa deste ticket.', 16, 1); RETURN; END

    IF EXISTS (SELECT 1 FROM TicketSuporte WHERE id_ticket = @id_ticket AND status = 'fechado')
    BEGIN RAISERROR('Ticket encerrado: não aceita novas mensagens.', 16, 1); RETURN; END

    INSERT INTO TicketMensagem (id_ticket, id_usuario_remetente, texto, data_hora)
    VALUES (@id_ticket, @id_usuario_remetente, @texto, GETDATE());
END
GO

-- 6.3 Encerrar ticket (professor destinatário ou admin)
CREATE OR ALTER PROCEDURE sp_FecharTicket
    @id_ticket  INT,
    @id_usuario INT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE TicketSuporte SET status = 'fechado'
    WHERE id_ticket = @id_ticket
      AND (id_usuario_destinatario = @id_usuario OR EXISTS (SELECT 1 FROM Admin WHERE id_usuario = @id_usuario));

    IF @@ROWCOUNT = 0 RAISERROR('Ticket não encontrado ou sem permissão para encerrar.', 16, 1);
END
GO

-- 6.4 Lista de tickets do usuário (aluno vê os que abriu; professor/admin os recebidos)
CREATE OR ALTER PROCEDURE sp_ListarTickets
    @id_usuario INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT T.id_ticket, T.assunto, T.status, T.data_abertura,
           M.titulo AS materia, US.nome AS aluno, UD.nome AS professor
    FROM TicketSuporte T
    INNER JOIN Usuario US ON US.id_usuario = T.id_usuario_solicitante
    INNER JOIN Usuario UD ON UD.id_usuario = T.id_usuario_destinatario
    LEFT JOIN Materia M   ON M.id_materia = T.id_materia
    WHERE T.id_usuario_solicitante = @id_usuario OR T.id_usuario_destinatario = @id_usuario
    ORDER BY T.data_abertura DESC;
END
GO

-- 6.5 Conversa de um ticket (remetente 'aluno' = quem abriu; 'professor' = o resto)
CREATE OR ALTER PROCEDURE sp_MensagensTicket
    @id_ticket  INT,
    @id_usuario INT
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM TicketSuporte
                   WHERE id_ticket = @id_ticket
                     AND (id_usuario_solicitante = @id_usuario OR id_usuario_destinatario = @id_usuario))
       AND NOT EXISTS (SELECT 1 FROM Admin WHERE id_usuario = @id_usuario)
    BEGIN RAISERROR('Usuário não participa deste ticket.', 16, 1); RETURN; END

    SELECT TM.id_mensagem, U.nome,
           CASE WHEN TM.id_usuario_remetente = T.id_usuario_solicitante THEN 'aluno' ELSE 'professor' END AS remetente,
           TM.texto, TM.data_hora
    FROM TicketMensagem TM
    INNER JOIN TicketSuporte T ON T.id_ticket = TM.id_ticket
    INNER JOIN Usuario U       ON U.id_usuario = TM.id_usuario_remetente
    WHERE TM.id_ticket = @id_ticket
    ORDER BY TM.data_hora, TM.id_mensagem;
END
GO

-- ============================================================
-- PARTE 7 — PROCEDURES: PERFIL
-- ============================================================

-- 7.1 Atualizar dados do perfil (parâmetro NULL = mantém o valor atual)
CREATE OR ALTER PROCEDURE sp_AtualizarPerfil
    @id_usuario    INT,
    @nome          VARCHAR(100) = NULL,
    @nickname      VARCHAR(50)  = NULL,
    @email         VARCHAR(100) = NULL,
    @foto_url      VARCHAR(MAX) = NULL,
    @especialidade VARCHAR(100) = NULL     -- só vale para professor
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRAN;

    UPDATE Usuario
    SET nome     = ISNULL(@nome, nome),
        nickname = ISNULL(@nickname, nickname),
        email    = ISNULL(@email, email),
        foto_url = ISNULL(@foto_url, foto_url)
    WHERE id_usuario = @id_usuario;

    IF @especialidade IS NOT NULL
        UPDATE Professor SET especialidade = @especialidade WHERE id_usuario = @id_usuario;

    COMMIT;
END
GO

-- 7.2 Trocar senha (a API confere a senha atual e envia o novo hash)
CREATE OR ALTER PROCEDURE sp_AlterarSenha
    @id_usuario INT,
    @senha_hash VARCHAR(255)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE Usuario SET senha = @senha_hash WHERE id_usuario = @id_usuario;
END
GO

-- ============================================================
-- PARTE 8 — PROCEDURES: ADMIN
-- ============================================================

-- 8.1 Criar/editar usuário (tela "Gerenciar Usuários"). id_usuario NULL = novo.
--     Troca de perfil só é permitida se o usuário não tiver dados vinculados.
CREATE OR ALTER PROCEDURE sp_AdminSalvarUsuario
    @id_usuario    INT = NULL,
    @nome          VARCHAR(100),
    @email         VARCHAR(100),
    @perfil        VARCHAR(20),          -- 'aluno' | 'professor' | 'admin'
    @especialidade VARCHAR(100) = NULL,
    @nickname      VARCHAR(50)  = NULL,
    @senha_hash    VARCHAR(255) = NULL,  -- obrigatório ao criar
    @data_nascimento DATE = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @perfil NOT IN ('aluno', 'professor', 'admin')
    BEGIN RAISERROR('Perfil inválido.', 16, 1); RETURN; END
    IF @perfil = 'professor' AND ISNULL(@especialidade, '') = ''
    BEGIN RAISERROR('Selecione a especialidade do professor.', 16, 1); RETURN; END

    -- nickname é UNIQUE e o SQL Server só aceita um NULL: gera um a partir do e-mail
    IF @nickname IS NULL
        SET @nickname = '@' + LEFT(@email, CHARINDEX('@', @email + '@') - 1);

    BEGIN TRAN;

    IF @id_usuario IS NULL
    BEGIN
        IF @senha_hash IS NULL BEGIN ROLLBACK; RAISERROR('Defina uma senha para o novo usuário.', 16, 1); RETURN; END
        INSERT INTO Usuario (nome, nickname, email, senha, data_nascimento, status)
        VALUES (@nome, @nickname, @email, @senha_hash, @data_nascimento, 'ativo');
        SET @id_usuario = SCOPE_IDENTITY();
    END
    ELSE
    BEGIN
        UPDATE Usuario SET nome = @nome, email = @email,
                           senha = ISNULL(@senha_hash, senha)
        WHERE id_usuario = @id_usuario;
        IF @@ROWCOUNT = 0 BEGIN ROLLBACK; RAISERROR('Usuário não encontrado.', 16, 1); RETURN; END
    END

    DECLARE @atual VARCHAR(20) =
        CASE WHEN EXISTS (SELECT 1 FROM Admin     WHERE id_usuario = @id_usuario) THEN 'admin'
             WHEN EXISTS (SELECT 1 FROM Professor WHERE id_usuario = @id_usuario) THEN 'professor'
             WHEN EXISTS (SELECT 1 FROM Aluno     WHERE id_usuario = @id_usuario) THEN 'aluno'
             ELSE NULL END;

    IF @atual IS NOT NULL AND @atual <> @perfil
    BEGIN
        -- só remove o perfil antigo se não houver histórico dele
        IF (@atual = 'aluno' AND EXISTS (
                SELECT 1 FROM Aluno A WHERE A.id_usuario = @id_usuario AND
                   (EXISTS (SELECT 1 FROM Aluno_Aula    WHERE id_aluno = A.id_aluno)
                 OR EXISTS (SELECT 1 FROM Aluno_Questao WHERE id_aluno = A.id_aluno)
                 OR EXISTS (SELECT 1 FROM Aluno_Desafio WHERE id_aluno = A.id_aluno)
                 OR EXISTS (SELECT 1 FROM Aluno_Materia WHERE id_aluno = A.id_aluno)
                 OR EXISTS (SELECT 1 FROM Aluno_Trilha  WHERE id_aluno = A.id_aluno)
                 OR EXISTS (SELECT 1 FROM Certificado   WHERE id_aluno = A.id_aluno))))
        OR (@atual = 'professor' AND EXISTS (
                SELECT 1 FROM Professor P WHERE P.id_usuario = @id_usuario AND
                   (EXISTS (SELECT 1 FROM Materia   WHERE id_professor = P.id_professor)
                 OR EXISTS (SELECT 1 FROM Aula      WHERE id_professor = P.id_professor)
                 OR EXISTS (SELECT 1 FROM Questao   WHERE id_professor = P.id_professor)
                 OR EXISTS (SELECT 1 FROM Desafio   WHERE id_professor = P.id_professor)
                 OR EXISTS (SELECT 1 FROM Aprovacao WHERE id_professor = P.id_professor))))
        BEGIN
            ROLLBACK;
            RAISERROR('Este usuário já possui histórico no perfil atual. Inative a conta e crie outra para o novo perfil.', 16, 1);
            RETURN;
        END

        IF @atual = 'aluno'     DELETE FROM Aluno     WHERE id_usuario = @id_usuario;
        IF @atual = 'professor' DELETE FROM Professor WHERE id_usuario = @id_usuario;
        IF @atual = 'admin'     DELETE FROM Admin     WHERE id_usuario = @id_usuario;
        SET @atual = NULL;
    END

    IF @atual IS NULL
    BEGIN
        IF @perfil = 'aluno'     INSERT INTO Aluno (id_usuario) VALUES (@id_usuario);
        IF @perfil = 'professor' INSERT INTO Professor (id_usuario, especialidade, criador_conteudo) VALUES (@id_usuario, @especialidade, 1);
        IF @perfil = 'admin'     INSERT INTO Admin (id_usuario) VALUES (@id_usuario);
    END
    ELSE IF @perfil = 'professor'
        UPDATE Professor SET especialidade = @especialidade WHERE id_usuario = @id_usuario;

    INSERT INTO LogAtividade (id_usuario, descricao, data_hora)
    VALUES (@id_usuario, 'USUÁRIO EDITADO: ' + @nome + ' — Admin', GETDATE());

    COMMIT;
    SELECT @id_usuario AS id_usuario;
END
GO

-- 8.2 Excluir usuário: só se não tiver histórico; senão o admin deve INATIVAR
CREATE OR ALTER PROCEDURE sp_ExcluirUsuario
    @id_usuario INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @nome VARCHAR(100) = (SELECT nome FROM Usuario WHERE id_usuario = @id_usuario);
    IF @nome IS NULL BEGIN RAISERROR('Usuário não encontrado.', 16, 1); RETURN; END

    IF EXISTS (SELECT 1 FROM Aluno A WHERE A.id_usuario = @id_usuario AND
                  (EXISTS (SELECT 1 FROM Aluno_Aula    WHERE id_aluno = A.id_aluno)
                OR EXISTS (SELECT 1 FROM Aluno_Questao WHERE id_aluno = A.id_aluno)
                OR EXISTS (SELECT 1 FROM Aluno_Desafio WHERE id_aluno = A.id_aluno)
                OR EXISTS (SELECT 1 FROM Aluno_Materia WHERE id_aluno = A.id_aluno)
                OR EXISTS (SELECT 1 FROM Aluno_Trilha  WHERE id_aluno = A.id_aluno)
                OR EXISTS (SELECT 1 FROM Certificado   WHERE id_aluno = A.id_aluno)))
       OR EXISTS (SELECT 1 FROM Professor P WHERE P.id_usuario = @id_usuario AND
                  (EXISTS (SELECT 1 FROM Materia   WHERE id_professor = P.id_professor)
                OR EXISTS (SELECT 1 FROM Aula      WHERE id_professor = P.id_professor)
                OR EXISTS (SELECT 1 FROM Questao   WHERE id_professor = P.id_professor)
                OR EXISTS (SELECT 1 FROM Desafio   WHERE id_professor = P.id_professor)
                OR EXISTS (SELECT 1 FROM Aprovacao WHERE id_professor = P.id_professor)))
       OR EXISTS (SELECT 1 FROM TicketSuporte WHERE id_usuario_solicitante = @id_usuario OR id_usuario_destinatario = @id_usuario)
       OR EXISTS (SELECT 1 FROM TicketMensagem WHERE id_usuario_remetente = @id_usuario)
    BEGIN
        RAISERROR('Usuário com histórico não pode ser excluído. Use "Inativar" para preservar os dados.', 16, 1);
        RETURN;
    END

    BEGIN TRAN;
    UPDATE LogAtividade SET id_usuario = NULL WHERE id_usuario = @id_usuario;
    DELETE FROM Aluno     WHERE id_usuario = @id_usuario;
    DELETE FROM Professor WHERE id_usuario = @id_usuario;
    DELETE FROM Admin     WHERE id_usuario = @id_usuario;
    DELETE FROM Usuario   WHERE id_usuario = @id_usuario;
    INSERT INTO LogAtividade (id_usuario, descricao, data_hora)
    VALUES (NULL, 'USUÁRIO EXCLUÍDO: ' + @nome + ' — Admin', GETDATE());
    COMMIT;
END
GO

-- 8.3 Lista de usuários para a tabela do admin (busca/perfil/status opcionais)
CREATE OR ALTER PROCEDURE sp_ListarUsuarios
    @busca  VARCHAR(100) = NULL,
    @perfil VARCHAR(20)  = NULL,
    @status VARCHAR(20)  = NULL
AS
BEGIN
    SET NOCOUNT ON;

    SELECT * FROM (
        SELECT U.id_usuario, U.nome, U.nickname, U.email, U.status, U.foto_url,
               CASE WHEN Ad.id_admin    IS NOT NULL THEN 'admin'
                    WHEN P.id_professor IS NOT NULL THEN 'professor'
                    WHEN Al.id_aluno    IS NOT NULL THEN 'aluno' END AS perfil,
               P.especialidade
        FROM Usuario U
        LEFT JOIN Admin Ad ON Ad.id_usuario = U.id_usuario
        LEFT JOIN Professor P ON P.id_usuario = U.id_usuario
        LEFT JOIN Aluno Al ON Al.id_usuario = U.id_usuario
    ) X
    WHERE (@busca  IS NULL OR X.nome LIKE '%' + @busca + '%' OR X.email LIKE '%' + @busca + '%')
      AND (@perfil IS NULL OR X.perfil = @perfil)
      AND (@status IS NULL OR X.status = @status)
    ORDER BY X.nome;
END
GO

-- 8.4 Fila de aprovação pendente (tela do admin). @tipo NULL = todas.
CREATE OR ALTER PROCEDURE sp_ListarAprovacoes
    @tipo   VARCHAR(20) = NULL,        -- 'aula' | 'questao' | 'desafio'
    @status VARCHAR(20) = 'pendente'
AS
BEGIN
    SET NOCOUNT ON;

    SELECT Ap.id_aprovacao, Ap.tipo, Ap.status, Ap.data_submissao, U.nome AS autor,
           COALESCE(Au.titulo, LEFT(Q.enunciado, 100), Ds.titulo) AS titulo,
           COALESCE(M1.titulo, M2.titulo, M3.titulo) AS materia,
           COALESCE(Q.dificuldade, Ds.dificuldade) AS dificuldade,
           COALESCE(Au.conteudo, Q.enunciado, Ds.enunciado) AS corpo
    FROM Aprovacao Ap
    INNER JOIN Professor P ON P.id_professor = Ap.id_professor
    INNER JOIN Usuario U   ON U.id_usuario = P.id_usuario
    LEFT JOIN Aula Au      ON Au.id_aula = Ap.id_aula
    LEFT JOIN Materia M1   ON M1.id_materia = Au.id_materia
    LEFT JOIN Questao Q    ON Q.id_questao = Ap.id_questao
    LEFT JOIN Materia M2   ON M2.id_materia = Q.id_materia
    LEFT JOIN Desafio Ds   ON Ds.id_desafio = Ap.id_desafio
    LEFT JOIN Materia M3   ON M3.id_materia = Ds.id_materia
    WHERE (@tipo IS NULL OR Ap.tipo = @tipo)
      AND (@status IS NULL OR Ap.status = @status)
    ORDER BY Ap.data_submissao DESC;
END
GO

-- 8.5 Métricas do dashboard/plataforma do admin
CREATE OR ALTER PROCEDURE sp_MetricasPlataforma
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        (SELECT COUNT(*) FROM Usuario)                                   AS total_usuarios,
        (SELECT COUNT(*) FROM Usuario WHERE status = 'ativo')            AS usuarios_ativos,
        (SELECT COUNT(*) FROM Aluno)                                     AS total_alunos,
        (SELECT COUNT(*) FROM Professor)                                 AS total_professores,
        (SELECT COUNT(*) FROM Admin)                                     AS total_admins,
        (SELECT COUNT(*) FROM Aprovacao WHERE status = 'pendente')       AS pendentes_total,
        (SELECT COUNT(*) FROM Aprovacao WHERE status = 'pendente' AND tipo = 'aula')    AS pendentes_textos,
        (SELECT COUNT(*) FROM Aprovacao WHERE status = 'pendente' AND tipo = 'questao') AS pendentes_questoes,
        (SELECT COUNT(*) FROM Aprovacao WHERE status = 'pendente' AND tipo = 'desafio') AS pendentes_desafios,
        (SELECT COUNT(*) FROM Aprovacao WHERE status = 'aprovado')       AS aprovados_total,
        (SELECT COUNT(*) FROM Aula WHERE status_aprovacao = 'aprovado')
      + (SELECT COUNT(*) FROM Questao WHERE status_aprovacao = 'aprovado') AS conteudos_publicados,
        (SELECT COUNT(*) FROM Certificado)                               AS certificados_emitidos;
END
GO

-- 8.6 Log de atividades para a tela do admin (mais recentes primeiro)
CREATE OR ALTER PROCEDURE sp_ListarLogs
    @top INT = 50
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP (@top) data_hora, descricao
    FROM LogAtividade
    ORDER BY data_hora DESC, id_log DESC;
END
GO

-- 8.7 Ler a configuração de gamificação (tela de configurações do admin)
CREATE OR ALTER PROCEDURE sp_ObterConfiguracaoPlataforma
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP 1 chaves_questao_facil, chaves_questao_media, chaves_questao_dificil, chaves_certificado
    FROM ChavesConfig;
END
GO

PRINT 'HytechDB v4 aplicado com sucesso.';
GO
