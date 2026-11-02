-- ============================================================
-- SEED DO PRIMEIRO ADMINISTRADOR  (rodar 1x por ambiente, DEPOIS do HytechDB.sql)
--
-- NÃO coloque a senha real aqui nem suba este arquivo preenchido para o Git.
-- 1) Gere o hash:   dotnet run --project HytechApi -- --hash "SuaSenhaForte"
-- 2) Cole o hash abaixo (começa com $2a$ ou $2b$), rode, e depois APAGUE o hash do arquivo.
-- ============================================================
USE HytechDB;
GO

IF NOT EXISTS (SELECT 1 FROM Admin)
BEGIN
    EXEC sp_CadastrarAdmin
        @nome       = N'Administrador',
        @nickname   = N'@admin',
        @email      = N'admin@seudominio.com',
        @senha_hash = N'COLE_O_HASH_BCRYPT_AQUI';
    PRINT N'Administrador criado.';
END
ELSE
    PRINT N'Já existe administrador: nada a fazer.';
GO
