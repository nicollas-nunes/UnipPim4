-- ============================================================
-- HYTECH — FASE 4: TICKETS DE SUPORTE (leitura para a web)
--
-- Rodar DEPOIS do HytechDB.sql e do 02_fase2_conteudo.sql. É idempotente (CREATE OR ALTER).
-- Não altera nenhuma procedure usada pelo HytechAdmin-WinForms. Só cria 2 de leitura que devolvem
-- os ids de que a API precisa (as antigas devolvem só nomes):
--   sp_WebListarTickets  — tickets em que o usuário é solicitante ou destinatário
--   sp_WebObterTicket    — cabeçalho de UM ticket, somente para participante
-- Escrever (abrir, responder, fechar) continua com as procedures existentes:
--   sp_AbrirTicket, sp_ResponderTicket, sp_FecharTicket, sp_MensagensTicket.
-- ============================================================
USE HytechDB;
GO

CREATE OR ALTER PROCEDURE sp_WebListarTickets
    @id_usuario INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT T.id_ticket, T.assunto, T.status, T.data_abertura,
           T.id_materia, M.titulo AS materia,
           T.id_usuario_solicitante, US.nome AS solicitante,
           T.id_usuario_destinatario, UD.nome AS destinatario,
           ISNULL(VD.papel, N'') AS papel_destinatario,
           ISNULL((SELECT MAX(Msg.data_hora) FROM TicketMensagem Msg WHERE Msg.id_ticket = T.id_ticket),
                  T.data_abertura) AS ultima_atividade
    FROM TicketSuporte T
    INNER JOIN Usuario US ON US.id_usuario = T.id_usuario_solicitante
    INNER JOIN Usuario UD ON UD.id_usuario = T.id_usuario_destinatario
    LEFT JOIN Materia M   ON M.id_materia  = T.id_materia
    LEFT JOIN vw_UsuarioPapel VD ON VD.id_usuario = T.id_usuario_destinatario
    WHERE @id_usuario IN (T.id_usuario_solicitante, T.id_usuario_destinatario)
    ORDER BY ultima_atividade DESC, T.id_ticket DESC;
END
GO

CREATE OR ALTER PROCEDURE sp_WebObterTicket
    @id_ticket INT, @id_usuario INT
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM TicketSuporte
                   WHERE id_ticket = @id_ticket
                     AND @id_usuario IN (id_usuario_solicitante, id_usuario_destinatario))
    BEGIN RAISERROR(N'Você não participa deste ticket.', 16, 1); RETURN; END

    SELECT T.id_ticket, T.assunto, T.status, T.data_abertura,
           T.id_materia, M.titulo AS materia,
           T.id_usuario_solicitante, US.nome AS solicitante,
           T.id_usuario_destinatario, UD.nome AS destinatario,
           ISNULL(VD.papel, N'') AS papel_destinatario
    FROM TicketSuporte T
    INNER JOIN Usuario US ON US.id_usuario = T.id_usuario_solicitante
    INNER JOIN Usuario UD ON UD.id_usuario = T.id_usuario_destinatario
    LEFT JOIN Materia M   ON M.id_materia  = T.id_materia
    LEFT JOIN vw_UsuarioPapel VD ON VD.id_usuario = T.id_usuario_destinatario
    WHERE T.id_ticket = @id_ticket;
END
GO
