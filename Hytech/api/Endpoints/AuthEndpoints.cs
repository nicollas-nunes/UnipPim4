using System.Globalization;
using System.Security.Claims;
using System.Text.RegularExpressions;
using HytechApi.Data;
using HytechApi.Infra;
using HytechApi.Security;
using HytechApi.Services;
using static HytechApi.Data.Parametros;

namespace HytechApi.Endpoints;

/// <summary>Login, cadastro (com código por e-mail) e "esqueci a senha".</summary>
public static class AuthEndpoints
{
    private static readonly Regex EmailRe = new(@"^[^\s@]+@[^\s@]+\.[^\s@]+$", RegexOptions.Compiled);

    public static bool EmailValido(string e) => EmailRe.IsMatch(e) && e.Length <= 100;

    public static void MapAuth(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/auth");

        // ── Login do site (aluno e professor). O admin usa o aplicativo desktop (/auth/login-admin). ──
        g.MapPost("/login", (HttpRequest req, IDb db, ISenhaHasher hasher, TokenService tokens)
            => Entrar(req, false, db, hasher, tokens)).RequireRateLimiting("auth");

        // Login do app desktop: só admin. A checagem do papel vem ANTES de sp_RegistrarLogin, então
        // um aluno que tente entrar por aqui não gera log nem mexe no streak. Resposta é igual à de senha errada.
        g.MapPost("/login-admin", (HttpRequest req, IDb db, ISenhaHasher hasher, TokenService tokens)
            => Entrar(req, true, db, hasher, tokens)).RequireRateLimiting("auth");

        // ── Cadastro público: SOMENTE aluno. Professor e admin são criados pelo painel do administrador. ──
        // Passo 1: valida e manda um código por e-mail. A conta só é criada no passo 2 (confirmar).
        g.MapPost("/cadastro", async (HttpRequest req, IDb db, ISenhaHasher hasher, Verificacao verif, Opcoes opc) =>
        {
            var b = await Corpo.LerAsync(req);
            var nome = b.Str("nome").Trim();
            var nick = b.Str("nickname").Trim();
            var email = b.Str("email").Trim().ToLowerInvariant();
            var senha = b.Str("senha");
            var nasc = b.Verdadeiro("dataNascimento") ? b.Str("dataNascimento") : null;   // 'AAAA-MM-DD' (opcional)

            if (nome.Length is 0 or > 100) return Erro("Informe o nome (até 100 caracteres).");
            if (nick.Length is 0 or > 49) return Erro("Informe o nome de usuário (até 49 caracteres).");
            if (!EmailValido(email)) return Erro("E-mail inválido.");
            if (senha.Length < 8) return Erro("A senha precisa ter pelo menos 8 caracteres.");
            if (nasc is not null && DataValida(nasc) is null) return Erro("Data de nascimento inválida.");

            var disp = await db.PrimeiroAsync("sp_VerificarDisponibilidade", P("email", email), P("nickname", nick));
            if (disp?.Bool("email_em_uso") == true) return Erro("Este e-mail já está cadastrado.");
            if (disp?.Bool("nickname_em_uso") == true) return Erro("Este nome de usuário já está em uso.");

            await verif.EmitirAsync("cadastro", Verificacao.ChaveEmail(email), email, nome, dados: new Dictionary<string, object?>
            {
                ["nome"] = nome, ["nickname"] = nick, ["senha_hash"] = hasher.Hash(senha), ["data_nascimento"] = nasc
            });
            return Results.Json(new { verificacao = "pendente", email = Texto.MascararEmail(email), minutos = opc.CodigoMinutos }, statusCode: 202);
        }).RequireRateLimiting("auth");

        // Passo 2: confere o código e cria a conta.
        g.MapPost("/cadastro/confirmar", async (HttpRequest req, IDb db, Verificacao verif) =>
        {
            var b = await Corpo.LerAsync(req);
            var email = b.Str("email").Trim().ToLowerInvariant();
            var d = (await verif.ConferirAsync("cadastro", Verificacao.ChaveEmail(email), b.StrOuNumero("codigo"))).Dados;
            if (d.SemObjeto) throw new ServicoException("Código inválido ou expirado. Peça um novo código.");

            var nasc = d.Str("data_nascimento") is { Length: > 0 } s ? DataValida(s) : null;
            var ids = await db.UnicoAsync("sp_CadastrarAluno",
                P("nome", d.Str("nome")), P("nickname", d.Str("nickname")), P("email", email),
                P("senha_hash", d.Str("senha_hash")), P("data_nascimento", nasc));
            return Results.Json(new { id_usuario = ids.Int("id_usuario"), id_aluno = ids.Int("id_aluno") }, statusCode: 201);
        }).RequireRateLimiting("auth");

        g.MapPost("/cadastro/reenviar", async (HttpRequest req, Verificacao verif, Opcoes opc) =>
        {
            var b = await Corpo.LerAsync(req);
            var email = b.Str("email").Trim().ToLowerInvariant();
            var chave = Verificacao.ChaveEmail(email);
            var p = await verif.PendenteAsync("cadastro", chave);
            if (p is null || p.Dados.SemObjeto) return Erro("Cadastro não encontrado ou expirado. Preencha o formulário de novo.");
            await verif.EmitirAsync("cadastro", chave, email, p.Dados.Str("nome"), dados: p.Dados.Bruto);
            return Results.Ok(new { verificacao = "pendente", email = Texto.MascararEmail(email), minutos = opc.CodigoMinutos });
        }).RequireRateLimiting("auth");

        // ── Esqueci a senha (aluno e professor) ──
        // Sempre responde igual: não revela se o e-mail existe.
        g.MapPost("/esqueci", async (HttpRequest req, IDb db, Verificacao verif, Opcoes opc) =>
        {
            var b = await Corpo.LerAsync(req);
            var email = b.Str("email").Trim().ToLowerInvariant();
            if (!EmailRe.IsMatch(email)) return Erro("E-mail inválido.");

            var r = await db.PrimeiroAsync("sp_ObterUsuarioParaLogin", P("email", email));
            if (r is not null)
            {
                var u = UsuarioLogin.De(r);
                if (u.Status == "ativo" && u.Papel is "aluno" or "professor")
                {
                    try { await verif.EmitirAsync("recuperar", Verificacao.ChaveEmail(email), email, u.Nome, u.Id); }
                    catch (ServicoException) { /* limite de pedidos: mesma resposta */ }
                }
            }
            return Results.Ok(new { ok = true, mensagem = "Se este e-mail tiver uma conta, enviamos um código para ele.", minutos = opc.CodigoMinutos });
        }).RequireRateLimiting("auth");

        g.MapPost("/esqueci/redefinir", async (HttpRequest req, IDb db, ISenhaHasher hasher, Verificacao verif) =>
        {
            var b = await Corpo.LerAsync(req);
            var email = b.Str("email").Trim().ToLowerInvariant();
            var nova = b.Str("novaSenha");
            if (nova.Length < 8) return Erro("A nova senha precisa ter pelo menos 8 caracteres.");

            var info = await verif.ConferirAsync("recuperar", Verificacao.ChaveEmail(email), b.StrOuNumero("codigo"));
            await db.ExecutarAsync("sp_AlterarSenha", P("id_usuario", info.IdUsuario), P("senha_hash", hasher.Hash(nova)));
            return Results.Ok(new { ok = true });
        }).RequireRateLimiting("auth");

        // ── Quem sou eu (valida o token) ──
        g.MapGet("/me", (ClaimsPrincipal user) =>
        {
            var u = UsuarioToken.De(user);
            return Results.Ok(new { id = u.Id, nome = u.Nome, email = u.Email, papel = u.Papel, id_aluno = u.IdAluno, id_professor = u.IdProfessor });
        }).RequireAuthorization("login");
    }

