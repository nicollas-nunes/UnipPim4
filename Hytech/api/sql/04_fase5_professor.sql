-- =====================================================================
-- HyTech — Fase 5: área do professor (procedures sp_Web*)
-- Rodar DEPOIS do HytechDB.sql, 02_fase2_conteudo.sql e 03_fase4_tickets.sql.
-- Idempotente (CREATE OR ALTER). Não altera as procedures usadas pelo WinForms.
-- ATENÇÃO: nunca foi executado em SQL Server real — teste numa CÓPIA do banco.
-- =====================================================================

USE HytechDB;
GO
-- Perfil do professor + contadores do painel
CREATE OR ALTER PROCEDURE sp_WebObterPerfilProfessor
    @id_usuario INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT P.id_professor, U.nome, U.nickname, U.email, U.foto_url, P.especialidade,
           (SELECT TOP 1 M.id_materia FROM Materia M WHERE M.titulo = P.especialidade ORDER BY M.id_materia)
               AS id_materia_especialidade,
           ISNULL(C.enviados, 0)  AS enviados,
           ISNULL(C.aprovados, 0) AS aprovados,
           ISNULL(C.pendentes, 0) AS pendentes,
           (SELECT COUNT(*) FROM Aluno A INNER JOIN Usuario UA ON UA.id_usuario = A.id_usuario
            WHERE UA.status = N'ativo') AS alunos_ativos
    FROM Professor P
    INNER JOIN Usuario U ON U.id_usuario = P.id_usuario
    OUTER APPLY (
        SELECT COUNT(*) AS enviados,
               SUM(CASE WHEN X.st = N'aprovado' THEN 1 ELSE 0 END) AS aprovados,
               SUM(CASE WHEN X.st = N'pendente' THEN 1 ELSE 0 END) AS pendentes
        FROM (SELECT status_aprovacao AS st FROM Aula    WHERE id_professor = P.id_professor
              UNION ALL SELECT status_aprovacao FROM Questao WHERE id_professor = P.id_professor
              UNION ALL SELECT status           FROM Desafio WHERE id_professor = P.id_professor) X
        WHERE X.st <> N'rascunho'
    ) C
    WHERE P.id_usuario = @id_usuario;
END
GO
-- "Meus conteúdos": 4 tabelas de resultado (aulas, questões, alternativas, desafios).
-- Traz o gabarito (correta) porque quem lê é o AUTOR, para poder editar.
CREATE OR ALTER PROCEDURE sp_WebListarMeusConteudos
    @id_professor INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Au.id_aula, Au.id_materia, M.titulo AS materia, Au.topico, Au.titulo, Au.conteudo,
           Au.exemplo_codigo, Au.dica_titulo, Au.dica_corpo, Au.status_aprovacao AS status,
           L.data_submissao AS data, L.motivo_rejeicao AS motivo
    FROM Aula Au
    INNER JOIN Materia M ON M.id_materia = Au.id_materia
    OUTER APPLY (SELECT TOP 1 Ap.data_submissao, Ap.motivo_rejeicao FROM Aprovacao Ap
                 WHERE Ap.id_aula = Au.id_aula ORDER BY Ap.data_submissao DESC, Ap.id_aprovacao DESC) L
    WHERE Au.id_professor = @id_professor
    ORDER BY Au.id_aula DESC;

    SELECT Q.id_questao, Q.id_materia, M.titulo AS materia, Q.enunciado, Q.dificuldade, Q.codigo_exemplo,
           Q.status_aprovacao AS status, Q.data_criacao AS data, L.motivo_rejeicao AS motivo
    FROM Questao Q
    INNER JOIN Materia M ON M.id_materia = Q.id_materia
    OUTER APPLY (SELECT TOP 1 Ap.motivo_rejeicao FROM Aprovacao Ap
                 WHERE Ap.id_questao = Q.id_questao ORDER BY Ap.data_submissao DESC, Ap.id_aprovacao DESC) L
    WHERE Q.id_professor = @id_professor
    ORDER BY Q.id_questao DESC;

    SELECT A.id_alternativa, A.id_questao, A.texto, A.correta
    FROM Alternativa A INNER JOIN Questao Q ON Q.id_questao = A.id_questao
    WHERE Q.id_professor = @id_professor
    ORDER BY A.id_questao, A.id_alternativa;

    SELECT D.id_desafio, D.id_materia, M.titulo AS materia, D.titulo, D.enunciado, D.dica, D.dificuldade,
           D.codigo_base, D.status, D.data_criacao AS data, L.motivo_rejeicao AS motivo
    FROM Desafio D
    INNER JOIN Materia M ON M.id_materia = D.id_materia
    OUTER APPLY (SELECT TOP 1 Ap.motivo_rejeicao FROM Aprovacao Ap
                 WHERE Ap.id_desafio = D.id_desafio ORDER BY Ap.data_submissao DESC, Ap.id_aprovacao DESC) L
    WHERE D.id_professor = @id_professor
    ORDER BY D.id_desafio DESC;
END
GO

