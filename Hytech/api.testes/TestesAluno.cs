using static HytechApi.Testes.T;

namespace HytechApi.Testes;

public static class TestesAluno
{
    public static async Task Todos()
    {
        await Estado();
        await Certificado();
        await Ranking();
        await Conta();
    }

    // Conclui tudo o que é preciso para o certificado da matéria (aulas não extras + todas as questões)
    static async Task<Resp?> FazerTudoDaMateria(Servidor s, string token, int idMateria)
    {
        foreach (var a in (await s.Get($"/materias/{idMateria}/aulas", token)).Json.Itens.Where(x => !x["isExtra"].B))
            await s.Post($"/aluno/aulas/{a["id"].N}/concluir", null, token);
        Resp? ultima = null;
        foreach (var q in (await s.Get($"/materias/{idMateria}/questoes", token)).Json.Itens)
        {
            var certa = s.Db.Alternativas.First(a => a.IdQuestao == q["id"].N && a.Correta);
            ultima = await s.Post($"/aluno/questoes/{q["id"].N}/responder", new { idAlternativa = certa.Id }, token);
        }
        return ultima;
    }

    static async Task Estado()
    {
        Grupo("Fase 3 — estado do aluno");
        await using var s = await Servidor.Subir();

        await Caso("aluno novo vem zerado; professor e anônimo não acessam", async () =>
        {
            var tk = await s.NovoAluno(1);
            var r = await s.Get("/aluno/estado", tk);
            Status(200, r);
            var res = r.Json["resumo"];
            Igual(0, res["pontos"].N); Igual(0, res["saldoChaves"].N); Igual(0, res["streak"].N); Igual(0, res["fasesConcluidas"].N); Igual(0, res["certificados"].N);
            Igual(0, r.Json["aulasConcluidas"].Len); Igual(4, r.Json["materias"].Len);
            Igual("True,False,False,False", string.Join(",", r.Json["materias"].Itens.Select(m => m["desbloqueada"].B)));
            Igual("@aluno1", r.Json["perfil"]["nickname"].S);
            Status(403, await s.Get("/aluno/estado", await s.Login("professor@hytech.com")));
            Status(401, await s.Get("/aluno/estado"));
        });

        await Caso("concluir aula: 1ª vez credita, 2ª não; estado reflete; outro aluno não vê", async () =>
        {
            var tk = await s.NovoAluno(2);
            var aulas = (await s.Get("/materias/1/aulas", tk)).Json;
            var id = aulas[0]["id"].N; var chaves = aulas[0]["chaves"].N;
            var r1 = await s.Post($"/aluno/aulas/{id}/concluir", null, tk);
            Status(200, r1); Igual(chaves, r1.Json["chavesGanhas"].N); Igual(chaves, r1.Json["saldoChaves"].N);
            var r2 = await s.Post($"/aluno/aulas/{id}/concluir", null, tk);
            Igual(0, r2.Json["chavesGanhas"].N); Igual(chaves, r2.Json["saldoChaves"].N);
            var e = (await s.Get("/aluno/estado", tk)).Json;
            Igual(id.ToString(), string.Join(",", e["aulasConcluidas"].Itens.Select(x => x.N)));
            Igual(1, e["resumo"]["fasesConcluidas"].N); Igual(1, e["resumo"]["streak"].N);
            var outro = (await s.Get("/aluno/estado", await s.NovoAluno(3))).Json;
            Igual(0, outro["aulasConcluidas"].Len); Igual(0, outro["resumo"]["pontos"].N);
        });

        await Caso("concluir: aula inexistente => 400 do banco; id inválido => 400; sem token => 401", async () =>
        {
            var tk = await s.NovoAluno(4);
            var r = await s.Post("/aluno/aulas/99999/concluir", null, tk);
            Status(400, r); Casa("inexistente", r.Json["erro"].S);
            Status(400, await s.Post("/aluno/aulas/abc/concluir", null, tk));
            Status(401, await s.Post("/aluno/aulas/1/concluir"));
        });
    }

