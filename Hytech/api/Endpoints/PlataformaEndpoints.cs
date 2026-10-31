using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using HytechApi.Data;
using HytechApi.Infra;
using HytechApi.Security;
using static HytechApi.Data.Parametros;

namespace HytechApi.Endpoints;

/// <summary>Conteúdo da plataforma (matérias, aulas, questões), configuração e ranking. Todas exigem login.</summary>
public static class PlataformaEndpoints
{
    public static void MapPlataforma(this IEndpointRouteBuilder app)
    {
        // Valores de gamificação definidos pelo admin (ChavesConfig). O web LÊ daqui, em vez de ter 5 / 15 fixos no JS.
        app.MapGet("/config/chaves", async (IDb db) =>
        {
            var c = await LerChavesAsync(db);
            return c is null ? Results.NotFound(new { erro = "Configuração não encontrada." }) : Results.Ok(c);
        }).RequireAuthorization("login");

        // Matérias, com slug (imagens do web), ordem e custo em chaves para desbloquear.
        app.MapGet("/materias", async (IDb db) =>
            Results.Ok(await db.ConsultarAsync("sp_WebListarMaterias", m => new
            {
                id = m.Int("id_materia"),
                titulo = m.Txt("titulo"),
                slug = Slug(m.Txt("titulo")),
                ordem = m.Int("ordem"),
                chavesParaDesbloquear = m.Int("chaves_para_desbloquear")
            }))).RequireAuthorization("login");

        // Aulas (tópicos) aprovadas da matéria, na ordem. Extras vêm por último (isExtra = true).
        app.MapGet("/materias/{id}/aulas", async (string id, IDb db) =>
        {
            if (Rotas.IdInt(id) is not int idMateria) return Erro("Matéria inválida.");
            return Results.Ok(await db.ConsultarAsync("sp_WebListarAulasMateria", a => new
            {
                id = a.Int("id_aula"),
                topico = a.TxtN("topico"),
                titulo = a.Txt("titulo"),
                conteudo = a.Txt("conteudo"),
                exemplo = a.TxtN("exemplo_codigo"),
                dica = new { titulo = a.TxtN("dica_titulo"), corpo = a.TxtN("dica_corpo") },
                ordem = a.IntN("ordem"),
                isExtra = a.Bool("is_extra"),
                chaves = a.Int("chaves_recompensa"),
                autor = a.TxtN("autor") is { Length: > 0 } x ? x : null
            }, P("id_materia", idMateria)));
        }).RequireAuthorization("login");

        // Questões aprovadas da matéria com as alternativas. NUNCA inclui o gabarito:
        // a resposta é conferida no banco por POST /aluno/questoes/{id}/responder.
        app.MapGet("/materias/{id}/questoes", async (string id, IDb db) =>
        {
            if (Rotas.IdInt(id) is not int idMateria) return Erro("Matéria inválida.");
            var questoes = await db.ExecAsync("sp_WebListarQuestoesMateria", P("id_materia", idMateria));
            var alts = await db.ExecAsync("sp_WebListarAlternativasMateria", P("id_materia", idMateria));
            return Results.Ok(questoes.Select(q => new
            {
                id = q.Int("id_questao"),
                pergunta = q.Txt("enunciado"),
                codigo = q.Txt("codigo_exemplo"),
                dificuldade = q.Txt("dificuldade"),
                chaves = q.Int("chaves_recompensa"),
                alternativas = alts.Where(a => a.Int("id_questao") == q.Int("id_questao"))
                                   .Select(a => new { id = a.Int("id_alternativa"), texto = a.Txt("texto") })
            }));
        }).RequireAuthorization("login");

        // Ranking real (alunos ativos, por pontos acumulados; empate = mesma posição).
        app.MapGet("/ranking", async (HttpContext ctx, IDb db) =>
        {
            var eu = UsuarioToken.De(ctx.User);
            var top = int.TryParse(ctx.Request.Query["top"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var t) && t != 0 ? t : 10;
            top = Math.Clamp(top, 1, 50);
            return Results.Ok(await db.ConsultarAsync("sp_RankingAlunos", a => new
            {
                posicao = a.Int("posicao"),
                nome = a.Txt("nome"),
                nickname = a.Txt("nickname"),
                foto = a.TxtN("foto_url") is { Length: > 0 } f ? f : null,
                pontos = a.Int("pontos_acumulados"),
                streak = a.Int("streak_atual"),
                fases = a.Int("fases_concluidas"),
                souEu = a.Txt("nickname") == eu.Nickname
            }, P("top", top)));
        }).RequireAuthorization("login");
    }

    public record ChavesDto(int QuestaoFacil, int QuestaoMedia, int QuestaoDificil, int Certificado);

    /// <summary>Lê o ChavesConfig (usada por /config/chaves e pelas rotas de admin).</summary>
    public static async Task<ChavesDto?> LerChavesAsync(IDb db)
        => (await db.ConsultarAsync("sp_ObterConfiguracaoPlataforma", r => new ChavesDto(
                r.Int("chaves_questao_facil"), r.Int("chaves_questao_media"),
                r.Int("chaves_questao_dificil"), r.Int("chaves_certificado")))).FirstOrDefault();

    /// <summary>"Banco de Dados" -> "banco-de-dados" (= assets/materias/banco-de-dados.webp).</summary>
    public static string Slug(string texto)
    {
        var sb = new StringBuilder();
        foreach (var ch in texto.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) sb.Append(ch);
        return Regex.Replace(sb.ToString().ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
    }

    private static IResult Erro(string msg) => Results.Json(new { erro = msg }, statusCode: 400);
}
