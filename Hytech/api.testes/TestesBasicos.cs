using System.Text.Json;
using static HytechApi.Testes.T;

namespace HytechApi.Testes;

public static class TestesBasicos
{
    public static async Task Todos()
    {
        await Fase1();
        await Fase2();
        await LimiteAuth();
        await Admin();
    }

    static async Task Fase1()
    {
        Grupo("Fase 1 — login, cadastro, config");
        await using var s = await Servidor.Subir();

        await Caso("login ok devolve token e usuário (aluno)", async () =>
        {
            var r = await s.Post("/auth/login", new { email = "ALUNO@hytech.com ", senha = Servidor.Senha });
            Status(200, r); Verdade(r.Json["token"].S is { Length: > 0 }, "sem token");
            Igual("aluno", r.Json["usuario"]["papel"].S); Igual(1, r.Json["usuario"]["id_aluno"].N);
            Verdade(s.Db.Chamadas.Any(c => c.Proc == "sp_RegistrarLogin"), "não registrou o login");
        });

        await Caso("senha errada e e-mail inexistente dão a MESMA resposta 401", async () =>
        {
            var a = await s.Post("/auth/login", new { email = "aluno@hytech.com", senha = "errada123" });
            var b = await s.Post("/auth/login", new { email = "nao@existe.com", senha = "errada123" });
            Status(401, a); Status(401, b); Igual(a.Json.ToString(), b.Json.ToString());
        });

        await Caso("conta inativa => 403 (só depois de acertar a senha)", async () =>
        {
            Status(403, await s.Post("/auth/login", new { email = "inativo@hytech.com", senha = Servidor.Senha }));
            Status(401, await s.Post("/auth/login", new { email = "inativo@hytech.com", senha = "errada123" }));
        });

        await Caso("admin NÃO entra pelo site (/auth/login)", async () =>
        {
            var r = await s.Post("/auth/login", new { email = "admin@hytech.com", senha = Servidor.Senha });
            Status(403, r); Casa("HytechAdmin", r.Json["erro"].S);
        });

        await Caso("cadastro valida campos; só cria a conta depois do código", async () =>
        {
            var b = new { nome = "Maria Souza", nickname = "maria", email = "maria@x.com", senha = Servidor.Senha };
            Status(400, await s.Post("/auth/cadastro", new { b.nome, b.nickname, b.email, senha = "123" }));
            Status(400, await s.Post("/auth/cadastro", new { b.nome, b.nickname, email = "ruim", b.senha }));
            Status(400, await s.Post("/auth/cadastro", new { b.nome, b.nickname, b.email, b.senha, dataNascimento = "2999-01-01" }));
            Status(202, await s.Post("/auth/cadastro", new { b.nome, b.nickname, b.email, b.senha, dataNascimento = "2008-05-20" }));
            Verdade(!s.Db.Chamadas.Any(c => c.Proc == "sp_CadastrarAluno"), "criou a conta antes do código");
            Status(401, await s.Post("/auth/login", new { email = "maria@x.com", senha = Servidor.Senha }));
            Status(201, await s.Post("/auth/cadastro/confirmar", new { email = "maria@x.com", codigo = s.Email.UltimoCodigo("maria@x.com") }));
            var proc = s.Db.Chamadas.Last(c => c.Proc == "sp_CadastrarAluno").P;
            Verdade(BCrypt.Net.BCrypt.Verify(Servidor.Senha, (string)proc["senha_hash"]!), "senha não foi como hash");
            Verdade(!proc.ContainsKey("senha"), "a senha em texto foi para o banco");
            Verdade(proc["data_nascimento"] is DateOnly, "data de nascimento não chegou como data");
        });

        await Caso("regra do banco (RAISERROR) vira 400 com a mensagem", async () =>
        {
            var r = await s.Post("/auth/cadastro", new { nome = "A B", nickname = "ab", email = "aluno@hytech.com", senha = Servidor.Senha });
            Status(400, r); Igual("Este e-mail já está cadastrado.", r.Json["erro"].S);
        });

        await Caso("rotas protegidas exigem token; /auth/me e /config/chaves funcionam", async () =>
        {
            var r0 = await s.Get("/materias"); Status(401, r0); Igual("Faça login para continuar.", r0.Json["erro"].S);
            var r1 = await s.Get("/materias", "lixo"); Status(401, r1); Casa("Sessão expirada", r1.Json["erro"].S);
            var token = await s.Login("professor@hytech.com");
            var me = await s.Get("/auth/me", token);
            Igual("professor", me.Json["papel"].S); Igual(1, me.Json["id_professor"].N);
            var c = (await s.Get("/config/chaves", token)).Json;
            Igual(7, c["questaoFacil"].N); Igual(10, c["questaoMedia"].N); Igual(15, c["questaoDificil"].N); Igual(50, c["certificado"].N);
        });

        await Caso("rota inexistente => 404 em JSON; /saude é pública", async () =>
        {
            var r = await s.Get("/nada"); Status(404, r); Igual("Rota não encontrada.", r.Json["erro"].S);
            Status(200, await s.Get("/saude"));
        });
    }

