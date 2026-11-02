using System.Net.Mail;
using System.Security.Claims;
using HytechApi.Data;
using HytechApi.Security;
using static HytechApi.Data.Db;

namespace HytechApi.Endpoints;

public record LoginRequest(string? Email, string? Senha);
public record CadastroRequest(string? Nome, string? Nickname, string? Email, string? Senha, DateOnly? DataNascimento);

public static class AuthEndpoints
{
    public static void MapAuth(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/auth");

        // ── Login: sp_ObterUsuarioParaLogin + BCrypt + sp_RegistrarLogin ──────────────
        g.MapPost("/login", async (LoginRequest req, Db db, ISenhaHasher hasher, TokenService tokens) =>
        {
            if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrEmpty(req.Senha))
                return Results.BadRequest(new { erro = "Informe e-mail e senha." });

            var u = (await db.ConsultarAsync("sp_ObterUsuarioParaLogin", r => new UsuarioLogin(
                        r.Int("id_usuario"), r.Txt("nome"), r.Txt("nickname"), r.Txt("email"),
                        r.Txt("status"), r.Txt("papel"),
                        r.IntN("id_aluno"), r.IntN("id_professor"), r.IntN("id_admin"),
                        r.Txt("senha_hash")),
                    P("@email", req.Email.Trim()))).FirstOrDefault();

            // Sempre confere um hash (mesmo sem usuário) e responde igual: não revela se o e-mail existe.
            var senhaOk = hasher.Verificar(req.Senha, u?.Hash ?? SenhaHasherBcrypt.HashFalso);
            if (u is null || !senhaOk)
                return Results.Json(new { erro = "E-mail ou senha incorretos." }, statusCode: 401);

            // Só depois de a senha estar correta é seguro dizer que a conta está inativa.
            if (u.Status != "ativo")
                return Results.Json(new { erro = "Conta inativa. Fale com o administrador." }, statusCode: 403);
            if (string.IsNullOrEmpty(u.Papel))
                return Results.Json(new { erro = "Conta sem perfil definido." }, statusCode: 403);

            await db.ExecutarAsync("sp_RegistrarLogin", P("@id_usuario", u.Id));   // log + streak do aluno

            var (token, expira) = tokens.Criar(u);
            return Results.Ok(new
            {
                token,
                expiraEm = expira,
                usuario = new { id = u.Id, u.Nome, u.Nickname, u.Email, papel = u.Papel }
            });
        }).RequireRateLimiting("auth");

        // ── Cadastro público: SOMENTE aluno ──────────────────────────────────────────
        // Professor e admin são criados pelo painel do administrador (sp_AdminSalvarUsuario).
        g.MapPost("/cadastro", async (CadastroRequest req, Db db, ISenhaHasher hasher) =>
        {
            var nome = req.Nome?.Trim() ?? "";
            var nick = req.Nickname?.Trim() ?? "";
            var email = req.Email?.Trim() ?? "";

            if (nome.Length is 0 or > 100) return Erro("Informe o nome (até 100 caracteres).");
            if (nick.Length is 0 or > 49)  return Erro("Informe o nome de usuário (até 49 caracteres).");
            if (!EmailValido(email) || email.Length > 100) return Erro("E-mail inválido.");
            if (string.IsNullOrEmpty(req.Senha) || req.Senha.Length < 8) return Erro("A senha precisa ter pelo menos 8 caracteres.");
            if (req.DataNascimento is DateOnly d && (d > DateOnly.FromDateTime(DateTime.Today) || d.Year < 1900))
                return Erro("Data de nascimento inválida.");

            var ids = (await db.ConsultarAsync("sp_CadastrarAluno",
                r => new { IdUsuario = r.Int("id_usuario"), IdAluno = r.Int("id_aluno") },
                P("@nome", nome), P("@nickname", nick), P("@email", email),
                P("@senha_hash", hasher.Hash(req.Senha)),
                P("@data_nascimento", req.DataNascimento?.ToDateTime(TimeOnly.MinValue)))).First();

            return Results.Json(ids, statusCode: 201);
        }).RequireRateLimiting("auth");

        // ── Quem sou eu (valida o token) ─────────────────────────────────────────────
        g.MapGet("/me", (ClaimsPrincipal user) => Results.Ok(new
        {
            id = user.FindFirstValue("sub"),
            nome = user.FindFirstValue("name"),
            email = user.FindFirstValue("email"),
            papel = user.FindFirstValue("role")
        })).RequireAuthorization();
    }

    private static IResult Erro(string msg) => Results.BadRequest(new { erro = msg });

    private static bool EmailValido(string e)
        => MailAddress.TryCreate(e, out var m) && m.Address == e;
}
