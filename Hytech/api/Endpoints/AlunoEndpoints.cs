using HytechApi.Data;
using HytechApi.Infra;
using HytechApi.Security;
using static HytechApi.Data.Parametros;

namespace HytechApi.Endpoints;

/// <summary>Área do aluno. O id do aluno vem SEMPRE do token, nunca do corpo da requisição.</summary>
public static class AlunoEndpoints
{
    public static void MapAluno(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/aluno").RequireAuthorization("aluno");

        // Responde uma questão. Quem diz se acertou (e quantas chaves ganhou) é o BANCO (sp_ResponderQuestao).
        g.MapPost("/questoes/{id}/responder", async (string id, HttpRequest req, HttpContext ctx, IDb db) =>
        {
            var eu = UsuarioToken.De(ctx.User);
            var b = await Corpo.LerAsync(req);
            var idQuestao = Rotas.IdInt(id);
            var idAlternativa = b.IdInt("idAlternativa");
            if (idQuestao is null || idAlternativa is null) return Erro("Informe a questão e a alternativa.");

            var x = await db.UnicoAsync("sp_ResponderQuestao",
                P("id_aluno", eu.IdAluno), P("id_questao", idQuestao), P("id_alternativa", idAlternativa));
            return Results.Ok(new
            {
                acertou = x.Bool("acertou"),
                jaConcluida = x.Bool("ja_concluida"),
                chavesGanhas = x.Int("chaves_ganhas"),
                certificadoEmitido = x.Bool("certificado_emitido"),
                saldoChaves = x.Int("saldo_chaves")
            });
        });

        // ── Estado completo do aluno: tudo o que o site precisa para reconstruir a tela depois do login/F5 ──
        g.MapGet("/estado", async (HttpContext ctx, IDb db) =>
        {
            var eu = UsuarioToken.De(ctx.User);
            var perfil = await db.PrimeiroAsync("sp_ObterPerfilAluno", P("id_aluno", eu.IdAluno));
            if (perfil is null) return Results.NotFound(new { erro = "Aluno não encontrado." });

            var tabelas = await db.ExecMultiAsync("sp_ProgressoAluno", P("id_aluno", eu.IdAluno));
            List<Linha> T(int i) => i < tabelas.Count ? tabelas[i] : new List<Linha>();

            return Results.Ok(new
            {
                resumo = new
                {
                    pontos = perfil.Int("pontos_acumulados"),     // total ganho na vida: vale para nível e ranking
                    saldoChaves = perfil.Int("chaves_saldo"),     // o que ainda pode gastar
                    streak = perfil.Int("streak_atual"),
                    posicao = perfil.IntN("posicao_ranking"),
                    fasesConcluidas = perfil.Int("fases_concluidas"),
                    questoesConcluidas = perfil.Int("questoes_concluidas"),
                    certificados = perfil.Int("certificados")
                },
                perfil = new
                {
                    nome = perfil.Txt("nome"), nickname = perfil.Txt("nickname"), email = perfil.Txt("email"),
                    foto = perfil.TxtN("foto_url") is { Length: > 0 } f ? f : null
                },
                aulasConcluidas = T(0).Select(a => a.Int("id_aula")),
                questoesConcluidas = T(1).Select(q => q.Int("id_questao")),
                materias = T(2).Select(m => new { id = m.Int("id_materia"), desbloqueada = m.Bool("desbloqueada") }),
                certificados = T(3).Select(c => new { idMateria = c.Int("id_materia"), dataEmissao = c.DataN("data_emissao") })
            });
        });

        // ── "Marcar como lido": o banco credita as chaves da aula (uma vez só) e pode emitir certificado ──
        g.MapPost("/aulas/{id}/concluir", async (string id, HttpContext ctx, IDb db) =>
        {
            var eu = UsuarioToken.De(ctx.User);
            if (Rotas.IdInt(id) is not int idAula) return Erro("Aula inválida.");
            var x = await db.UnicoAsync("sp_ConcluirAula", P("id_aluno", eu.IdAluno), P("id_aula", idAula));
            return Results.Ok(new
            {
                chavesGanhas = x.Int("chaves_ganhas"),
                certificadoEmitido = x.Bool("certificado_emitido"),
                saldoChaves = x.Int("saldo_chaves")
            });
        });

        // ── Desbloquear matéria gastando chaves (o banco confere saldo e se a anterior foi concluída) ──
        g.MapPost("/materias/{id}/desbloquear", async (string id, HttpContext ctx, IDb db) =>
        {
            var eu = UsuarioToken.De(ctx.User);
            if (Rotas.IdInt(id) is not int idMateria) return Erro("Matéria inválida.");
            var x = await db.UnicoAsync("sp_DesbloquearMateria", P("id_aluno", eu.IdAluno), P("id_materia", idMateria));
            return Results.Ok(new
            {
                desbloqueada = x.Bool("desbloqueada"),
                chavesGastas = x.Int("chaves_gastas"),
                saldoChaves = x.Int("saldo_chaves")
            });
        });

        // ── Desafios dos professores (só os aprovados) ──
        g.MapGet("/desafios", async (HttpContext ctx, IDb db) =>
        {
            var eu = UsuarioToken.De(ctx.User);
            return Results.Ok(await db.ConsultarAsync("sp_WebListarDesafiosAluno", d => new
            {
                id = d.Int("id_desafio"),
                idMateria = d.Int("id_materia"),
                materia = d.Txt("materia"),
                titulo = d.Txt("titulo"),
                enunciado = d.Txt("enunciado"),
                dica = d.Txt("dica"),
                dificuldade = d.Txt("dificuldade"),
                codigo = d.Txt("codigo_base"),
                autor = d.Txt("autor"),
                concluido = d.Bool("concluido")
            }, P("id_aluno", eu.IdAluno)));
        });

        g.MapPost("/desafios/{id}/concluir", async (string id, HttpContext ctx, IDb db) =>
        {
            var eu = UsuarioToken.De(ctx.User);
            if (Rotas.IdInt(id) is not int idDesafio) return Erro("Desafio inválido.");
            var x = await db.UnicoAsync("sp_WebConcluirDesafio", P("id_aluno", eu.IdAluno), P("id_desafio", idDesafio));
            return Results.Ok(new
            {
                jaConcluido = x.Bool("ja_concluido"),
                chavesGanhas = x.Int("chaves_ganhas"),
                saldoChaves = x.Int("saldo_chaves")
            });
        });
    }

    private static IResult Erro(string msg) => Results.Json(new { erro = msg }, statusCode: 400);
}
