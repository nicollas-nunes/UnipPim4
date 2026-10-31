using System.Security.Claims;
using HytechApi.Data;
using HytechApi.Infra;
using HytechApi.Security;
using static HytechApi.Data.Parametros;

namespace HytechApi.Endpoints;

public record UsuarioRequest(string? Nome, string? Email, string? Perfil, string? Especialidade, string? Senha, string? Status);
public record StatusRequest(string? Status);
public record DecisaoRequest(string? Decisao);
public record MensagemRequest(string? Texto);
public record ChavesRequest(int QuestaoFacil, int QuestaoMedia, int QuestaoDificil, int Certificado);

/// <summary>
/// Rotas do painel de administração (usadas pelo HytechAdmin-WinForms).
/// Todas exigem token com papel "admin". O id do administrador vem SEMPRE do token (claim "sub"),
/// nunca do corpo da requisição: assim ninguém consegue se passar por outro admin nos logs e aprovações.
/// </summary>
public static class AdminEndpoints
{
    private static readonly string[] Tipos = { "aula", "questao", "desafio" };

    public static void MapAdmin(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/admin").RequireAuthorization("admin");

        // ── Usuários ────────────────────────────────────────────────────────────
        g.MapGet("/usuarios", async (IDb db) => Results.Ok(await db.ConsultarAsync("sp_ListarUsuarios", r => new
        {
            id = r.Int("id_usuario"),
            nome = r.Txt("nome"),
            nickname = r.Txt("nickname"),
            email = r.Txt("email"),
            perfil = r.Txt("papel"),                 // aluno | professor | admin
            especialidade = r.Txt("especialidade"),
            status = r.Txt("status"),
            dataCadastro = r.DataN("data_cadastro")
        })));

        g.MapPost("/usuarios", async (UsuarioRequest req, ClaimsPrincipal user, IDb db, ISenhaHasher hasher) =>
        {
            if (string.IsNullOrEmpty(req.Senha)) return Erro("Defina uma senha para o novo usuário.");
            var id = await SalvarUsuario(null, req, user, db, hasher);
            return Results.Json(new { id }, statusCode: 201);
        });

        g.MapPut("/usuarios/{id:int}", async (int id, UsuarioRequest req, ClaimsPrincipal user, IDb db, ISenhaHasher hasher) =>
        {
            await SalvarUsuario(id, req, user, db, hasher);     // senha vazia = mantém a atual
            return Results.NoContent();
        });

        g.MapPatch("/usuarios/{id:int}/status", async (int id, StatusRequest req, IDb db) =>
        {
            await db.ExecutarAsync("sp_AlterarStatusUsuario", P("@id_usuario", id), P("@novo_status", req.Status));
            return Results.NoContent();
        });

        g.MapDelete("/usuarios/{id:int}", async (int id, ClaimsPrincipal user, IDb db) =>
        {
            await db.ExecutarAsync("sp_ExcluirUsuario", P("@id_usuario", id), P("@id_usuario_admin", IdAdmin(user)));
            return Results.NoContent();
        });

        g.MapGet("/materias", async (IDb db) => Results.Ok(await db.ConsultarAsync("sp_ListarMaterias", r => new
        {
            id = r.Int("id_materia"),
            titulo = r.Txt("titulo"),
            icone = r.Txt("icone")
        })));

        // ── Conteúdos (aula | questao | desafio) ────────────────────────────────
        g.MapGet("/conteudos", async (IDb db) => Results.Ok(await db.ConsultarAsync("sp_AdminListarConteudos", Conteudo)));

        g.MapGet("/conteudos/{tipo}/{id:int}", async (string tipo, int id, IDb db) =>
        {
            if (!Tipos.Contains(tipo)) return Erro("Tipo de conteúdo inválido.");
            var c = (await db.ConsultarAsync("sp_AdminObterConteudo", Conteudo, P("@tipo", tipo), P("@id", id))).FirstOrDefault();
            return c is null ? Results.NotFound(new { erro = "Conteúdo não encontrado." }) : Results.Ok(c);
        });

        g.MapPost("/conteudos/{tipo}/{id:int}/decisao", async (string tipo, int id, DecisaoRequest req, ClaimsPrincipal user, IDb db) =>
        {
            if (!Tipos.Contains(tipo)) return Erro("Tipo de conteúdo inválido.");
            await db.ExecutarAsync("sp_AdminDecidirConteudo", P("@tipo", tipo), P("@id", id),
                P("@decisao", req.Decisao), P("@id_usuario_admin", IdAdmin(user)));
            return Results.NoContent();
        });

        g.MapDelete("/conteudos/{tipo}/{id:int}", async (string tipo, int id, ClaimsPrincipal user, IDb db) =>
        {
            if (!Tipos.Contains(tipo)) return Erro("Tipo de conteúdo inválido.");
            await db.ExecutarAsync("sp_AdminExcluirConteudo", P("@tipo", tipo), P("@id", id), P("@id_usuario_admin", IdAdmin(user)));
            return Results.NoContent();
        });

        // ── Tickets ─────────────────────────────────────────────────────────────
        g.MapGet("/tickets", async (string? escopo, ClaimsPrincipal user, IDb db) =>
            Results.Ok(await db.ConsultarAsync("sp_AdminListarTickets", r => new
            {
                id = r.Int("id_ticket"),
                assunto = r.Txt("assunto"),
                status = r.Txt("status"),
                materia = r.Txt("materia"),
                solicitante = r.Txt("solicitante"),
                destinatario = r.Txt("destinatario"),
                idDestinatario = r.Int("id_usuario_destinatario"),
                dataAbertura = r.DataN("data_abertura"),
                ultimaMensagem = r.DataN("ultima_mensagem"),
                totalMensagens = r.Int("total_mensagens")
            },
            P("@id_usuario_admin", IdAdmin(user)),
            P("@escopo", escopo == "todos" ? "todos" : "meus"))));

        g.MapGet("/tickets/{id:int}/mensagens", async (int id, ClaimsPrincipal user, IDb db) =>
            Results.Ok(await db.ConsultarAsync("sp_MensagensTicket", r => new
            {
                id = r.Int("id_mensagem"),
                idRemetente = r.Int("id_usuario_remetente"),
                nome = r.Txt("nome"),
                papel = r.Txt("papel"),
                texto = r.Txt("texto"),
                dataHora = r.DataN("data_hora")
            },
            P("@id_ticket", id), P("@id_usuario", IdAdmin(user)))));

        g.MapPost("/tickets/{id:int}/mensagens", async (int id, MensagemRequest req, ClaimsPrincipal user, IDb db) =>
        {
            var texto = req.Texto?.Trim() ?? "";
            if (texto.Length is 0 or > 4000) return Erro("A mensagem deve ter entre 1 e 4000 caracteres.");
            await db.ExecutarAsync("sp_ResponderTicket", P("@id_ticket", id),
                P("@id_usuario_remetente", IdAdmin(user)), P("@texto", texto));
            return Results.NoContent();
        });

        g.MapPost("/tickets/{id:int}/encerrar", async (int id, ClaimsPrincipal user, IDb db) =>
        {
            await db.ExecutarAsync("sp_FecharTicket", P("@id_ticket", id), P("@id_usuario", IdAdmin(user)));
            return Results.NoContent();
        });

        // ── Configuração, métricas e logs ───────────────────────────────────────
        g.MapGet("/config/chaves", async (IDb db) =>
        {
            var c = await PlataformaEndpoints.LerChavesAsync(db);
            return c is null ? Results.NotFound(new { erro = "Configuração não encontrada." }) : Results.Ok(c);
        });

        g.MapPut("/config/chaves", async (ChavesRequest req, ClaimsPrincipal user, IDb db) =>
        {
            await db.ExecutarAsync("sp_AtualizarConfiguracaoPlataforma",
                P("@chaves_questao_facil", req.QuestaoFacil), P("@chaves_questao_media", req.QuestaoMedia),
                P("@chaves_questao_dificil", req.QuestaoDificil), P("@chaves_certificado", req.Certificado),
                P("@id_usuario_admin", IdAdmin(user)));
            return Results.NoContent();
        });

        g.MapGet("/metricas", async (IDb db) =>
        {
            var m = (await db.ConsultarAsync("sp_MetricasPlataforma", r => new
            {
                totalUsuarios = r.Int("total_usuarios"),
                usuariosAtivos = r.Int("usuarios_ativos"),
                totalAlunos = r.Int("total_alunos"),
                totalProfessores = r.Int("total_professores"),
                totalAdmins = r.Int("total_admins"),
                pendentesTotal = r.Int("pendentes_total"),
                certificadosEmitidos = r.Int("certificados_emitidos"),
                chavesDistribuidas = r.Long("chaves_distribuidas")
            })).FirstOrDefault();
            return Results.Ok(m);
        });

        g.MapGet("/logs", async (int? top, IDb db) => Results.Ok(await db.ConsultarAsync("sp_ListarLogs", r => new
        {
            dataHora = r.DataN("data_hora"),
            descricao = r.Txt("descricao")
        }, P("@top", Math.Clamp(top ?? 100, 1, 500)))));
    }

