using HytechApi.Data;
using HytechApi.Infra;
using HytechApi.Security;
using HytechApi.Services;
using static HytechApi.Data.Parametros;

namespace HytechApi.Endpoints;

/// <summary>
/// Área do professor: criar/editar/enviar/excluir aula, questão e desafio; perfil; desempenho dos alunos.
/// O id do professor vem SEMPRE do token. Quem aprova é o admin (HytechAdmin).
/// </summary>
public static class ProfessorEndpoints
{
    private static readonly string[] Dificuldades = { "Fácil", "Médio", "Difícil" };
    private static readonly Dictionary<string, string> Tipos = new() { ["aulas"] = "aula", ["questoes"] = "questao", ["desafios"] = "desafio" };

    public static void MapProfessor(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/professor").RequireAuthorization("professor");

        g.MapGet("/perfil", async (HttpContext ctx, IDb db) =>
        {
            var p = await PerfilAsync(db, UsuarioToken.De(ctx.User));
            return Results.Ok(new
            {
                nome = p.Txt("nome"), nickname = p.Txt("nickname"), email = p.Txt("email"),
                foto = p.TxtN("foto_url") is { Length: > 0 } f ? f : null,
                especialidade = p.Txt("especialidade"),
                idMateriaEspecialidade = p.IntN("id_materia_especialidade") is > 0 and var m ? m : (int?)null,
                resumo = new
                {
                    enviados = p.Int("enviados"), aprovados = p.Int("aprovados"),
                    aguardando = p.Int("pendentes"), alunosAtivos = p.Int("alunos_ativos")
                }
            });
        });

        g.MapGet("/meus", async (HttpContext ctx, IDb db) =>
        {
            var eu = UsuarioToken.De(ctx.User);
            var t = await db.ExecMultiAsync("sp_WebListarMeusConteudos", P("id_professor", eu.IdProfessor));
            List<Linha> T(int i) => i < t.Count ? t[i] : new List<Linha>();
            var alts = T(2);

            return Results.Ok(new
            {
                aulas = T(0).Select(a => new
                {
                    id = a.Int("id_aula"), idMateria = a.Int("id_materia"), materia = a.Txt("materia"),
                    topico = a.Txt("topico"), titulo = a.Txt("titulo"), texto = Texto.HtmlParaTexto(a.TxtN("conteudo")),
                    codigo = a.Txt("exemplo_codigo"), dicaTitulo = a.Txt("dica_titulo"), dica = a.Txt("dica_corpo"),
                    status = a.Txt("status"), data = a.DataN("data"), motivo = a.TxtN("motivo")
                }),
                questoes = T(1).Select(q =>
                {
                    var lista = alts.Where(x => x.Int("id_questao") == q.Int("id_questao")).ToList();
                    return new
                    {
                        id = q.Int("id_questao"), idMateria = q.Int("id_materia"), materia = q.Txt("materia"),
                        enunciado = q.Txt("enunciado"), dificuldade = q.Txt("dificuldade"), codigo = q.Txt("codigo_exemplo"),
                        status = q.Txt("status"), data = q.DataN("data"), motivo = q.TxtN("motivo"),
                        alternativas = lista.Select(x => x.Txt("texto")),
                        indiceCorreta = lista.FindIndex(x => x.Bool("correta"))
                    };
                }),
                desafios = T(3).Select(d => new
                {
                    id = d.Int("id_desafio"), idMateria = d.Int("id_materia"), materia = d.Txt("materia"),
                    titulo = d.Txt("titulo"), enunciado = d.Txt("enunciado"), dica = d.Txt("dica"),
                    dificuldade = d.Txt("dificuldade"), codigo = d.Txt("codigo_base"), status = d.Txt("status"),
                    data = d.DataN("data"), motivo = d.TxtN("motivo")
                })
            });
        });

        // ── Aulas ──
        g.MapPost("/aulas", (HttpRequest req, HttpContext ctx, IDb db) => SalvarAula(req, ctx, db, null)).RequireRateLimiting("escrita-professor");
        g.MapPut("/aulas/{id}", (string id, HttpRequest req, HttpContext ctx, IDb db) =>
            Rotas.IdInt(id) is int n ? SalvarAula(req, ctx, db, n) : Task.FromResult(Erro("Aula inválida.")))
            .RequireRateLimiting("escrita-professor");

        // ── Questões ──
        g.MapPost("/questoes", (HttpRequest req, HttpContext ctx, IDb db) => SalvarQuestao(req, ctx, db, null)).RequireRateLimiting("escrita-professor");
        g.MapPut("/questoes/{id}", (string id, HttpRequest req, HttpContext ctx, IDb db) =>
            Rotas.IdInt(id) is int n ? SalvarQuestao(req, ctx, db, n) : Task.FromResult(Erro("Questão inválida.")))
            .RequireRateLimiting("escrita-professor");

        // ── Desafios ──
        g.MapPost("/desafios", (HttpRequest req, HttpContext ctx, IDb db) => SalvarDesafio(req, ctx, db, null)).RequireRateLimiting("escrita-professor");
        g.MapPut("/desafios/{id}", (string id, HttpRequest req, HttpContext ctx, IDb db) =>
            Rotas.IdInt(id) is int n ? SalvarDesafio(req, ctx, db, n) : Task.FromResult(Erro("Desafio inválido.")))
            .RequireRateLimiting("escrita-professor");

        // ── Enviar para aprovação / excluir (aulas | questoes | desafios) ──
        g.MapPost("/{tipo}/{id}/enviar", async (string tipo, string id, HttpContext ctx, IDb db) =>
        {
            if (!Tipos.TryGetValue(tipo, out var t) || Rotas.IdInt(id) is not int n) return Erro("Item inválido.");
            await db.ExecutarAsync("sp_EnviarParaAprovacao", P("tipo", t), P("id", n), P("id_professor", UsuarioToken.De(ctx.User).IdProfessor));
            return Results.Ok(new { ok = true });
        }).RequireRateLimiting("escrita-professor");

        g.MapDelete("/{tipo}/{id}", async (string tipo, string id, HttpContext ctx, IDb db) =>
        {
            if (!Tipos.TryGetValue(tipo, out var t) || Rotas.IdInt(id) is not int n) return Erro("Item inválido.");
            await db.ExecutarAsync("sp_ExcluirConteudoProfessor", P("tipo", t), P("id", n), P("id_professor", UsuarioToken.De(ctx.User).IdProfessor));
            return Results.Ok(new { ok = true });
        }).RequireRateLimiting("escrita-professor");

        // ── Desempenho dos alunos (padrão: matéria da especialidade; ?materia=todas ou ?materia=<id>) ──
        g.MapGet("/desempenho", async (HttpContext ctx, IDb db) =>
        {
            var p = await PerfilAsync(db, UsuarioToken.De(ctx.User));
            string? q = ctx.Request.Query["materia"];
            int? idMateria = q == "todas" ? null : Rotas.IdInt(q) ?? (p.IntN("id_materia_especialidade") is > 0 and var m ? m : null);
            return Results.Ok(await db.ConsultarAsync("sp_DesempenhoAlunos", a => new
            {
                nome = a.Txt("nome"), email = a.Txt("email"), chaves = a.Int("chaves"), fases = a.Int("fases"),
                acerto = a.Int("acerto"), certificados = a.Int("certs"), ultimaAtividade = a.DataN("ultima")
            }, P("id_materia", idMateria)));
        });
    }

