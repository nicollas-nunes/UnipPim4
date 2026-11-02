using System.Text.RegularExpressions;

namespace HytechApi.Services;

/// <summary>Texto escrito por professor é ENTRADA NÃO CONFIÁVEL: nunca vira HTML bruto.</summary>
public static class Texto
{
    public static string Esc(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
        .Replace("\"", "&quot;").Replace("'", "&#39;");

    /// <summary>"l1\nl1b\n\npar 2" -> "&lt;p&gt;l1&lt;br&gt;l1b&lt;/p&gt;&lt;br&gt;\n&lt;p&gt;par 2&lt;/p&gt;" (formato do conteúdo importado na Fase 2).</summary>
    public static string TextoParaHtml(string? texto)
    {
        var limpo = (texto ?? "").Replace("\r\n", "\n");
        var pars = Regex.Split(limpo, "\n{2,}").Select(p => p.Trim()).Where(p => p.Length > 0)
            .Select(p => "<p>" + Esc(p).Replace("\n", "<br>") + "</p>");
        return string.Join("<br>\n", pars);
    }

    /// <summary>Volta para texto puro (para o professor editar a própria aula).</summary>
    public static string HtmlParaTexto(string? html)
    {
        var s = Regex.Replace(html ?? "", "</p><br>\n?<p>", "\n\n");
        return s.Replace("<br>", "\n").Replace("</p>", "").Replace("<p>", "")
            .Replace("&lt;", "<").Replace("&gt;", ">").Replace("&quot;", "\"").Replace("&#39;", "'").Replace("&amp;", "&")
            .Trim();
    }

    /// <summary>Aparar e limitar: "  texto  " -> "texto" (no máximo n caracteres).</summary>
    public static string Aparar(string s, int n)
    {
        s = s.Trim();
        return s.Length > n ? s[..n] : s;
    }

    /// <summary>"joao.silva@gmail.com" -> "j***@gmail.com" (para mostrar na tela sem expor o e-mail inteiro).</summary>
    public static string MascararEmail(string e)
    {
        var i = e.IndexOf('@');
        var u = i >= 0 ? e[..i] : e;
        var d = i >= 0 ? e[(i + 1)..] : "";
        return (u.Length > 0 ? u[..1] : "") + "***@" + d;
    }
}
