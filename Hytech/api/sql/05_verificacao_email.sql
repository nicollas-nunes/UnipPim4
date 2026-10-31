-- =====================================================================
-- HyTech — Verificação por e-mail (cadastro, esqueci a senha, trocar senha/e-mail)
--          e desativação da própria conta (LGPD: os dados NÃO são apagados).
-- Rodar DEPOIS do HytechDB.sql e dos scripts 02 a 04.
-- Idempotente. Não altera tabelas nem procedures usadas pelo WinForms (só CRIA coisas novas).
-- ATENÇÃO: nunca foi executado em SQL Server real — teste numa CÓPIA do banco.
-- =====================================================================

USE HytechDB;
GO

-- Códigos de 6 dígitos enviados por e-mail. O código NUNCA é guardado em texto: só o hash (HMAC).
-- "dados" guarda o que será aplicado quando o código for confirmado (JSON):
--   cadastro -> nome, nickname, senha_hash, data_nascimento | senha -> senha_hash | email -> novo_email
IF OBJECT_ID(N'CodigoVerificacao', N'U') IS NULL
CREATE TABLE CodigoVerificacao (
    id_codigo    INT IDENTITY(1,1) PRIMARY KEY,
    finalidade   NVARCHAR(20)  NOT NULL,     -- 'cadastro' | 'recuperar' | 'senha' | 'email'
    chave        NVARCHAR(150) NOT NULL,     -- 'e:<email>' (fluxos públicos) ou 'u:<id_usuario>' (usuário logado)
    id_usuario   INT NULL,
    codigo_hash  NVARCHAR(100) NOT NULL,
    dados        NVARCHAR(MAX) NULL,
    criado_em    DATETIME NOT NULL DEFAULT GETDATE(),
    expira_em    DATETIME NOT NULL,
    tentativas   INT NOT NULL DEFAULT 0,
    usado        BIT NOT NULL DEFAULT 0,
    CONSTRAINT CK_CodigoVerificacao_Finalidade CHECK (finalidade IN (N'cadastro', N'recuperar', N'senha', N'email'))
);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_CodigoVerificacao_Chave')
    CREATE INDEX IX_CodigoVerificacao_Chave ON CodigoVerificacao(finalidade, chave, criado_em);
GO

-- Cria um código novo. Invalida os anteriores da mesma finalidade/chave.
-- Limites: 1 código a cada 30 s e no máximo 5 por hora (contra spam de e-mail).
CREATE OR ALTER PROCEDURE sp_CriarCodigoVerificacao
    @finalidade  NVARCHAR(20),
    @chave       NVARCHAR(150),
    @id_usuario  INT = NULL,
    @codigo_hash NVARCHAR(100),
    @dados       NVARCHAR(MAX) = NULL,
    @minutos     INT = 10
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;

    IF EXISTS (SELECT 1 FROM CodigoVerificacao WHERE finalidade = @finalidade AND chave = @chave
               AND criado_em > DATEADD(SECOND, -30, GETDATE()))
    BEGIN RAISERROR(N'Aguarde alguns segundos para pedir outro código.', 16, 1); RETURN; END

    IF (SELECT COUNT(*) FROM CodigoVerificacao WHERE finalidade = @finalidade AND chave = @chave
        AND criado_em > DATEADD(HOUR, -1, GETDATE())) >= 5
    BEGIN RAISERROR(N'Muitos códigos solicitados. Tente novamente mais tarde.', 16, 1); RETURN; END

    BEGIN TRAN;
        DELETE FROM CodigoVerificacao WHERE criado_em < DATEADD(DAY, -2, GETDATE());   -- faxina
        UPDATE CodigoVerificacao SET usado = 1 WHERE finalidade = @finalidade AND chave = @chave AND usado = 0;
        INSERT INTO CodigoVerificacao (finalidade, chave, id_usuario, codigo_hash, dados, expira_em)
        VALUES (@finalidade, @chave, @id_usuario, @codigo_hash, @dados, DATEADD(MINUTE, @minutos, GETDATE()));
    COMMIT;

    SELECT SCOPE_IDENTITY() AS id_codigo;
