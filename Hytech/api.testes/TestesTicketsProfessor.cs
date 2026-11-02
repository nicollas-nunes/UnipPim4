using HytechApi.Services;
using static HytechApi.Testes.T;

namespace HytechApi.Testes;

public static class TestesTicketsProfessor
{
    public static async Task Todos()
    {
        await Tickets();
        await Professor();
    }

    static Task<Resp> Abrir(Servidor s, string? token, object? idMateria, object? extra = null, string assunto = "Dúvida", string mensagem = "Não entendi variáveis")
        => s.Post("/tickets", new Dictionary<string, object?> { ["idMateria"] = idMateria, ["assunto"] = assunto, ["mensagem"] = mensagem }
            .Concat(extra is null ? new Dictionary<string, object?>() : extra.GetType().GetProperties().ToDictionary(p => p.Name, p => p.GetValue(extra))).ToDictionary(k => k.Key, k => k.Value), token);

    static async Task Tickets()
    {
        Grupo("Fase 4 — tickets de suporte");
        await using var s = await Servidor.Subir();
        var aluno = await s.NovoAluno(1);
        var outro = await s.NovoAluno(2);
        var prof = await s.Login("professor@hytech.com");

        await Caso("Programação vai para o professor; Design cai no admin; solicitante vem do token", async () =>
        {
            var a = await Abrir(s, aluno, 1); Status(201, a);
            var det = (await s.Get("/tickets/" + a.Json["id"].N, aluno)).Json;
            Igual("professor", det["papelDestinatario"].S); Igual("Prof. Silva", det["professor"].S); Igual("Programação", det["materia"].S);
            Igual("aberto", det["status"].S); Igual(1, det["mensagens"].Len);
            Igual("Não entendi variáveis", det["mensagens"][0]["texto"].S); Igual("aluno", det["mensagens"][0]["remetente"].S);
            var b = await Abrir(s, aluno, 2);
            Igual("admin", (await s.Get("/tickets/" + b.Json["id"].N, aluno)).Json["papelDestinatario"].S);
            await Abrir(s, aluno, 1, new { id_usuario_solicitante = 999 });
            var ultima = s.Db.Chamadas.Last(c => c.Proc == "sp_AbrirTicket").P;
            Verdade(Convert.ToInt32(ultima["id_usuario_solicitante"]) != 999, "usou o id do corpo");
        });

        await Caso("validações, permissão e login", async () =>
        {
            Status(400, await Abrir(s, aluno, 1, assunto: "   "));
            Status(400, await Abrir(s, aluno, 1, assunto: new string('x', 151)));
            Status(400, await Abrir(s, aluno, 1, mensagem: ""));
            Status(400, await Abrir(s, aluno, 1, mensagem: new string('x', 4001)));
            Status(400, await Abrir(s, aluno, "abc"));
            Status(400, await Abrir(s, aluno, null));
            Status(403, await Abrir(s, prof, 1));
            Status(401, await Abrir(s, null, 1));
        });

        var t1 = (await Abrir(s, aluno, 1, assunto: "Do aluno 1")).Json["id"].N;
        var t2 = (await Abrir(s, outro, 1, assunto: "Do aluno 2")).Json["id"].N;

        await Caso("aluno só vê os próprios; professor vê só os que recebeu", async () =>
        {
            var l1 = (await s.Get("/tickets", aluno)).Json;
            Verdade(l1.Itens.Any(x => x["assunto"].S == "Do aluno 1") && !l1.Itens.Any(x => x["assunto"].S == "Do aluno 2"), "vazou ticket de outro aluno");
            Igual(true, l1[0]["souSolicitante"].B);
            var lp = (await s.Get("/tickets", prof)).Json.Itens.Select(x => x["assunto"].S).ToList();
            Verdade(lp.Contains("Do aluno 1") && lp.Contains("Do aluno 2"), "professor deveria ver os dois");
            Verdade(!lp.Contains("Vai para o admin"), "o de Design é do admin");
        });

        await Caso("quem não participa não lê (400 do banco); id inválido => 400; sem token => 401", async () =>
        {
            var r = await s.Get("/tickets/" + t1, outro); Status(400, r); Casa("não participa", r.Json["erro"].S);
            Status(400, await s.Get("/tickets/abc", aluno));
            Status(401, await s.Get("/tickets/" + t2));
            Status(200, await s.Get("/tickets/" + t2, prof));
        });

        var id = (await Abrir(s, aluno, 1)).Json["id"].N;
        Task<Resp> Msg(string? token, string texto, object? i = null) => s.Post($"/tickets/{i ?? id}/mensagens", new { texto }, token);
        async Task<string?> Status_() => (await s.Get("/tickets/" + id, aluno)).Json["status"].S;

        await Caso("professor responde => respondido; aluno escreve de novo => aberto", async () =>
        {
            Status(201, await Msg(prof, "Veja a aula 1")); Igual("respondido", await Status_());
            Status(201, await Msg(aluno, "Obrigado, mas ainda tenho dúvida")); Igual("aberto", await Status_());
            var det = (await s.Get("/tickets/" + id, aluno)).Json;
            Igual("aluno,professor,aluno", string.Join(",", det["mensagens"].Itens.Select(m => m["remetente"].S)));
            Igual("True,False,True", string.Join(",", det["mensagens"].Itens.Select(m => m["minha"].B)));
            Igual("professor", det["mensagens"][1]["papel"].S);
        });

        await Caso("validações de mensagem e quem não participa", async () =>
        {
            Status(400, await Msg(aluno, "   ")); Status(400, await Msg(aluno, new string('x', 4001))); Status(400, await Msg(aluno, "oi", "abc"));
            var r = await Msg(outro, "intruso"); Status(400, r); Casa("não participa", r.Json["erro"].S);
            Status(401, await Msg(null, "oi"));
        });

        await Caso("só o professor destinatário encerra; depois, ninguém escreve", async () =>
        {
            Status(403, await s.Post($"/tickets/{id}/fechar", null, aluno));
            Status(200, await s.Post($"/tickets/{id}/fechar", null, prof)); Igual("fechado", await Status_());
            var r = await Msg(aluno, "ainda aí?"); Status(400, r); Casa("encerrado", r.Json["erro"].S);
            Status(400, await Msg(prof, "ok?"));
            Status(200, await s.Post($"/tickets/{id}/fechar", null, prof));   // idempotente
        });

        await Caso("outro professor sem relação não encerra o ticket alheio", async () =>
        {
            var id2 = (await Abrir(s, aluno, 1)).Json["id"].N;
            s.Db.Usuarios.Add(new() { IdUsuario = 77, Nome = "Outro Prof", Nickname = "@outro", Email = "outroprof@hytech.com", Papel = "professor", IdProfessor = 7, Especialidade = "Redes", SenhaHash = s.Db.Usuarios[0].SenhaHash });
            var tk = await s.Login("outroprof@hytech.com");
            var r = await s.Post($"/tickets/{id2}/fechar", null, tk); Status(400, r); Casa("sem permissão", r.Json["erro"].S);
            Status(400, await Msg(tk, "oi", id2));
        });

        await Caso("texto com HTML é guardado como texto (o escape é do site)", async () =>
        {
            var id3 = (await Abrir(s, aluno, 1, mensagem: "<script>alert(1)</script>")).Json["id"].N;
            Igual("<script>alert(1)</script>", (await s.Get("/tickets/" + id3, aluno)).Json["mensagens"][0]["texto"].S);
        });

        await Caso("limite de mensagens por hora => 429", async () =>
        {
            await using var s2 = await Servidor.Subir(new() { ["Plataforma:LimiteTicketsPorHora"] = "3" });
            var tk = await s2.NovoAluno(1);
            var cod = new List<int>();
            for (var i = 0; i < 5; i++) cod.Add((await Abrir(s2, tk, 1)).Status);
            Igual("201,201,201,429,429", string.Join(",", cod));
        });
    }

