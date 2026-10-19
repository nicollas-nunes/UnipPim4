using System.Globalization;
using System.Text;
using HytechApi.Data;

namespace HytechApi.Endpoints;

public static class PlataformaEndpoints
{
    public static void MapPlataforma(this IEndpointRouteBuilder app)
    {
        // Valores de gamificação definidos pelo admin (ChavesConfig). O web deve LER daqui,
        // em vez de ter 5 / 15 chaves fixos no JavaScript.
        app.MapGet("/config/chaves", async (Db db) =>
        {
            var c = (await db.ConsultarAsync("sp_ObterConfiguracaoPlataforma", r => new
            {
                questaoFacil = r.Int("chaves_questao_facil"),
                questaoMedia = r.Int("chaves_questao_media"),
                questaoDificil = r.Int("chaves_questao_dificil"),
                certificado = r.Int("chaves_certificado")
            })).FirstOrDefault();
            return c is null ? Results.NotFound(new { erro = "Configuração não encontrada." }) : Results.Ok(c);
        }).RequireAuthorization();

        // Matérias do banco. 'slug' resolve o mapeamento de imagens do web (hoje por índice 1..4):
        // "Banco de Dados" -> "banco-de-dados" = assets/materias/banco-de-dados.webp
        app.MapGet("/materias", async (Db db) =>
        {
            var lista = await db.ConsultarAsync("sp_ListarMaterias", r => new
            {
                id = r.Int("id_materia"),
                titulo = r.Txt("titulo"),
                slug = Slug(r.Txt("titulo"))
            });
            return Results.Ok(lista);
        }).RequireAuthorization();
    }

    private static string Slug(string texto)
    {
        var d = texto.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var ch in d)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '-');
        }
        return string.Join('-', sb.ToString().Split('-', StringSplitOptions.RemoveEmptyEntries));
    }
}