END
GO

-- Devolve o código mais recente ainda utilizável (não usado, não expirado, menos de 5 tentativas).
-- @incluir_expirados = 1: ignora expiração/tentativas (usado só para REENVIAR o código).
CREATE OR ALTER PROCEDURE sp_ObterCodigoVerificacao
    @finalidade        NVARCHAR(20),
    @chave             NVARCHAR(150),
    @incluir_expirados BIT = 0
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP 1 id_codigo, id_usuario, codigo_hash, dados, tentativas, expira_em
    FROM CodigoVerificacao
    WHERE finalidade = @finalidade AND chave = @chave AND usado = 0
      AND (@incluir_expirados = 1 OR (expira_em > GETDATE() AND tentativas < 5))
    ORDER BY criado_em DESC, id_codigo DESC;
END
GO

CREATE OR ALTER PROCEDURE sp_RegistrarTentativaCodigo
    @id_codigo INT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE CodigoVerificacao SET tentativas = tentativas + 1 WHERE id_codigo = @id_codigo;
END
GO

CREATE OR ALTER PROCEDURE sp_ConsumirCodigo
    @id_codigo INT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE CodigoVerificacao SET usado = 1 WHERE id_codigo = @id_codigo;
END
GO

-- E-mail / nickname já usados por OUTRO usuário? (@id_usuario_ignorar = o próprio usuário, ao trocar e-mail)
CREATE OR ALTER PROCEDURE sp_VerificarDisponibilidade
    @email             NVARCHAR(100) = NULL,
    @nickname          NVARCHAR(50)  = NULL,
    @id_usuario_ignorar INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    IF @email IS NOT NULL SET @email = LOWER(LTRIM(RTRIM(@email)));
    IF @nickname IS NOT NULL
    BEGIN
        SET @nickname = LTRIM(RTRIM(@nickname));
        IF LEFT(@nickname, 1) <> N'@' SET @nickname = N'@' + @nickname;
    END
    SELECT
        CAST(CASE WHEN @email IS NOT NULL AND EXISTS (SELECT 1 FROM Usuario WHERE email = @email
                  AND (@id_usuario_ignorar IS NULL OR id_usuario <> @id_usuario_ignorar)) THEN 1 ELSE 0 END AS BIT) AS email_em_uso,
        CAST(CASE WHEN @nickname IS NOT NULL AND EXISTS (SELECT 1 FROM Usuario WHERE nickname = @nickname
                  AND (@id_usuario_ignorar IS NULL OR id_usuario <> @id_usuario_ignorar)) THEN 1 ELSE 0 END AS BIT) AS nickname_em_uso;
END
GO

-- Desativa a PRÓPRIA conta (LGPD: nenhum dado é apagado; o admin pode reativar pelo HytechAdmin).
-- Só aluno e professor; administrador não se desativa por aqui.
CREATE OR ALTER PROCEDURE sp_DesativarConta
    @id_usuario INT
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    IF NOT EXISTS (SELECT 1 FROM Usuario WHERE id_usuario = @id_usuario)
    BEGIN RAISERROR(N'Usuário não encontrado.', 16, 1); RETURN; END
    IF EXISTS (SELECT 1 FROM Admin WHERE id_usuario = @id_usuario)
    BEGIN RAISERROR(N'Administradores não podem desativar a conta por aqui.', 16, 1); RETURN; END

    BEGIN TRAN;
        UPDATE Usuario SET status = N'inativo' WHERE id_usuario = @id_usuario;
        INSERT INTO LogAtividade (id_usuario, descricao, data_hora)
        SELECT id_usuario, N'CONTA DESATIVADA pelo próprio usuário: ' + nome, GETDATE() FROM Usuario WHERE id_usuario = @id_usuario;
    COMMIT;
END
GO
