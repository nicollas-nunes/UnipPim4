using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HytechApi.Data;
using HytechApi.Infra;
using static HytechApi.Data.Parametros;

namespace HytechApi.Services;

/// <summary>Resultado de conferir/consultar um código: quem pediu e os dados guardados junto (ex.: senha_hash do cadastro).</summary>
public sealed record CodigoInfo(int? IdUsuario, Corpo Dados);

/// <summary>Códigos de verificação por e-mail: gera, guarda (só o hash), envia e confere.</summary>
public sealed class Verificacao
{
    private static readonly Dictionary<string, (string Titulo, string Frase)> Assuntos = new()
    {
        ["cadastro"]  = ("Confirme seu cadastro", "Use o código abaixo para confirmar seu cadastro na HyTech."),
        ["recuperar"] = ("Redefinir sua senha", "Use o código abaixo para criar uma nova senha na HyTech."),
        ["senha"]     = ("Confirme a troca de senha", "Use o código abaixo para confirmar a troca da sua senha na HyTech."),
        ["email"]     = ("Confirme seu novo e-mail", "Use o código abaixo para confirmar este novo e-mail na sua conta HyTech."),
    };

    private readonly IDb _db;
    private readonly Opcoes _opc;
    private readonly IEmail _email;
    private readonly ILogger<Verificacao> _log;

    public Verificacao(IDb db, Opcoes opc, IEmail email, ILogger<Verificacao> log)
    { _db = db; _opc = opc; _email = email; _log = log; }

    // HMAC com a chave do servidor: um vazamento da tabela não revela os códigos (e eles expiram em minutos).
    private string Hash(string finalidade, string chave, string codigo)
    {
        using var h = new HMACSHA256(Encoding.UTF8.GetBytes(_opc.Jwt.Key));
        return Convert.ToHexString(h.ComputeHash(Encoding.UTF8.GetBytes($"{finalidade}|{chave}|{codigo}"))).ToLowerInvariant();
    }

    public static string ChaveEmail(string e) => "e:" + e.Trim().ToLowerInvariant();
    public static string ChaveUsuario(int id) => "u:" + id;

    /// <summary>Cria o código no banco e envia por e-mail. <paramref name="dados"/> é guardado como JSON junto do código.</summary>
    public async Task EmitirAsync(string finalidade, string chave, string para, string nome = "", int? idUsuario = null, object? dados = null)
    {
        var codigo = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        await _db.ExecAsync("sp_CriarCodigoVerificacao",
            P("finalidade", finalidade), P("chave", chave), P("id_usuario", idUsuario),
            P("codigo_hash", Hash(finalidade, chave, codigo)),
            P("dados", dados is null ? null : JsonSerializer.Serialize(dados)),
            P("minutos", _opc.CodigoMinutos));

        var (titulo, frase) = Assuntos[finalidade];
        var ola = nome.Length > 0 ? $"Olá, {nome}!" : "Olá!";
        var texto = $"{ola}\n\n{frase}\n\n    {codigo}\n\nEle vale por {_opc.CodigoMinutos} minutos. Se não foi você, ignore este e-mail: nada será alterado.\n\n— Equipe HyTech";
        var html = $"""
            <div style="font-family:Arial,sans-serif;max-width:480px;margin:auto;padding:24px;border:1px solid #e5e0f5;border-radius:12px">
            <h2 style="color:#5b21b6;margin:0 0 12px">{WebUtility.HtmlEncode(titulo)}</h2><p>{WebUtility.HtmlEncode(ola)}</p><p>{WebUtility.HtmlEncode(frase)}</p>
            <p style="font-size:32px;letter-spacing:8px;font-weight:700;background:#f5f3ff;padding:14px;text-align:center;border-radius:8px">{codigo}</p>
            <p style="color:#555;font-size:13px">Ele vale por {_opc.CodigoMinutos} minutos. Se não foi você, ignore este e-mail: nada será alterado.</p></div>
            """;
        try
        {
            await _email.EnviarAsync(para, $"HyTech — {titulo}", texto, html);
        }
        catch (Exception ex)
        {
            _log.LogError("Falha ao enviar e-mail: {Mensagem}", ex.Message);
            throw new ErroHttp(503, "Não foi possível enviar o e-mail agora. Tente novamente em instantes.");
        }
    }

    /// <summary>Confere o código. Devolve quem pediu + dados e já o consome (uso único). Erro genérico para não ajudar adivinhação.</summary>
    public async Task<CodigoInfo> ConferirAsync(string finalidade, string chave, string codigo)
    {
        if (codigo.Length != 6 || !codigo.All(char.IsAsciiDigit)) throw new ServicoException("Digite o código de 6 dígitos.");

        var row = await _db.PrimeiroAsync("sp_ObterCodigoVerificacao", P("finalidade", finalidade), P("chave", chave))
                  ?? throw new ServicoException("Código inválido ou expirado. Peça um novo código.");

        var certo = false;
        try
        {
            certo = CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(row.Txt("codigo_hash")), Convert.FromHexString(Hash(finalidade, chave, codigo)));
        }
        catch (FormatException) { /* hash guardado inválido: tratado como código errado */ }

        if (!certo)
        {
            await _db.ExecAsync("sp_RegistrarTentativaCodigo", P("id_codigo", row.Int("id_codigo")));
            throw new ServicoException("Código incorreto. Confira e tente de novo.");
        }
        await _db.ExecAsync("sp_ConsumirCodigo", P("id_codigo", row.Int("id_codigo")));   // uso único
        return Montar(row);
    }

    /// <summary>Dados do último código pendente (mesmo expirado): serve para REENVIAR sem pedir tudo de novo.</summary>
    public async Task<CodigoInfo?> PendenteAsync(string finalidade, string chave)
    {
        var row = await _db.PrimeiroAsync("sp_ObterCodigoVerificacao",
            P("finalidade", finalidade), P("chave", chave), P("incluir_expirados", 1));
        return row is null ? null : Montar(row);
    }

    private static CodigoInfo Montar(Linha row)
    {
        var json = row.TxtN("dados");
        JsonElement? el = null;
        if (!string.IsNullOrEmpty(json))
        {
            try { el = JsonDocument.Parse(json).RootElement.Clone(); }
            catch (JsonException) { /* dados ilegíveis: tratados como ausentes */ }
        }
        return new CodigoInfo(row.IntN("id_usuario"), el is null ? Corpo.Vazio : Corpo.De(el));
    }
}
