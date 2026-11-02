using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using static HytechApi.Testes.T;

namespace HytechApi.Testes;

public static class TestesSeguranca
{
    public static async Task Todos()
    {
        await Seguranca();
        await Configuracao();
        await Verificacao();
        await Robustez();
    }

    static async Task Seguranca()
    {
        Grupo("Fase 6 — segurança e publicação");
        await using var s = await Servidor.Subir();

        await Caso("cabeçalhos de segurança e sem Server/X-Powered-By", async () =>
        {
            var r = await s.Get("/saude"); Status(200, r);
            Verdade(r.Raw.Headers.GetValues("Content-Security-Policy").First().Contains("frame-ancestors 'none'"), "CSP sem frame-ancestors");
            Igual("nosniff", r.Raw.Headers.GetValues("X-Content-Type-Options").First());
            Verdade(!r.Raw.Headers.Contains("X-Powered-By") && !r.Raw.Headers.Contains("Server"), "vazou Server/X-Powered-By");
        });

        await Caso("corpo grande demais => 413; foto de perfil tem limite maior", async () =>
        {
            var grande = JsonSerializer.Serialize(new { email = "a@b.com", senha = new string('x', 200000) });
            Status(413, await s.Req("/auth/login", "POST", grande));
            var tk = await s.Login("aluno@hytech.com");
            var foto = "data:image/png;base64," + new string('A', 900000);
            Verdade((await s.Req("/conta/perfil", "PUT", new { foto }, tk)).Status != 413, "foto de 900 KB deveria ser aceita");
        });

        await Caso("JSON inválido => 400; erro interno não vaza detalhes", async () =>
        {
            Status(400, await s.Req("/auth/login", "POST", "{ isto não é json"));
            await using var q = await Servidor.Subir();
            // um IDb que quebra com uma mensagem "secreta"
            await using var q2 = await SubirQuebrado();
            var r = await q2.Req("/auth/login", "POST", new { email = "a@b.com", senha = "x" });
            Status(500, r); Verdade(!r.Json.ToString().Contains("secreto"), "vazou detalhe interno");
        });

        await Caso("limite geral por IP => 429", async () =>
        {
            await using var s2 = await Servidor.Subir(new() { ["Plataforma:LimiteGeralPorMinuto"] = "5" });
            var cod = new List<int>();
            for (var i = 0; i < 8; i++) cod.Add((await s2.Get("/saude")).Status);
            Verdade(cod.Contains(429), string.Join(",", cod));
        });

        await Caso("CORS: só origens permitidas", async () =>
        {
            var ok = await s.Req("/saude", cab: new() { ["Origin"] = "http://localhost:5500" });
            var ruim = await s.Req("/saude", cab: new() { ["Origin"] = "http://malvado.example" });
            Igual("http://localhost:5500", ok.Raw.Headers.TryGetValues("Access-Control-Allow-Origin", out var v) ? v.First() : null);
            Verdade(!ruim.Raw.Headers.Contains("Access-Control-Allow-Origin"), "origem estranha foi liberada");
        });

        await Caso("ServirSite: a API entrega o site e a API continua em pé", async () =>
        {
            var web = AcharWeb();
            await using var s2 = await Servidor.Subir(new() { ["Plataforma:ServirSite"] = "true", ["Plataforma:SiteDir"] = web });
            var h = await s2.Http.GetAsync("/");
            Igual(200, (int)h.StatusCode); Verdade((await h.Content.ReadAsStringAsync()).Contains("<title>"), "index.html não veio");
            Igual(200, (int)(await s2.Http.GetAsync("/js/api.js")).StatusCode);
            Status(401, await s2.Get("/materias"));
            Status(404, await s2.Get("/nao-existe"));
        });
    }