-- Salvar aula do professor. Igual à sp_SalvarAula, mas grava dica_titulo e SEMPRE is_extra = 1 / chaves 0
-- (aula de professor nunca é obrigatória para o certificado e não paga chaves; o admin ajusta no WinForms).
CREATE OR ALTER PROCEDURE sp_WebSalvarAula
    @id_aula        INT = NULL,
    @id_professor   INT,
    @id_materia     INT,
    @titulo         NVARCHAR(100),
    @topico         NVARCHAR(150) = NULL,
    @conteudo       NVARCHAR(MAX) = NULL,
    @exemplo_codigo NVARCHAR(MAX) = NULL,
    @dica_titulo    NVARCHAR(150) = NULL,
    @dica_corpo     NVARCHAR(MAX) = NULL,
    @enviar         BIT = 0
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    IF NOT EXISTS (SELECT 1 FROM Materia WHERE id_materia = @id_materia)
    BEGIN RAISERROR(N'Matéria não encontrada.', 16, 1); RETURN; END

    IF @id_aula IS NULL
    BEGIN
        INSERT INTO Aula (id_materia, id_professor, titulo, topico, conteudo, exemplo_codigo,
                          dica_titulo, dica_corpo, is_extra, chaves_recompensa, status_aprovacao)
        VALUES (@id_materia, @id_professor, @titulo, @topico, @conteudo, @exemplo_codigo,
                @dica_titulo, @dica_corpo, 1, 0, CASE WHEN @enviar = 1 THEN N'pendente' ELSE N'rascunho' END);
        SET @id_aula = SCOPE_IDENTITY();      -- a trigger cria a Aprovacao se for 'pendente'
    END
    ELSE
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM Aula WHERE id_aula = @id_aula AND id_professor = @id_professor
                       AND status_aprovacao IN (N'rascunho', N'rejeitado'))
        BEGIN RAISERROR(N'Só é possível editar rascunhos ou conteúdos rejeitados do próprio professor.', 16, 1); RETURN; END

        UPDATE Aula SET id_materia = @id_materia, titulo = @titulo, topico = @topico, conteudo = @conteudo,
                        exemplo_codigo = @exemplo_codigo, dica_titulo = @dica_titulo, dica_corpo = @dica_corpo,
                        is_extra = 1
        WHERE id_aula = @id_aula;

        IF @enviar = 1 EXEC sp_EnviarParaAprovacao N'aula', @id_aula, @id_professor;
    END
    SELECT @id_aula AS id_aula;
END
GO

-- Aula do hub passa a trazer o autor (redefine a procedure criada no 02; mesmas colunas + autor)
CREATE OR ALTER PROCEDURE sp_WebListarAulasMateria
    @id_materia INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Au.id_aula, Au.topico, Au.titulo, Au.conteudo, Au.exemplo_codigo, Au.dica_titulo, Au.dica_corpo,
           Au.ordem, Au.is_extra, Au.chaves_recompensa, U.nome AS autor
    FROM Aula Au
    LEFT JOIN Professor P ON P.id_professor = Au.id_professor
    LEFT JOIN Usuario U   ON U.id_usuario   = P.id_usuario
    WHERE Au.id_materia = @id_materia AND Au.status_aprovacao = N'aprovado'
    ORDER BY Au.is_extra, ISNULL(Au.ordem, 9999), Au.id_aula;
END
GO
-- Desafios aprovados para o aluno, com o "concluído" dele
CREATE OR ALTER PROCEDURE sp_WebListarDesafiosAluno
    @id_aluno INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT D.id_desafio, D.id_materia, M.titulo AS materia, D.titulo, D.enunciado, D.dica, D.dificuldade,
           D.codigo_base, U.nome AS autor,
           CAST(CASE WHEN AD.concluido = 1 THEN 1 ELSE 0 END AS BIT) AS concluido
    FROM Desafio D
    INNER JOIN Materia M ON M.id_materia = D.id_materia
    LEFT JOIN Professor P ON P.id_professor = D.id_professor
    LEFT JOIN Usuario U   ON U.id_usuario   = P.id_usuario
    LEFT JOIN Aluno_Desafio AD ON AD.id_desafio = D.id_desafio AND AD.id_aluno = @id_aluno
    WHERE D.status = N'aprovado'
    ORDER BY M.ordem, D.id_desafio DESC;
END
GO
-- Concluir desafio + devolver quantas chaves ganhou (embrulha a sp_ConcluirDesafio; a trigger credita)
CREATE OR ALTER PROCEDURE sp_WebConcluirDesafio
    @id_aluno INT, @id_desafio INT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @antes INT = ISNULL((SELECT pontos_acumulados FROM Aluno WHERE id_aluno = @id_aluno), 0);
    DECLARE @ja BIT = CASE WHEN EXISTS (SELECT 1 FROM Aluno_Desafio
                                        WHERE id_aluno = @id_aluno AND id_desafio = @id_desafio AND concluido = 1)
                           THEN 1 ELSE 0 END;
    EXEC sp_ConcluirDesafio @id_aluno, @id_desafio;
    SELECT @ja AS ja_concluido,
           ISNULL((SELECT pontos_acumulados FROM Aluno WHERE id_aluno = @id_aluno), 0) - @antes AS chaves_ganhas,
           dbo.fn_SaldoChaves(@id_aluno) AS saldo_chaves;
END
GO