    // ── Auxiliares ──────────────────────────────────────────────────────────────

    /// <summary>Id do administrador logado, lido do token.</summary>
    private static int IdAdmin(ClaimsPrincipal user)
        => int.Parse(user.FindFirstValue("sub") ?? throw new InvalidOperationException("Token sem 'sub'."));

    private static IResult Erro(string msg) => Results.BadRequest(new { erro = msg });

    private static async Task<int> SalvarUsuario(int? id, UsuarioRequest req, ClaimsPrincipal user, IDb db, ISenhaHasher hasher)
    {
        if (!string.IsNullOrEmpty(req.Senha) && req.Senha.Length < 8)
            throw new ServicoException("A senha precisa ter pelo menos 8 caracteres.");

        // O hash é gerado AQUI: o desktop envia a senha (por HTTPS) e nunca conhece o algoritmo.
        var hash = string.IsNullOrEmpty(req.Senha) ? null : hasher.Hash(req.Senha);

        var ids = await db.ConsultarAsync("sp_AdminSalvarUsuario", r => r.Int("id_usuario"),
            P("@id_usuario", id),
            P("@nome", req.Nome),
            P("@email", req.Email),
            P("@perfil", req.Perfil),
            P("@especialidade", req.Perfil == "professor" ? req.Especialidade : null),
            P("@senha_hash", hash),
            P("@status", req.Status),
            P("@id_usuario_admin", IdAdmin(user)));
        return ids.FirstOrDefault();
    }

    private static object Conteudo(Linha r) => new
    {
        tipo = r.Txt("tipo"),
        id = r.Int("id"),
        titulo = r.Txt("titulo"),
        materia = r.Txt("materia"),
        autor = r.Txt("autor"),
        status = r.Txt("status"),
        data = r.DataN("data"),
        dificuldade = r.Txt("dificuldade"),
        corpo = r.Txt("corpo")
    };
}