    static async Task Fase2()
    {
        Grupo("Fase 2 — conteúdo e resposta de questões");
        await using var s = await Servidor.Subir();
        var token = await s.Login("aluno@hytech.com");

        await Caso("/materias traz slug, ordem e custo de desbloqueio (0/19/38/57)", async () =>
        {
            var r = await s.Get("/materias", token);
            Igual("programacao,design,banco-de-dados,redes", string.Join(",", r.Json.Itens.Select(m => m["slug"].S)));
            Igual("0,19,38,57", string.Join(",", r.Json.Itens.Select(m => m["chavesParaDesbloquear"].N)));
        });

        await Caso("/materias/{id}/aulas devolve 4 tópicos (o último é extra) com dica e exemplo", async () =>
        {
            var r = await s.Get("/materias/1/aulas", token);
            Status(200, r); Igual(4, r.Json.Len);
            Igual("False,False,False,True", string.Join(",", r.Json.Itens.Select(a => a["isExtra"].B)));
            Igual("Tópico 1", r.Json[0]["topico"].S); Igual("Variáveis em Python", r.Json[0]["titulo"].S);
            Igual("3,3,3,5", string.Join(",", r.Json.Itens.Select(a => a["chaves"].N)));
            Verdade(r.Json[0]["dica"]["titulo"].S is { Length: > 0 } && r.Json[0]["exemplo"].S is { Length: > 0 } && r.Json[0]["conteudo"].S!.Contains("<p>"), "faltou dica/exemplo/conteúdo");
        });

        await Caso("/materias/{id}/questoes tem 3 questões × 5 alternativas e NUNCA o gabarito", async () =>
        {
            var r = await s.Get("/materias/1/questoes", token);
            Igual(3, r.Json.Len);
            foreach (var q in r.Json.Itens) { Igual(5, q["alternativas"].Len); foreach (var a in q["alternativas"].Itens) Verdade(a["id"].Existe && a["texto"].Existe && !a["correta"].Existe, "alternativa mal formada"); }
            Verdade(!r.Json.ToString().ToLowerInvariant().Contains("correta"), "o JSON não pode mencionar \"correta\"");
        });

        await Caso("id de matéria inválido => 400", async () =>
        {
            Status(400, await s.Get("/materias/abc/aulas", token));
            Status(400, await s.Get("/materias/-1/questoes", token));
        });

        await Caso("responder: erra, acerta (ganha chaves do banco), repete (não ganha de novo)", async () =>
        {
            var q = (await s.Get("/materias/1/questoes", token)).Json[0];
            var qid = q["id"].N;
            var certa = s.Db.Alternativas.First(a => a.IdQuestao == qid && a.Correta).Id;
            var errada = q["alternativas"].Itens.First(a => a["id"].N != certa)["id"].N;
            var e = await s.Post($"/aluno/questoes/{qid}/responder", new { idAlternativa = errada }, token);
            Status(200, e); Igual(false, e.Json["acertou"].B); Igual(0, e.Json["chavesGanhas"].N);
            var c = await s.Post($"/aluno/questoes/{qid}/responder", new { idAlternativa = certa }, token);
            Igual(true, c.Json["acertou"].B); Igual(7, c.Json["chavesGanhas"].N); Igual(7, c.Json["saldoChaves"].N);
            var again = await s.Post($"/aluno/questoes/{qid}/responder", new { idAlternativa = certa }, token);
            Igual(true, again.Json["jaConcluida"].B); Igual(0, again.Json["chavesGanhas"].N);
        });

        await Caso("o id do aluno vem do TOKEN, não do corpo", async () =>
        {
            var q = (await s.Get("/materias/2/questoes", token)).Json[0];
            await s.Post($"/aluno/questoes/{q["id"].N}/responder", new { idAlternativa = q["alternativas"][0]["id"].N, id_aluno = 999, idAluno = 999 }, token);
            Igual(1, Convert.ToInt32(s.Db.Chamadas.Last(c => c.Proc == "sp_ResponderQuestao").P["id_aluno"]));
        });

        await Caso("responder: validações e permissão", async () =>
        {
            Status(400, await s.Post("/aluno/questoes/1/responder", new { }, token));
            Status(401, await s.Post("/aluno/questoes/1/responder", new { idAlternativa = 1 }));
            Status(403, await s.Post("/aluno/questoes/1/responder", new { idAlternativa = 1 }, await s.Login("professor@hytech.com")));
            var r = await s.Post("/aluno/questoes/1/responder", new { idAlternativa = 60 }, token);
            Status(400, r); Casa("Alternativa não pertence", r.Json["erro"].S);
        });
    }