    /// <summary>Fluxo único de login. <paramref name="soAdmin"/> restringe ao papel admin (app desktop).</summary>
    private static async Task<IResult> Entrar(HttpRequest req, bool soAdmin, IDb db, ISenhaHasher hasher, TokenService tokens)
    {
        var b = await Corpo.LerAsync(req);
        var email = b.Str("email").Trim();
        var senha = b.Str("senha");
        if (email.Length == 0 || senha.Length == 0) return Erro("Informe e-mail e senha.");

        var linha = await db.PrimeiroAsync("sp_ObterUsuarioParaLogin", P("email", email));
        var u = linha is null ? null : UsuarioLogin.De(linha);

        // Sempre confere um hash (mesmo sem usuário) e responde igual: não revela se o e-mail existe.
        var senhaOk = hasher.Verificar(senha, u is { Hash.Length: > 0 } ? u.Hash : hasher.HashFalso);
        if (u is null || !senhaOk || (soAdmin && u.Papel != "admin"))
            return Results.Json(new { erro = "E-mail ou senha incorretos." }, statusCode: 401);

        // Só depois de a senha estar correta é seguro dizer que a conta está inativa.
        if (u.Status != "ativo")
            return Results.Json(new { erro = "Conta desativada. Para reativar, fale com o administrador." }, statusCode: 403);
        // O admin usa o aplicativo desktop (HytechAdmin). O site é só aluno e professor.
        if (!soAdmin && u.Papel == "admin")
            return Results.Json(new { erro = "Administradores acessam pelo aplicativo HytechAdmin (Windows)." }, statusCode: 403);
        if (string.IsNullOrEmpty(u.Papel))
            return Results.Json(new { erro = "Conta sem perfil definido." }, statusCode: 403);

        await db.ExecutarAsync("sp_RegistrarLogin", P("id_usuario", u.Id));   // log + streak do aluno

        var (token, expira) = tokens.Criar(u);
        return Results.Ok(new
        {
            token,
            expiraEm = expira,
            usuario = new
            {
                id = u.Id, nome = u.Nome, nickname = u.Nickname, email = u.Email, papel = u.Papel, foto = u.Foto,
                id_aluno = u.IdAluno, id_professor = u.IdProfessor
            }
        });
    }

    /// <summary>'AAAA-MM-DD' válida, não futura e a partir de 1900; senão null.</summary>
    private static DateOnly? DataValida(string iso)
    {
        if (!DateOnly.TryParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return null;
        if (d > DateOnly.FromDateTime(DateTime.UtcNow) || d.Year < 1900) return null;
        return d;
    }

    private static IResult Erro(string msg) => Results.Json(new { erro = msg }, statusCode: 400);
}