    static async Task Certificado()
    {
        Grupo("Fase 3 — certificado e desbloqueio de matéria");
        await using var s = await Servidor.Subir();

        await Caso("desbloquear Design sem a anterior concluída => 400", async () =>
        {
            var r = await s.Post("/aluno/materias/2/desbloquear", null, await s.NovoAluno(10));
            Status(400, r); Casa("matéria anterior", r.Json["erro"].S);
        });

        await Caso("matéria 1 completa => certificado (+50); desbloquear Design cobra 19 e não cobra 2x", async () =>
        {
            var tk = await s.NovoAluno(11);
            var antes = (await s.Get("/aluno/estado", tk)).Json["resumo"]["pontos"].N;
            var ultima = await FazerTudoDaMateria(s, tk, 1);
            Igual(true, ultima!.Json["certificadoEmitido"].B);
            var e = (await s.Get("/aluno/estado", tk)).Json;
            Igual(1, e["certificados"].Len); Igual(1, e["certificados"][0]["idMateria"].N);
            Verdade(e["resumo"]["pontos"].N - antes - 50 > 0, "pontos = aulas + questões + 50 do certificado");
            var saldoAntes = e["resumo"]["saldoChaves"].N;
            var d = await s.Post("/aluno/materias/2/desbloquear", null, tk);
            Status(200, d); Igual(19, d.Json["chavesGastas"].N); Igual(saldoAntes - 19, d.Json["saldoChaves"].N);
            var d2 = await s.Post("/aluno/materias/2/desbloquear", null, tk);
            Igual(0, d2.Json["chavesGastas"].N); Igual(saldoAntes - 19, d2.Json["saldoChaves"].N);
            var e2 = (await s.Get("/aluno/estado", tk)).Json;
            Igual(true, e2["materias"].Itens.First(m => m["id"].N == 2)["desbloqueada"].B);
            Igual(e["resumo"]["pontos"].N, e2["resumo"]["pontos"].N, "gastar chaves NÃO diminui os pontos");
            Igual(saldoAntes - 19, e2["resumo"]["saldoChaves"].N);
        });

        await Caso("refazer tudo não paga o certificado de novo", async () =>
        {
            var tk = await s.NovoAluno(12);
            await FazerTudoDaMateria(s, tk, 1);
            var p1 = (await s.Get("/aluno/estado", tk)).Json["resumo"]["pontos"].N;
            await FazerTudoDaMateria(s, tk, 1);
            Igual(p1, (await s.Get("/aluno/estado", tk)).Json["resumo"]["pontos"].N);
        });

        await Caso("chaves insuficientes (custo maior que o saldo) => 400", async () =>
        {
            var tk = await s.NovoAluno(13);
            await FazerTudoDaMateria(s, tk, 1);
            await s.Post("/aluno/materias/2/desbloquear", null, tk);
            await FazerTudoDaMateria(s, tk, 2);
            var e = (await s.Get("/aluno/estado", tk)).Json;
            var idAluno = s.Db.Usuarios.First(u => u.Email == "a13@x.com").IdAluno!.Value;
            s.Db.EstadoDe(idAluno).Gastas = e["resumo"]["pontos"].N - 1;
            var r = await s.Post("/aluno/materias/3/desbloquear", null, tk);
            Status(400, r); Igual("Chaves insuficientes.", r.Json["erro"].S);
        });
    }

    static async Task Ranking()
    {
        Grupo("Fase 3 — ranking");
        await using var s = await Servidor.Subir();

        await Caso("ordena por pontos, marca souEu, respeita top e exige login", async () =>
        {
            var tk = await s.NovoAluno(20);
            var a = (await s.Get("/materias/1/aulas", tk)).Json;
            await s.Post($"/aluno/aulas/{a[0]["id"].N}/concluir", null, tk);
            var r = await s.Get("/ranking?top=3", tk);
            Status(200, r); Igual(3, r.Json.Len);
            Igual("1,2,3", string.Join(",", r.Json.Itens.Select(x => x["posicao"].N)));
            Verdade(r.Json[0]["pontos"].N >= r.Json[1]["pontos"].N, "fora de ordem");
            Verdade(!r.Json.Itens.Any(x => x["souEu"].B), "aluno novo não está no top 3");
            var eu = (await s.Get("/ranking?top=50", tk)).Json.Itens.FirstOrDefault(x => x["souEu"].B);
            Verdade(eu is not null, "não achou a si mesmo"); Igual("@aluno20", eu!["nickname"].S);
            Verdade((await s.Get("/ranking?top=999", tk)).Json.Len <= 50, "top deve ser limitado a 50");
            Status(401, await s.Get("/ranking"));
        });

        await Caso("empate divide a posição", async () =>
        {
            var t1 = await s.NovoAluno(21); var t2 = await s.NovoAluno(22);
            var a = (await s.Get("/materias/1/aulas", t1)).Json;
            foreach (var tk in new[] { t1, t2 }) await s.Post($"/aluno/aulas/{a[0]["id"].N}/concluir", null, tk);
            var todos = (await s.Get("/ranking?top=50", t1)).Json.Itens.ToList();
            Igual(todos.First(x => x["nickname"].S == "@aluno21")["posicao"].N, todos.First(x => x["nickname"].S == "@aluno22")["posicao"].N);
        });
    }