    static async Task LimiteAuth()
    {
        Grupo("Limite de tentativas");
        await using var s = await Servidor.Subir(new() { ["Plataforma:LimiteAuthPorMinuto"] = "3" });
        await Caso("429 depois de estourar o limite de login", async () =>
        {
            var codigos = new List<int>();
            for (var i = 0; i < 5; i++) codigos.Add((await s.Post("/auth/login", new { email = "x@x.com", senha = "errada123" })).Status);
            Igual("401,401,401,429,429", string.Join(",", codigos));
        });
    }

    static async Task Admin()
    {
        Grupo("Admin (app desktop)");
        await using var s = await Servidor.Subir();
        await Caso("/auth/login-admin: admin entra; aluno recebe a mesma resposta de senha errada", async () =>
        {
            var ok = await s.Post("/auth/login-admin", new { email = "admin@hytech.com", senha = Servidor.Senha });
            Status(200, ok); Igual("admin", ok.Json["usuario"]["papel"].S);
            var aluno = await s.Post("/auth/login-admin", new { email = "aluno@hytech.com", senha = Servidor.Senha });
            Status(401, aluno);
            Verdade(!s.Db.Chamadas.Any(c => c.Proc == "sp_RegistrarLogin" && Convert.ToInt32(c.P["id_usuario"]) == 3), "aluno gerou log/streak pelo login-admin");
        });
        await Caso("/admin/* só com token de admin", async () =>
        {
            var adm = (await s.Post("/auth/login-admin", new { email = "admin@hytech.com", senha = Servidor.Senha })).Json["token"].S;
            var aluno = await s.Login("aluno@hytech.com");
            Status(401, await s.Get("/admin/metricas"));
            Status(403, await s.Get("/admin/metricas", aluno));
            var m = await s.Get("/admin/metricas", adm);
            Status(200, m); Verdade(m.Json["totalUsuarios"].N > 0, "métricas vazias");
            Status(200, await s.Get("/admin/materias", adm));
            // token de admin não vale nas rotas do aluno/professor
            Status(403, await s.Get("/aluno/estado", adm));
            Status(403, await s.Get("/tickets", adm));
        });
    }
}