    static string AcharWeb()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "web", "index.html"))) return Path.Combine(d.FullName, "web");
        throw new FalhaException("pasta web/ não encontrada");
    }

    static async Task<Servidor> SubirQuebrado()
    {
        var s = new Servidor { Db = new BancoFalso(), Email = new EmailMemoria() };
        s.App = HytechApp.Construir(new[] { "--environment=Development", "--urls=http://127.0.0.1:0" }, b =>
        {
            b.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:Key"] = new string('x', 40) });
            b.Services.AddSingleton<HytechApi.Data.IDb>(new DbQuebrado());
            b.Logging.ClearProviders();
        });
        await s.App.StartAsync();
        s.Url = s.App.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>().Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>()!.Addresses.First();
        s.Http = new HttpClient { BaseAddress = new Uri(s.Url) };
        return s;
    }

    sealed class DbQuebrado : HytechApi.Data.IDb
    {
        public Task<List<HytechApi.Data.Linha>> ExecAsync(string proc, params HytechApi.Data.Param[] ps) => throw new Exception("SELECT secreto FROM tabela");
        public Task<List<List<HytechApi.Data.Linha>>> ExecMultiAsync(string proc, params HytechApi.Data.Param[] ps) => throw new Exception("SELECT secreto FROM tabela");
    }

    sealed class Env : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "t";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    static IConfiguration Cfg(params (string, string)[] kv)
        => new ConfigurationBuilder().AddInMemoryCollection(kv.ToDictionary(k => k.Item1, k => (string?)k.Item2)).Build();

    static async Task Configuracao()
    {
        Grupo("Configuração (produção recusa valores de exemplo)");
        var chave = ("Jwt:Key", new string('x', 40));
        var prod = new Env();

        await Caso("chave curta ou ausente é recusada", () =>
        {
            Lanca<InvalidOperationException>(() => Opcoes.Carregar(Cfg(), new Env { EnvironmentName = "Development" }), "Jwt:Key");
            Lanca<InvalidOperationException>(() => Opcoes.Carregar(Cfg(("Jwt:Key", "curta")), new Env { EnvironmentName = "Development" }), "Jwt:Key");
            return Task.CompletedTask;
        });

        await Caso("produção exige e-mail configurado (ou modo console explícito)", () =>
        {
            Lanca<InvalidOperationException>(() => Opcoes.Carregar(Cfg(chave), prod), "e-mail");
            Opcoes.Carregar(Cfg(chave, ("Email:Modo", "console")), prod);
            Opcoes.Carregar(Cfg(chave, ("Email:Host", "smtp.gmail.com"), ("Email:Usuario", "a@gmail.com"), ("Email:Senha", "x")), prod);
            Lanca<InvalidOperationException>(() => Opcoes.Carregar(Cfg(chave, ("Email:Host", "smtp.gmail.com")), prod), "Email:Usuario");
            Lanca<InvalidOperationException>(() => Opcoes.Carregar(Cfg(chave, ("Email:Host", "smtp.gmail.com"), ("Email:Usuario", "a"), ("Email:Senha", "x"), ("Email:Porta", "465")), prod), "465");
            return Task.CompletedTask;
        });

        await Caso("produção recusa chave e senha de banco de exemplo", () =>
        {
            Lanca<InvalidOperationException>(() => Opcoes.Carregar(Cfg(("Jwt:Key", "troque-por-um-texto-aleatorio-bem-longo-123456"), ("Email:Modo", "console")), prod), "Jwt:Key");
            Lanca<InvalidOperationException>(() => Opcoes.Carregar(Cfg(chave, ("Email:Modo", "console"), ("ConnectionStrings:HytechDb", "Server=x;User Id=sa;Password=troque-esta-senha")), prod), "ConnectionStrings");
            var o = Opcoes.Carregar(Cfg(chave, ("Email:Modo", "console")), prod);
            Verdade(!o.Email.Smtp && o.Producao, "config de produção inesperada");
            var g = Opcoes.Carregar(Cfg(chave, ("Email:Host", "smtp.gmail.com"), ("Email:Usuario", "a@gmail.com"), ("Email:Senha", "abcd")), prod);
            Verdade(g.Email.Smtp && g.Email.Porta == 587 && g.Email.De == "HyTech <a@gmail.com>", "config do Gmail inesperada");
            return Task.CompletedTask;
        });
    }

    static async Task Verificacao()
    {
        Grupo("Fase 7 — verificação por e-mail e desativar conta");
        await using var c = await Servidor.Subir();
        var dados = new { nome = "Nova Pessoa", nickname = "nova", email = "nova@x.com", senha = Servidor.Senha };

        await Caso("cadastro manda o código, mostra e-mail mascarado e NÃO cria a conta ainda", async () =>
        {
            var r = await c.Post("/auth/cadastro", dados);
            Status(202, r); Igual("n***@x.com", r.Json["email"].S);
            Igual(1, c.Email.Enviados.Count); Igual("nova@x.com", c.Email.Enviados[0].Para);
            Casa(@"\b\d{6}\b", c.Email.Enviados[0].Texto);
            Verdade(!c.Db.Usuarios.Any(u => u.Email == "nova@x.com"), "criou a conta antes do código");
        });

        await Caso("o código NÃO é guardado em texto e a senha vai como hash", () =>
        {
            var cod = c.Email.UltimoCodigo("nova@x.com");
            var linha = c.Db.Codigos.First(x => x.Chave == "e:nova@x.com");
            Verdade(linha.CodigoHash != cod, "código guardado em texto");
            Verdade(!linha.Dados!.Contains(Servidor.Senha), "senha em texto nos dados");
            Verdade(BCrypt.Net.BCrypt.Verify(Servidor.Senha, JsonDocument.Parse(linha.Dados).RootElement.GetProperty("senha_hash").GetString()!), "senha_hash inválido");
            return Task.CompletedTask;
        });

        await Caso("código errado, e depois de 5 erros nem o certo vale mais", async () =>
        {
            var certo = c.Email.UltimoCodigo("nova@x.com");
            var errado = certo == "000000" ? "111111" : "000000";
            for (var i = 0; i < 5; i++)
            {
                var r = await c.Post("/auth/cadastro/confirmar", new { email = "nova@x.com", codigo = errado });
                Status(400, r); Casa("incorreto", r.Json["erro"].S);
            }
            var ok = await c.Post("/auth/cadastro/confirmar", new { email = "nova@x.com", codigo = certo });
            Status(400, ok); Casa("inválido ou expirado", ok.Json["erro"].S);
        });

        await Caso("reenviar (depois de esperar) gera código novo e o antigo deixa de valer", async () =>
        {
            var antigo = c.Email.UltimoCodigo("nova@x.com");
            var cedo = await c.Post("/auth/cadastro/reenviar", new { email = "nova@x.com" });
            Status(400, cedo); Casa("Aguarde", cedo.Json["erro"].S);          // limite de 30 s
            c.Db.AvancarTempo(31);
            Status(200, await c.Post("/auth/cadastro/reenviar", new { email = "nova@x.com" }));
            var novo = c.Email.UltimoCodigo("nova@x.com");
            if (novo != antigo) Status(400, await c.Post("/auth/cadastro/confirmar", new { email = "nova@x.com", codigo = antigo }));
            Status(201, await c.Post("/auth/cadastro/confirmar", new { email = "nova@x.com", codigo = novo }));
            Verdade(await c.Login("nova@x.com") is { Length: > 0 }, "não conseguiu entrar");
        });

        await Caso("código usado não funciona de novo; formato inválido => 400", async () =>
        {
            Status(400, await c.Post("/auth/cadastro/confirmar", new { email = "nova@x.com", codigo = c.Email.UltimoCodigo("nova@x.com") }));
            Status(400, await c.Post("/auth/cadastro/confirmar", new { email = "nova@x.com", codigo = "abc" }));
        });

        await Caso("código expira, mas dá para reenviar sem preencher tudo de novo", async () =>
        {
            await c.Post("/auth/cadastro", new { dados.nome, nickname = "exp", email = "exp@x.com", dados.senha });
            var cod = c.Email.UltimoCodigo("exp@x.com");
            c.Db.AvancarTempo(11 * 60);
            var r = await c.Post("/auth/cadastro/confirmar", new { email = "exp@x.com", codigo = cod });
            Status(400, r); Casa("expirado", r.Json["erro"].S);
            Status(200, await c.Post("/auth/cadastro/reenviar", new { email = "exp@x.com" }));
        });

        await Caso("e-mail ou usuário já existentes são recusados ANTES de mandar e-mail", async () =>
        {
            var antes = c.Email.Enviados.Count;
            Igual("Este e-mail já está cadastrado.", (await c.Post("/auth/cadastro", new { dados.nome, dados.nickname, email = "aluno@hytech.com", dados.senha })).Json["erro"].S);
            Igual("Este nome de usuário já está em uso.", (await c.Post("/auth/cadastro", new { dados.nome, nickname = "@PedroBala", email = "outro@x.com", dados.senha })).Json["erro"].S);
            Igual(antes, c.Email.Enviados.Count);
            Status(400, await c.Post("/auth/cadastro/reenviar", new { email = "ninguem@x.com" }));
        });

        await Caso("esqueci a senha: resposta igual para existente e inexistente; só o existente recebe e-mail", async () =>
        {
            await using var e = await Servidor.Subir();
            var a = await e.Post("/auth/esqueci", new { email = "aluno@hytech.com" });
            var b = await e.Post("/auth/esqueci", new { email = "nao@existe.com" });
            Status(200, a); Igual(a.Json.ToString(), b.Json.ToString());
            Igual(1, e.Email.Enviados.Count); Igual("aluno@hytech.com", e.Email.Enviados[0].Para);
            Status(400, await e.Post("/auth/esqueci", new { email = "ruim" }));
            var n = e.Email.Enviados.Count;
            await e.Post("/auth/esqueci", new { email = "inativo@hytech.com" }); await e.Post("/auth/esqueci", new { email = "admin@hytech.com" });
            Igual(n, e.Email.Enviados.Count, "conta inativa/admin não deveriam receber código");

            var cod = e.Email.UltimoCodigo("aluno@hytech.com");
            Task<Resp> Red(object corpo, string email = "aluno@hytech.com") => e.Post("/auth/esqueci/redefinir", corpo);
            Status(400, await Red(new { email = "aluno@hytech.com", codigo = cod, novaSenha = "123" }));
            Status(400, await Red(new { email = "aluno@hytech.com", codigo = cod == "123456" ? "654321" : "123456", novaSenha = "novasenha99" }));
            Status(200, await Red(new { email = "aluno@hytech.com", codigo = cod, novaSenha = "novasenha99" }));
            Status(401, await e.Post("/auth/login", new { email = "aluno@hytech.com", senha = Servidor.Senha }));
            Status(200, await e.Post("/auth/login", new { email = "aluno@hytech.com", senha = "novasenha99" }));
            Status(400, await Red(new { email = "aluno@hytech.com", codigo = cod, novaSenha = "outra12345" }));   // uso único

            // código de OUTRO e-mail/finalidade não serve
            await e.Post("/auth/esqueci", new { email = "professor@hytech.com" });
            var codProf = e.Email.UltimoCodigo("professor@hytech.com");
            Status(400, await Red(new { email = "aluno@hytech.com", codigo = codProf, novaSenha = "hackeado123" }));
            Status(400, await e.Post("/auth/cadastro/confirmar", new { email = "professor@hytech.com", codigo = codProf }));
            Status(200, await Red(new { email = "professor@hytech.com", codigo = codProf, novaSenha = "profnova123" }));
        });

        await Caso("trocar e-mail: código vai para o NOVO endereço; token novo; reenviar", async () =>
        {
            await using var e = await Servidor.Subir();
            var tk = await e.Login("aluno@hytech.com");
            Status(401, await e.Post("/conta/email", new { novoEmail = "a@b.com", senha = Servidor.Senha }));
            Task<Resp> Pedir(object corpo) => e.Post("/conta/email", corpo, tk);
            Status(400, await Pedir(new { novoEmail = "ruim", senha = Servidor.Senha }));
            Status(400, await Pedir(new { novoEmail = "aluno@hytech.com", senha = Servidor.Senha }));
            Igual("Este e-mail já está em uso.", (await Pedir(new { novoEmail = "professor@hytech.com", senha = Servidor.Senha })).Json["erro"].S);
            Igual("Senha incorreta.", (await Pedir(new { novoEmail = "novo@x.com", senha = "errada123" })).Json["erro"].S);
            Igual(0, e.Email.Enviados.Count);

            var r = await Pedir(new { novoEmail = "Novo@X.com", senha = Servidor.Senha });
            Status(200, r); Igual("n***@x.com", r.Json["email"].S); Igual("novo@x.com", e.Email.Enviados.Last().Para);
            Igual("aluno@hytech.com", e.Db.Usuarios.First(u => u.IdUsuario == 3).Email);
            e.Db.AvancarTempo(31);
            Status(200, await e.Post("/conta/reenviar", new { finalidade = "email" }, tk));
            var cod = e.Email.UltimoCodigo("novo@x.com");
            Status(400, await e.Post("/conta/email/confirmar", new { codigo = cod == "000000" ? "111111" : "000000" }, tk));
            var ok = await e.Post("/conta/email/confirmar", new { codigo = cod }, tk);
            Status(200, ok); Igual("novo@x.com", ok.Json["perfil"]["email"].S);
            Igual("novo@x.com", (await e.Get("/auth/me", ok.Json["token"].S)).Json["email"].S);

            var tp = await e.Login("professor@hytech.com");
            Status(400, await e.Post("/conta/reenviar", new { finalidade = "email" }, tp));
            Status(400, await e.Post("/conta/reenviar", new { finalidade = "xxx" }, tp));
            Status(400, await e.Post("/conta/email/confirmar", new { codigo = "123456" }, tp));
        });

        await Caso("desativar conta (LGPD: dados ficam, status vira inativo)", async () =>
        {
            await using var e = await Servidor.Subir();
            Status(401, await e.Post("/conta/desativar", new { }));
            var r = await e.Post("/conta/desativar", new { nickname = "profsilva", senha = Servidor.Senha }, await e.Login("professor@hytech.com"));
            Status(403, r); Igual("ativo", e.Db.Usuarios.First(u => u.Email == "professor@hytech.com").Status);

            var tk = await e.Login("aluno@hytech.com");
            Casa("não confere", (await e.Post("/conta/desativar", new { nickname = "outro", senha = Servidor.Senha }, tk)).Json["erro"].S);
            Igual("Senha incorreta.", (await e.Post("/conta/desativar", new { nickname = "PedroBala", senha = "errada123" }, tk)).Json["erro"].S);
            Igual("ativo", e.Db.Usuarios.First(u => u.Email == "aluno@hytech.com").Status);

            var total = e.Db.Usuarios.Count;
            Status(200, await e.Post("/conta/desativar", new { nickname = "@pedrobala", senha = Servidor.Senha }, tk));
            Igual(total, e.Db.Usuarios.Count, "nada pode ser apagado");
            Igual("inativo", e.Db.Usuarios.First(u => u.Email == "aluno@hytech.com").Status);
            var l = await e.Post("/auth/login", new { email = "aluno@hytech.com", senha = Servidor.Senha });
            Status(403, l); Casa("desativada", l.Json["erro"].S);
            var n = e.Email.Enviados.Count;
            await e.Post("/auth/esqueci", new { email = "aluno@hytech.com" });
            Igual(n, e.Email.Enviados.Count);
        });

        await Caso("falha ao enviar e-mail => 503 com mensagem amigável (sem detalhes internos)", async () =>
        {
            await using var e = await Servidor.Subir(email: new EmailQuebrado());
            var r = await e.Post("/auth/cadastro", new { nome = "A B", nickname = "ab1", email = "ab1@x.com", senha = Servidor.Senha });
            Status(503, r); Verdade(!r.Json.ToString().Contains("segredo"), "vazou o erro do SMTP");
        });
    }

    static async Task Robustez()
    {
        Grupo("Robustez — campos com tipo errado (objeto/lista/número) dão 4xx, nunca 500");
        await using var c = await Servidor.Subir();
        var tk = await c.Login("aluno@hytech.com"); var tp = await c.Login("professor@hytech.com");
        var lixo = new[] { "{\"a\":1}", "[1,2]", "123", "true" };
        var rotas = new (string Metodo, string Rota, string? Token, string[] Campos)[]
        {
            ("POST", "/auth/login", null, new[] { "email", "senha" }),
            ("POST", "/auth/cadastro", null, new[] { "nome", "nickname", "email", "senha" }),
            ("POST", "/auth/cadastro/confirmar", null, new[] { "email", "codigo" }),
            ("POST", "/auth/esqueci", null, new[] { "email" }),
            ("POST", "/auth/esqueci/redefinir", null, new[] { "email", "codigo", "novaSenha" }),
            ("PUT", "/conta/perfil", tk, new[] { "nome", "nickname", "email", "foto" }),
            ("POST", "/conta/senha", tk, new[] { "senhaAtual", "novaSenha", "confirmarSenha" }),
            ("POST", "/conta/email", tk, new[] { "novoEmail", "senha" }),
            ("POST", "/conta/desativar", tk, new[] { "nickname", "senha" }),
            ("POST", "/tickets", tk, new[] { "assunto", "mensagem", "idMateria" }),
            ("POST", "/professor/aulas", tp, new[] { "titulo", "texto", "idMateria" }),
            ("POST", "/professor/questoes", tp, new[] { "enunciado", "dificuldade", "idMateria", "alternativas", "indiceCorreta" }),
        };
        await Caso("nenhuma rota devolve 5xx para tipos errados", async () =>
        {
            foreach (var (metodo, rota, token, campos) in rotas)
                foreach (var v in lixo)
                {
                    var corpo = "{" + string.Join(",", campos.Select(k => $"\"{k}\":{v}")) + "}";
                    var r = await c.Req(rota, metodo, corpo, token);
                    Verdade(r.Status is >= 400 and < 500, $"{metodo} {rota} com {v} deveria dar 4xx e deu {r.Status}");
                }
            Verdade(!c.Db.Usuarios.Any(u => u.Nome.Contains("[object") || u.Nome.Contains("{")), "gravou lixo como nome");
        });
    }
}