    static async Task Conta()
    {
        Grupo("Fase 3 — conta (perfil e senha)");
        await using var s = await Servidor.Subir();
        const string Foto = "data:image/jpeg;base64,/9j/4AAQSkZJRgABAQ==";

        await Caso("editar nome/nickname/foto; token novo; serve aluno e professor", async () =>
        {
            var tk = await s.NovoAluno(30);
            var r = await s.Req("/conta/perfil", "PUT", new { nome = "Novo Nome", nickname = "novonick", foto = Foto }, tk);
            Status(200, r); Igual("@novonick", r.Json["perfil"]["nickname"].S); Igual("a30@x.com", r.Json["perfil"]["email"].S);
            var me = await s.Get("/auth/me", r.Json["token"].S);
            Igual("a30@x.com", me.Json["email"].S); Igual("Novo Nome", me.Json["nome"].S);
            var est = (await s.Get("/aluno/estado", r.Json["token"].S)).Json;
            Igual(Foto, est["perfil"]["foto"].S); Igual("@novonick", est["perfil"]["nickname"].S);
            Status(200, await s.Req("/conta/perfil", "PUT", new { nome = "Prof Novo" }, await s.Login("professor@hytech.com")));
        });

        await Caso("duplicidade e validações viram 400", async () =>
        {
            var tk = await s.NovoAluno(31);
            var r = await s.Req("/conta/perfil", "PUT", new { email = "aluno@hytech.com" }, tk);
            Status(400, r); Casa("confirmar com um código", r.Json["erro"].S);
            Igual("Este nome de usuário já está em uso.", (await s.Req("/conta/perfil", "PUT", new { nickname = "@PedroBala" }, tk)).Json["erro"].S);
            Status(400, await s.Req("/conta/perfil", "PUT", new { email = "ruim" }, tk));
            Status(400, await s.Req("/conta/perfil", "PUT", new { }, tk));
            Status(400, await s.Req("/conta/perfil", "PUT", new { foto = "data:image/png;base64," + new string('A', 1_500_000) }, tk));
            Status(400, await s.Req("/conta/perfil", "PUT", new { foto = "javascript:alert(1)" }, tk));
            Status(401, await s.Req("/conta/perfil", "PUT", new { nome = "x" }));
        });

        await Caso("trocar senha: atual errada, nova curta, confirmação diferente, código; só a nova funciona", async () =>
        {
            var tk = await s.NovoAluno(32);
            Task<Resp> Pedir(object c) => s.Post("/conta/senha", c, tk);
            Igual("Senha atual incorreta.", (await Pedir(new { senhaAtual = "errada123", novaSenha = "outrasenha1" })).Json["erro"].S);
            Status(400, await Pedir(new { senhaAtual = Servidor.Senha, novaSenha = "123" }));
            Status(400, await Pedir(new { senhaAtual = Servidor.Senha, novaSenha = "outrasenha1", confirmarSenha = "diferente123" }));
            Status(400, await Pedir(new { senhaAtual = Servidor.Senha, novaSenha = Servidor.Senha }));
            var pedido = await Pedir(new { senhaAtual = Servidor.Senha, novaSenha = "outrasenha1", confirmarSenha = "outrasenha1" });
            Status(200, pedido); Igual(true, pedido.Json["codigoEnviado"].B);
            Status(200, await s.Post("/auth/login", new { email = "a32@x.com", senha = Servidor.Senha }));   // ainda NÃO trocou
            Status(200, await s.Post("/conta/senha/confirmar", new { codigo = s.Email.UltimoCodigo("a32@x.com") }, tk));
            Status(401, await s.Post("/auth/login", new { email = "a32@x.com", senha = Servidor.Senha }));
            Status(200, await s.Post("/auth/login", new { email = "a32@x.com", senha = "outrasenha1" }));
        });
    }
}
