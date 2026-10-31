using HytechApi.Data;
using HytechApi.Infra;
using HytechApi.Security;
using static HytechApi.Data.Parametros;

namespace HytechApi.Endpoints;

/// <summary>
/// Tickets de suporte: o aluno abre, o professor (escolhido pelo banco) responde e encerra.
/// Aluno e professor usam as mesmas rotas; o banco só deixa participante ler/escrever.
/// (O admin tem as próprias rotas em /admin/tickets.)
/// </summary>
public static class TicketsEndpoints
{
    public static void MapTickets(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/tickets").RequireAuthorization("aluno-ou-professor");

        // Lista: aluno vê os que abriu; professor, os que recebeu.
        g.MapGet("/", async (HttpContext ctx, IDb db) =>
        {
            var eu = UsuarioToken.De(ctx.User);
            return Results.Ok(await db.ConsultarAsync("sp_WebListarTickets", t => Formatar(t, eu.Id), P("id_usuario", eu.Id)));
        });

        // Detalhe + conversa (só participante; senão o banco responde 400).
        g.MapGet("/{id}", async (string id, HttpContext ctx, IDb db) =>
        {
            var eu = UsuarioToken.De(ctx.User);
            if (Rotas.IdInt(id) is not int idTicket) return Erro("Ticket inválido.");
            var t = await db.PrimeiroAsync("sp_WebObterTicket", P("id_ticket", idTicket), P("id_usuario", eu.Id));
            if (t is null) return Results.NotFound(new { erro = "Ticket não encontrado." });

            var msgs = await db.ConsultarAsync("sp_MensagensTicket", m => new
            {
                id = m.Int("id_mensagem"),
                remetente = m.Txt("remetente"),          // 'aluno' | 'professor': define o lado do balão
                nome = m.Txt("nome"),
                papel = m.Txt("papel"),
                texto = m.Txt("texto"),
                dataHora = m.DataN("data_hora"),
                minha = m.Int("id_usuario_remetente") == eu.Id
            }, P("id_ticket", idTicket), P("id_usuario", eu.Id));

            var f = Formatar(t, eu.Id);
            return Results.Ok(new
            {
                f.id, f.assunto, f.status, f.criadoEm, f.ultimaAtividade, f.idMateria, f.materia,
                f.aluno, f.professor, f.papelDestinatario, f.souSolicitante,
                mensagens = msgs
            });
        });

        // Aluno abre ticket. O banco escolhe o destinatário (professor da matéria; senão o admin).
        g.MapPost("/", async (HttpRequest req, HttpContext ctx, IDb db) =>
        {
            var eu = UsuarioToken.De(ctx.User);
            var b = await Corpo.LerAsync(req);
            var assunto = b.Str("assunto").Trim();
            var texto = b.Str("mensagem").Trim();
            var idMateria = b.IdInt("idMateria");
            if (assunto.Length is 0 or > 150) return Erro("Informe o assunto (até 150 caracteres).");
            if (texto.Length is 0 or > 4000) return Erro("Escreva a mensagem (até 4000 caracteres).");
            if (idMateria is null) return Erro("Escolha a matéria.");

            var x = await db.UnicoAsync("sp_AbrirTicket",
                P("id_usuario_solicitante", eu.Id),             // do TOKEN, nunca do corpo
                P("assunto", assunto), P("descricao", texto), P("id_materia", idMateria));
            return Results.Json(new { id = x.Int("id_ticket") }, statusCode: 201);
        }).RequireAuthorization("aluno").RequireRateLimiting("tickets");

        // Nova mensagem (aluno ou professor do ticket). O status muda por trigger no banco.
        g.MapPost("/{id}/mensagens", async (string id, HttpRequest req, HttpContext ctx, IDb db) =>
        {
            var eu = UsuarioToken.De(ctx.User);
            var b = await Corpo.LerAsync(req);
            var texto = b.Str("texto").Trim();
            if (Rotas.IdInt(id) is not int idTicket) return Erro("Ticket inválido.");
            if (texto.Length is 0 or > 4000) return Erro("Escreva a mensagem (até 4000 caracteres).");

            await db.ExecutarAsync("sp_ResponderTicket",
                P("id_ticket", idTicket), P("id_usuario_remetente", eu.Id), P("texto", texto));
            return Results.Json(new { ok = true }, statusCode: 201);
        }).RequireRateLimiting("tickets");

        // Só o professor destinatário encerra (o banco confere).
        g.MapPost("/{id}/fechar", async (string id, HttpContext ctx, IDb db) =>
        {
            var eu = UsuarioToken.De(ctx.User);
            if (Rotas.IdInt(id) is not int idTicket) return Erro("Ticket inválido.");
            await db.ExecutarAsync("sp_FecharTicket", P("id_ticket", idTicket), P("id_usuario", eu.Id));
            return Results.Ok(new { ok = true });
        }).RequireAuthorization("professor");
    }

    private sealed record TicketDto(int id, string assunto, string status, DateTime? criadoEm, DateTime? ultimaAtividade,
        int? idMateria, string materia, string aluno, string professor, string papelDestinatario, bool souSolicitante);

    private static TicketDto Formatar(Linha t, int meuId) => new(
        t.Int("id_ticket"), t.Txt("assunto"), t.Txt("status"),
        t.DataN("data_abertura"), t.DataN("ultima_atividade") ?? t.DataN("data_abertura"),
        t.IntN("id_materia"), t.Txt("materia"),
        t.Txt("solicitante"), t.Txt("destinatario"), t.Txt("papel_destinatario"),
        t.Int("id_usuario_solicitante") == meuId);

    private static IResult Erro(string msg) => Results.Json(new { erro = msg }, statusCode: 400);
}