    private static async Task<Linha> PerfilAsync(IDb db, UsuarioToken eu)
        => await db.PrimeiroAsync("sp_WebObterPerfilProfessor", P("id_usuario", eu.Id))
           ?? throw new ErroHttp(404, "Professor não encontrado.");

    // O professor só cria conteúdo da própria especialidade. Devolve a mensagem de erro, ou null se está tudo certo.
    private static async Task<string?> ChecarMateriaAsync(IDb db, UsuarioToken eu, int idMateria)
    {
        var p = await PerfilAsync(db, eu);
        var esp = p.IntN("id_materia_especialidade");
        if (esp is null or 0) return "Peça ao administrador para definir sua especialidade.";
        if (idMateria != esp) return "Você só pode criar conteúdo da sua especialidade.";
        return null;
    }

    private static IResult Erro(string msg) => Results.Json(new { erro = msg }, statusCode: 400);
    private static IResult Proibido(string msg) => Results.Json(new { erro = msg }, statusCode: 403);
    private static string? NuloSeVazio(string s) => s.Length == 0 ? null : s;

    private static async Task<IResult> SalvarAula(HttpRequest req, HttpContext ctx, IDb db, int? idAula)
    {
        var eu = UsuarioToken.De(ctx.User);
        var b = await Corpo.LerAsync(req);

        var titulo = b.Str("titulo").Trim();
        if (titulo.Length is 0 or > 100) return Erro("Informe o título (até 100 caracteres).");
        var topico = b.Str("topico").Trim();
        if (topico.Length > 150) return Erro("Tópico muito longo (até 150 caracteres).");
        var texto = b.Str("texto");
        if (texto.Length > 20000) return Erro("Texto muito longo (até 20.000 caracteres).");
        if (b.IdInt("idMateria") is not int idMateria) return Erro("Escolha a matéria.");

        if (await ChecarMateriaAsync(db, eu, idMateria) is { } negado) return Proibido(negado);

        var x = await db.UnicoAsync("sp_WebSalvarAula",
            P("id_aula", idAula), P("id_professor", eu.IdProfessor), P("id_materia", idMateria), P("titulo", titulo),
            P("topico", NuloSeVazio(topico)), P("conteudo", Texto.TextoParaHtml(texto)),
            P("exemplo_codigo", NuloSeVazio(Texto.Aparar(b.Str("codigo"), 5000))),
            P("dica_titulo", NuloSeVazio(Texto.Aparar(b.Str("dicaTitulo"), 150))),
            P("dica_corpo", NuloSeVazio(Texto.Aparar(b.Str("dica"), 1000))),
            P("enviar", b.Verdadeiro("enviar") ? 1 : 0));
        return Results.Json(new { id = x.Int("id_aula") }, statusCode: idAula is null ? 201 : 200);
    }

