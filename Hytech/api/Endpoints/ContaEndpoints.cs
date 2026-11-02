using System.Text.RegularExpressions;
using HytechApi.Data;
using HytechApi.Infra;
using HytechApi.Security;
using HytechApi.Services;
using static HytechApi.Data.Parametros;

namespace HytechApi.Endpoints;

/// <summary>Dados da própria conta (aluno e professor): perfil, senha, e-mail e desativação.</summary>
public static class ContaEndpoints
{
    private const int FotoMax = 1_000_000;   // ~1 MB de texto (o site reduz a foto para ~256px antes de enviar)
    private static readonly Regex FotoRe = new(@"^data:image/(png|jpe?g|webp);base64,", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static void MapConta(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/conta").RequireAuthorization("aluno-ou-professor");

        // PUT /conta/perfil   { nome?, nickname?, email?, foto? }   (o que não vier não muda)
        g.MapPut("/perfil", async (HttpRequest req, HttpContext ctx, IDb db, TokenService tokens) =>
        {
            var eu = UsuarioToken.De(ctx.User);
            var b = await Corpo.LerAsync(req);
            foreach (var k in new[] { "nome", "nickname", "email", "foto" })
                if (b.TipoInvalido(k)) return Erro("Dados inválidos.");

            string? Campo(string k) => b.Presente(k) ? b.Str(k) : null;
            var nome = Campo("nome")?.Trim();
            var nick = Campo("nickname")?.Trim();
            var email = Campo("email")?.Trim();
            var foto = Campo("foto");

            if (nome is not null && (nome.Length < 1 || nome.Length > 100)) return Erro("Informe o nome (até 100 caracteres).");
            if (nick is not null && (nick.Length < 1 || nick.Length > 49)) return Erro("Informe o nome de usuário (até 49 caracteres).");
            if (email is not null && !AuthEndpoints.EmailValido(email)) return Erro("E-mail inválido.");
            if (foto is not null)
            {
                if (!FotoRe.IsMatch(foto)) return Erro("Foto inválida (use PNG, JPG ou WEBP).");
                if (foto.Length > FotoMax) return Erro("Foto grande demais.");
            }
            // O e-mail NÃO muda por aqui: precisa de confirmação por código (POST /conta/email).
            if (email is not null && !email.Equals(eu.Email, StringComparison.OrdinalIgnoreCase))
                return Erro("Para trocar o e-mail é preciso confirmar com um código enviado ao novo endereço.");
            if (nome is null && nick is null && foto is null) return Erro("Nada para alterar.");

            await db.ExecutarAsync("sp_AtualizarPerfil",
                P("id_usuario", eu.Id), P("nome", nome), P("nickname", nick), P("foto_url", foto));
            return Results.Ok(await RespostaPerfil(db, tokens, eu.Email));
        }).RequireRateLimiting("conta");

        // ── Trocar senha (2 passos) ──
        // POST /conta/senha            { senhaAtual, novaSenha, confirmarSenha } -> confere a senha atual e manda um código ao e-mail
        // POST /conta/senha/confirmar  { codigo }                                -> troca a senha
        g.MapPost("/senha", async (HttpRequest req, HttpContext ctx, IDb db, ISenhaHasher hasher, Verificacao verif, Opcoes opc) =>
        {
            var eu = UsuarioToken.De(ctx.User);
            var b = await Corpo.LerAsync(req);
            var atual = b.Str("senhaAtual");
            var nova = b.Str("novaSenha");
            if (atual.Length == 0) return Erro("Informe a senha atual.");
            if (nova.Length < 8) return Erro("A nova senha precisa ter pelo menos 8 caracteres.");
            if (b.Existe("confirmarSenha") && b.Str("confirmarSenha") != nova) return Erro("A confirmação da nova senha não confere.");
            if (nova == atual) return Erro("A nova senha deve ser diferente da atual.");

            var u = await UsuarioAtual(db, eu);
            if (u is null || !hasher.Verificar(atual, u.Hash)) return Erro("Senha atual incorreta.");

            await verif.EmitirAsync("senha", Verificacao.ChaveUsuario(u.Id), u.Email, u.Nome, u.Id,
                new Dictionary<string, object?> { ["senha_hash"] = hasher.Hash(nova) });
            return Results.Ok(new { codigoEnviado = true, email = Texto.MascararEmail(u.Email), minutos = opc.CodigoMinutos });
        }).RequireRateLimiting("conta");

        g.MapPost("/senha/confirmar", async (HttpRequest req, HttpContext ctx, IDb db, Verificacao verif) =>
        {
            var eu = UsuarioToken.De(ctx.User);
            var b = await Corpo.LerAsync(req);
            var info = await verif.ConferirAsync("senha", Verificacao.ChaveUsuario(eu.Id), b.StrOuNumero("codigo"));
            if (info.Dados.SemObjeto) throw new ServicoException("Código inválido ou expirado. Peça um novo código.");
            await db.ExecutarAsync("sp_AlterarSenha", P("id_usuario", eu.Id), P("senha_hash", info.Dados.Str("senha_hash")));
            return Results.Ok(new { ok = true });
        }).RequireRateLimiting("conta");

        // ── Trocar e-mail (2 passos): o código vai para o NOVO endereço ──
        // POST /conta/email            { novoEmail, senha }
        // POST /conta/email/confirmar  { codigo }  -> devolve perfil + token novos
        g.MapPost("/email", async (HttpRequest req, HttpContext ctx, IDb db, ISenhaHasher hasher, Verificacao verif, Opcoes opc) =>
        {
            var eu = UsuarioToken.De(ctx.User);
            var b = await Corpo.LerAsync(req);
            var novo = b.Str("novoEmail").Trim().ToLowerInvariant();
            if (!AuthEndpoints.EmailValido(novo)) return Erro("E-mail inválido.");
            if (novo == eu.Email.ToLowerInvariant()) return Erro("Este já é o seu e-mail atual.");

            var u = await UsuarioAtual(db, eu);
            if (u is null || !hasher.Verificar(b.Str("senha"), u.Hash)) return Erro("Senha incorreta.");
            var disp = await db.PrimeiroAsync("sp_VerificarDisponibilidade", P("email", novo), P("id_usuario_ignorar", u.Id));
            if (disp?.Bool("email_em_uso") == true) return Erro("Este e-mail já está em uso.");

            await verif.EmitirAsync("email", Verificacao.ChaveUsuario(u.Id), novo, u.Nome, u.Id,
                new Dictionary<string, object?> { ["novo_email"] = novo });
            return Results.Ok(new { codigoEnviado = true, email = Texto.MascararEmail(novo), minutos = opc.CodigoMinutos });
        }).RequireRateLimiting("conta");

        g.MapPost("/email/confirmar", async (HttpRequest req, HttpContext ctx, IDb db, TokenService tokens, Verificacao verif) =>
        {
            var eu = UsuarioToken.De(ctx.User);
            var b = await Corpo.LerAsync(req);
            var info = await verif.ConferirAsync("email", Verificacao.ChaveUsuario(eu.Id), b.StrOuNumero("codigo"));
            var novo = info.Dados.Str("novo_email");
            if (novo.Length == 0) throw new ServicoException("Código inválido ou expirado. Peça um novo código.");
            await db.ExecutarAsync("sp_AtualizarPerfil", P("id_usuario", eu.Id), P("email", novo));
            return Results.Ok(await RespostaPerfil(db, tokens, novo));
        }).RequireRateLimiting("conta");

        // POST /conta/reenviar { finalidade: 'senha' | 'email' } -> manda o código de novo (mesmos dados)
        g.MapPost("/reenviar", async (HttpRequest req, HttpContext ctx, Verificacao verif, Opcoes opc) =>
        {
            var eu = UsuarioToken.De(ctx.User);
            var b = await Corpo.LerAsync(req);
            var finalidade = b.Str("finalidade");
            if (finalidade is not ("senha" or "email")) return Erro("Finalidade inválida.");

            var chave = Verificacao.ChaveUsuario(eu.Id);
            var p = await verif.PendenteAsync(finalidade, chave);
            if (p is null || p.Dados.SemObjeto) return Erro("Nenhuma troca em andamento. Comece de novo.");
            var para = finalidade == "email" ? p.Dados.Str("novo_email") : eu.Email;
            await verif.EmitirAsync(finalidade, chave, para, eu.Nome, eu.Id, p.Dados.Bruto);
            return Results.Ok(new { codigoEnviado = true, email = Texto.MascararEmail(para), minutos = opc.CodigoMinutos });
        }).RequireRateLimiting("conta");

        // ── Desativar a própria conta (LGPD: nada é apagado; o admin pode reativar) ──
        // POST /conta/desativar { nickname, senha }   (só aluno; professor fala com o administrador)
        g.MapPost("/desativar", async (HttpRequest req, HttpContext ctx, IDb db, ISenhaHasher hasher) =>
        {
            var eu = UsuarioToken.De(ctx.User);
            if (eu.Papel != "aluno")
                return Results.Json(new { erro = "Professores devem pedir a desativação da conta ao administrador." }, statusCode: 403);

            var b = await Corpo.LerAsync(req);
            static string Norm(string n) => n.Trim().TrimStart('@').ToLowerInvariant();
            var u = await UsuarioAtual(db, eu);
            if (u is null || Norm(b.Str("nickname")) != Norm(u.Nickname)) return Erro("O nome de usuário informado não confere.");
            if (!hasher.Verificar(b.Str("senha"), u.Hash)) return Erro("Senha incorreta.");

            await db.ExecutarAsync("sp_DesativarConta", P("id_usuario", u.Id));
            return Results.Ok(new { ok = true });
        }).RequireRateLimiting("conta");
    }

    private static async Task<UsuarioLogin?> UsuarioAtual(IDb db, UsuarioToken eu)
        => await db.PrimeiroAsync("sp_ObterUsuarioParaLogin", P("email", eu.Email)) is { } r ? UsuarioLogin.De(r) : null;

    // Resposta padrão após mudar nome/e-mail: perfil novo + token NOVO (o token carrega nome/e-mail).
    private static async Task<object> RespostaPerfil(IDb db, TokenService tokens, string emailAtual)
    {
        var u = UsuarioLogin.De(await db.UnicoAsync("sp_ObterUsuarioParaLogin", P("email", emailAtual.ToLowerInvariant())));
        var (token, expira) = tokens.Criar(u);
        return new { perfil = new { nome = u.Nome, nickname = u.Nickname, email = u.Email, foto = u.Foto }, token, expiraEm = expira };
    }

    private static IResult Erro(string msg) => Results.Json(new { erro = msg }, statusCode: 400);
}