    static readonly object Aula = new { idMateria = 1, titulo = "Minha aula", topico = "Extra", texto = "Linha 1\nLinha 2\n\nParágrafo <b>2</b> & fim", codigo = "print(1)", dicaTitulo = "Dica", dica = "Use com cuidado" };
    static object AulaCom(string? titulo = null, bool enviar = false, int idMateria = 1)
        => new { idMateria, titulo = titulo ?? "Minha aula", topico = "Extra", texto = "Linha 1\nLinha 2\n\nParágrafo <b>2</b> & fim", codigo = "print(1)", dicaTitulo = "Dica", dica = "Use com cuidado", enviar };
    static object Questao(bool enviar = false, int indice = 1, string dif = "Fácil", string[]? alts = null)
        => new { idMateria = 1, enunciado = "Quanto é 1+1?", dificuldade = dif, alternativas = alts ?? new[] { "1", "2", "3", "" }, indiceCorreta = indice, enviar };
    static object Desafio(bool enviar = false) => new { idMateria = 1, titulo = "Desafio 1", enunciado = "Faça algo", dificuldade = "Médio", codigo = "# base", dica = "pense", enviar };

    static async Task Professor()
    {
        Grupo("Fase 5 — área do professor");

        await Caso("texto: HTML seguro e ida e volta", () =>
        {
            var texto = "Linha 1\nLinha 2\n\nParágrafo <b>2</b> & fim";
            var h = Texto.TextoParaHtml(texto);
            Verdade(!h.Contains("<b>") && h.Contains("&lt;b&gt;"), "não escapou o HTML");
            Igual(texto, Texto.HtmlParaTexto(h));
            return Task.CompletedTask;
        });

        await using var s = await Servidor.Subir();
        var tp = await s.Login("professor@hytech.com");
        var ta = await s.Login("aluno@hytech.com");

        await Caso("perfil e permissões", async () =>
        {
            var p = await s.Get("/professor/perfil", tp);
            Status(200, p); Igual("Programação", p.Json["especialidade"].S); Verdade(!p.Json["idMateriaEspecialidade"].Nulo, "sem matéria da especialidade");
            Status(403, await s.Get("/professor/perfil", ta)); Status(401, await s.Get("/professor/perfil"));
            Status(403, await s.Get("/aluno/desafios", tp));
        });

        await Caso("aula: rascunho -> pendente -> aprovada (extra, 0 chaves, com autor)", async () =>
        {
            var c = await s.Post("/professor/aulas", AulaCom(), tp); Status(201, c);
            var id = c.Json["id"].N;
            async Task<bool> Visivel() => (await s.Get("/materias/1/aulas", ta)).Json.Itens.Any(a => a["titulo"].S == "Minha aula");
            Verdade(!await Visivel(), "rascunho visível ao aluno");
            Status(200, await s.Post($"/professor/aulas/{id}/enviar", null, tp));
            Verdade(!await Visivel(), "pendente visível ao aluno");
            var m = (await s.Get("/professor/meus", tp)).Json["aulas"][0];
            Igual("pendente", m["status"].S); Igual("Linha 1\nLinha 2\n\nParágrafo <b>2</b> & fim", m["texto"].S);
            s.Db.Aprovar("aula", id);
            var a = (await s.Get("/materias/1/aulas", ta)).Json.Itens.FirstOrDefault(x => x["titulo"].S == "Minha aula");
            Verdade(a is not null, "aula aprovada deveria aparecer");
            Verdade(a!["conteudo"].S!.Contains("&lt;b&gt;"), "conteúdo não foi escapado");
            Igual("Prof. Silva", a["autor"].S); Igual(0, a["chaves"].N); Igual(true, a["isExtra"].B);
            Igual(1, (await s.Get("/professor/perfil", tp)).Json["resumo"]["aprovados"].N);
        });

        await Caso("questão: validações, envio, aprovação e sem gabarito para o aluno", async () =>
        {
            Status(400, await s.Post("/professor/questoes", Questao(dif: "X"), tp));
            Status(400, await s.Post("/professor/questoes", Questao(alts: new[] { "a", "b", "c", "d", "e", "f" }), tp));
            Status(400, await s.Post("/professor/questoes", Questao(enviar: true, indice: -1), tp));   // banco: marque a correta
            var c = await s.Post("/professor/questoes", Questao(enviar: true), tp); Status(201, c);
            var m = (await s.Get("/professor/meus", tp)).Json["questoes"].Itens.First(q => q["id"].N == c.Json["id"].N);
            Igual("pendente", m["status"].S); Igual(1, m["indiceCorreta"].N);
            s.Db.Aprovar("questao", c.Json["id"].N);
            var q = (await s.Get("/materias/1/questoes", ta)).Json.Itens.FirstOrDefault(x => x["pergunta"].S == "Quanto é 1+1?");
            Verdade(q is not null, "questão aprovada não apareceu");
            Verdade(!q!.ToString().Contains("correta"), "vazou o gabarito");
        });

        await Caso("dono: outro professor não mexe; aprovado não edita/exclui; rejeitado edita e reenvia", async () =>
        {
            s.Db.Usuarios.Add(new() { IdUsuario = 50, Nome = "Prof Dois", Nickname = "@p2", Email = "p2@hytech.com", Papel = "professor", IdProfessor = 2, Especialidade = "Programação", SenhaHash = s.Db.Usuarios[0].SenhaHash });
            var t2 = await s.Login("p2@hytech.com");
            var id = (await s.Post("/professor/aulas", AulaCom("Outra aula"), tp)).Json["id"].N;
            Status(400, await s.Req($"/professor/aulas/{id}", "PUT", AulaCom(), t2));
            Status(400, await s.Post($"/professor/aulas/{id}/enviar", null, t2));
            Status(400, await s.Req($"/professor/aulas/{id}", "DELETE", null, t2));
            Igual(0, (await s.Get("/professor/meus", t2)).Json["aulas"].Len);
            await s.Post($"/professor/aulas/{id}/enviar", null, tp);
            s.Db.Rejeitar("aula", id, "Faltou exemplo");
            var m = (await s.Get("/professor/meus", tp)).Json["aulas"].Itens.First(a => a["id"].N == id);
            Igual("rejeitado", m["status"].S); Igual("Faltou exemplo", m["motivo"].S);
            Status(200, await s.Req($"/professor/aulas/{id}", "PUT", AulaCom("Corrigida", enviar: true), tp));
            s.Db.Aprovar("aula", id);
            Status(400, await s.Req($"/professor/aulas/{id}", "PUT", AulaCom(), tp));
            Status(400, await s.Req($"/professor/aulas/{id}", "DELETE", null, tp));
        });

        await Caso("excluir rascunho; limites e especialidade", async () =>
        {
            var id = (await s.Post("/professor/desafios", Desafio(), tp)).Json["id"].N;
            Status(200, await s.Req($"/professor/desafios/{id}", "DELETE", null, tp));
            Igual(0, (await s.Get("/professor/meus", tp)).Json["desafios"].Len);
            Status(400, await s.Post("/professor/aulas", AulaCom(new string('x', 101)), tp));
            Status(403, await s.Post("/professor/aulas", AulaCom(idMateria: 2), tp));
            Status(400, await s.Req("/professor/aulas/abc", "PUT", AulaCom(), tp));
            Status(400, await s.Req("/professor/xyz/1", "DELETE", null, tp));
        });

        await Caso("desafio: professor cria, aprova, aluno conclui uma única vez", async () =>
        {
            var id = (await s.Post("/professor/desafios", Desafio(enviar: true), tp)).Json["id"].N;
            Igual(0, (await s.Get("/aluno/desafios", ta)).Json.Len);
            s.Db.Aprovar("desafio", id);
            var l = (await s.Get("/aluno/desafios", ta)).Json;
            Igual(1, l.Len); Igual(false, l[0]["concluido"].B); Igual("Prof. Silva", l[0]["autor"].S);
            var c1 = (await s.Post($"/aluno/desafios/{id}/concluir", null, ta)).Json;
            Igual(10, c1["chavesGanhas"].N); Igual(false, c1["jaConcluido"].B);
            var c2 = (await s.Post($"/aluno/desafios/{id}/concluir", null, ta)).Json;
            Igual(0, c2["chavesGanhas"].N); Igual(true, c2["jaConcluido"].B);
            Status(400, await s.Post("/aluno/desafios/9999/concluir", null, ta));
        });

        await Caso("desempenho dos alunos", async () =>
        {
            var d = await s.Get("/professor/desempenho", tp);
            Status(200, d); Verdade(d.Json.Len >= 1, "sem alunos");
            Verdade(d.Json[0]["nome"].Existe && d.Json[0]["fases"].Existe && d.Json[0]["certificados"].Existe, "campos faltando");
            Status(200, await s.Get("/professor/desempenho?materia=todas", tp));
            Status(403, await s.Get("/professor/desempenho", ta));
        });

        await Caso("limite de escrita do professor => 429", async () =>
        {
            await using var s2 = await Servidor.Subir(new() { ["Plataforma:LimiteConteudoPorHora"] = "2" });
            var tk = await s2.Login("professor@hytech.com");
            var r = new List<int>();
            for (var i = 0; i < 4; i++) r.Add((await s2.Post("/professor/desafios", Desafio(), tk)).Status);
            Verdade(r.Contains(429), string.Join(",", r));
        });
    }
}