    private static async Task<IResult> SalvarQuestao(HttpRequest req, HttpContext ctx, IDb db, int? idQuestao)
    {
        var eu = UsuarioToken.De(ctx.User);
        var b = await Corpo.LerAsync(req);

        var enunciado = b.Str("enunciado").Trim();
        if (enunciado.Length is 0 or > 2000) return Erro("Informe o enunciado (até 2000 caracteres).");
        var dificuldade = b.Str("dificuldade");
        if (!Dificuldades.Contains(dificuldade)) return Erro("Escolha a dificuldade.");
        if (b.IdInt("idMateria") is not int idMateria) return Erro("Escolha a matéria.");
        var alts = (b.ListaTextos("alternativas") ?? new List<string>()).Select(a => a.Trim()).ToList();
        if (alts.Count > 5 || alts.Any(a => a.Length > 255)) return Erro("No máximo 5 alternativas de até 255 caracteres.");
        // NÃO remover as vazias: o índice da correta é a posição original.
        var idx = b.Inteiro("indiceCorreta") ?? -1;

        if (await ChecarMateriaAsync(db, eu, idMateria) is { } negado) return Proibido(negado);

        var x = await db.UnicoAsync("sp_SalvarQuestao",
            P("id_questao", idQuestao), P("id_professor", eu.IdProfessor), P("id_materia", idMateria), P("enunciado", enunciado),
            P("dificuldade", dificuldade), P("codigo_exemplo", NuloSeVazio(Texto.Aparar(b.Str("codigo"), 5000))),
            P("alternativas_json", System.Text.Json.JsonSerializer.Serialize(alts)),
            P("indice_correta", idx), P("enviar", b.Verdadeiro("enviar") ? 1 : 0));
        return Results.Json(new { id = x.Int("id_questao") }, statusCode: idQuestao is null ? 201 : 200);
    }

    private static async Task<IResult> SalvarDesafio(HttpRequest req, HttpContext ctx, IDb db, int? idDesafio)
    {
        var eu = UsuarioToken.De(ctx.User);
        var b = await Corpo.LerAsync(req);

        var titulo = b.Str("titulo").Trim();
        var enunciado = b.Str("enunciado").Trim();
        if (titulo.Length is 0 or > 150) return Erro("Informe o título (até 150 caracteres).");
        if (enunciado.Length is 0 or > 4000) return Erro("Informe o enunciado (até 4000 caracteres).");
        var dificuldade = b.Str("dificuldade");
        if (!Dificuldades.Contains(dificuldade)) return Erro("Escolha a dificuldade.");
        if (b.IdInt("idMateria") is not int idMateria) return Erro("Escolha a matéria.");

        if (await ChecarMateriaAsync(db, eu, idMateria) is { } negado) return Proibido(negado);

        var x = await db.UnicoAsync("sp_SalvarDesafio",
            P("id_desafio", idDesafio), P("id_professor", eu.IdProfessor), P("id_materia", idMateria), P("titulo", titulo),
            P("enunciado", enunciado), P("dificuldade", dificuldade),
            P("codigo_base", NuloSeVazio(Texto.Aparar(b.Str("codigo"), 5000))),
            P("dica", NuloSeVazio(Texto.Aparar(b.Str("dica"), 1000))),
            P("enviar", b.Verdadeiro("enviar") ? 1 : 0));
        return Results.Json(new { id = x.Int("id_desafio") }, statusCode: idDesafio is null ? 201 : 200);
    }
}
