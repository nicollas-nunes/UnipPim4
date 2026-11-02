using System.Globalization;
using System.Text.Json;

namespace HytechApi.Infra;

/// <summary>Erro com status HTTP próprio (ex.: 404, 503). Vira { "erro": "..." }.</summary>
public sealed class ErroHttp : Exception
{
    public int Status { get; }
    public ErroHttp(int status, string mensagem) : base(mensagem) => Status = status;
}

/// <summary>
/// Corpo JSON da requisição, lido de forma tolerante: texto de entrada é NÃO CONFIÁVEL.
/// Campo com tipo errado (objeto, lista, número onde se espera texto) vira "vazio" e cai na validação (400),
/// nunca em erro 500 — mesmo comportamento da API antiga em Node.
/// </summary>
public sealed class Corpo
{
    private readonly JsonElement? _obj;
    private Corpo(JsonElement? obj) => _obj = obj;

    public static readonly Corpo Vazio = new(null);

    /// <summary>Sem objeto JSON nenhum (corpo vazio ou que não é um objeto).</summary>
    public bool SemObjeto => _obj is null;

    /// <summary>O objeto JSON original (para guardar/reenviar tal como veio).</summary>
    public object? Bruto => _obj;

    public static Corpo De(JsonElement? el) => new(el is { ValueKind: JsonValueKind.Object } o ? o : null);

    public static async Task<Corpo> LerAsync(HttpRequest req)
    {
        using var ms = new MemoryStream();
        await req.Body.CopyToAsync(ms);          // o limite de tamanho é imposto pelo Kestrel (ver HytechApp)
        if (ms.Length == 0) return Vazio;
        try
        {
            using var doc = JsonDocument.Parse(ms.ToArray());
            return De(doc.RootElement.Clone());
        }
        catch (JsonException) { throw new ErroHttp(400, "JSON inválido."); }
    }

    private JsonElement? Campo(string nome)
        => _obj is { } o && o.TryGetProperty(nome, out var v) ? v : null;

    /// <summary>O campo existe no JSON (mesmo que seja null).</summary>
    public bool Existe(string nome) => Campo(nome) is not null;

    /// <summary>Existe e não é null.</summary>
    public bool Presente(string nome) => Campo(nome) is { ValueKind: not JsonValueKind.Null };

    /// <summary>Texto; qualquer outro tipo (ou ausente) vira "".</summary>
    public string Str(string nome) => Campo(nome) is { ValueKind: JsonValueKind.String } v ? v.GetString() ?? "" : "";

    /// <summary>Presente mas NÃO é texto (objeto, lista, número...).</summary>
    public bool TipoInvalido(string nome) => Presente(nome) && Campo(nome)!.Value.ValueKind != JsonValueKind.String;

    /// <summary>Texto ou número escrito como texto (o código de 6 dígitos pode chegar como 123456).</summary>
    public string StrOuNumero(string nome) => Campo(nome) switch
    {
        { ValueKind: JsonValueKind.String } v => v.GetString() ?? "",
        { ValueKind: JsonValueKind.Number } v => v.GetRawText(),
        _ => ""
    };

    /// <summary>Inteiro positivo (número ou texto numérico); senão null.</summary>
    public int? IdInt(string nome) => Campo(nome) is { } v ? IdInt(v) : null;

    public static int? IdInt(JsonElement v)
    {
        switch (v.ValueKind)
        {
            case JsonValueKind.Number:
                if (v.TryGetDouble(out var d) && d == Math.Floor(d) && d > 0 && d <= int.MaxValue) return (int)d;
                return null;
            case JsonValueKind.String:
                return Rotas.IdInt(v.GetString());
            default:
                return null;
        }
    }

    /// <summary>Número inteiro (positivo, zero ou negativo); senão null. Ex.: índice da alternativa correta.</summary>
    public int? Inteiro(string nome)
    {
        if (Campo(nome) is { ValueKind: JsonValueKind.Number } v && v.TryGetDouble(out var d)
            && d == Math.Floor(d) && Math.Abs(d) <= int.MaxValue) return (int)d;
        return null;
    }

    /// <summary>"Verdadeiro" no sentido do JavaScript: true, número diferente de 0, texto não vazio.</summary>
    public bool Verdadeiro(string nome) => Campo(nome) switch
    {
        { ValueKind: JsonValueKind.True } => true,
        { ValueKind: JsonValueKind.Number } v => v.TryGetDouble(out var d) && d != 0,
        { ValueKind: JsonValueKind.String } v => !string.IsNullOrEmpty(v.GetString()),
        { ValueKind: JsonValueKind.Array or JsonValueKind.Object } => true,
        _ => false
    };

    /// <summary>Lista de textos (itens que não são texto viram ""); null se o campo não for uma lista.</summary>
    public List<string>? ListaTextos(string nome)
        => Campo(nome) is { ValueKind: JsonValueKind.Array } a
            ? a.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() ?? "" : "").ToList()
            : null;
}

public static class Rotas
{
    /// <summary>Inteiro positivo vindo da URL ("12"); senão null.</summary>
    public static int? IdInt(string? v)
        => int.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n > 0 ? n : null;
}
