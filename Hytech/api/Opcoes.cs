using System.Text.RegularExpressions;

namespace HytechApi;

public sealed record JwtOpcoes(string Key, string Issuer, string Audience, int ExpiraMinutos);

public sealed record EmailOpcoes(bool Smtp, string Host, int Porta, bool Ssl, string Usuario, string Senha, string De);

/// <summary>
/// Configurações da API (appsettings.json, user-secrets, variáveis de ambiente). Equivale ao antigo .env do Node.
/// Em variável de ambiente use "__" no lugar de ":" — ex.: Jwt__Key, Email__Host, Plataforma__ServirSite.
/// </summary>
public sealed class Opcoes
{
    public bool Producao { get; init; }
    public JwtOpcoes Jwt { get; init; } = null!;
    public string[] CorsOrigens { get; init; } = Array.Empty<string>();
    public EmailOpcoes Email { get; init; } = null!;
    public int CodigoMinutos { get; init; }
    /// <summary>Pasta do site (web/) quando a própria API entrega o site; null = só API.</summary>
    public string? SiteDir { get; init; }
    public bool ConfiarNoProxy { get; init; }
    public bool LogRequisicoes { get; init; }
    public int CustoBcrypt { get; init; }
    public int LimiteGeralPorMinuto { get; init; }
    public int LimiteAuthPorMinuto { get; init; }
    public int LimiteConteudoPorHora { get; init; }
    public int LimiteTicketsPorHora { get; init; }

    public static Opcoes Carregar(IConfiguration cfg, IHostEnvironment env)
    {
        var producao = env.IsProduction();

        // ── JWT: a chave NUNCA fica no repositório (user-secrets em dev, variável de ambiente em produção) ──
        var chave = cfg["Jwt:Key"] ?? "";
        if (chave.Length < 32)
            throw new InvalidOperationException(
                "Jwt:Key ausente ou curta demais (mínimo 32 caracteres). Em dev: dotnet user-secrets set \"Jwt:Key\" \"<texto-aleatorio-longo>\" " +
                "(gere um com: dotnet run -- --chave). Em produção: variável de ambiente Jwt__Key.");
        if (producao && Regex.IsMatch(chave, "troque|exemplo|123456", RegexOptions.IgnoreCase))
            throw new InvalidOperationException("Jwt:Key ainda é o valor de exemplo. Gere uma chave aleatória (dotnet run -- --chave).");

        var conexao = cfg.GetConnectionString("HytechDb") ?? "";
        if (producao && Regex.IsMatch(conexao, "troque", RegexOptions.IgnoreCase))
            throw new InvalidOperationException("ConnectionStrings:HytechDb ainda tem a senha de exemplo. Defina a conexão real (ConnectionStrings__HytechDb).");

        // ── E-mail: com Email:Host envia de verdade; sem ele, "modo console" (o código aparece no terminal) ──
        var host = cfg["Email:Host"] ?? "";
        var modoConsole = string.Equals(cfg["Email:Modo"], "console", StringComparison.OrdinalIgnoreCase);
        var smtp = host.Length > 0 && !modoConsole;
        if (producao && !smtp && !modoConsole)
            throw new InvalidOperationException("Em produção, configure o e-mail (Email:Host, Email:Usuario, Email:Senha) ou defina Email:Modo=console (só para demonstração).");
        var usuario = cfg["Email:Usuario"] ?? "";
        var senha = cfg["Email:Senha"] ?? "";
        if (smtp && (usuario.Length == 0 || senha.Length == 0))
            throw new InvalidOperationException("Email:Host foi definido, mas faltam Email:Usuario e Email:Senha.");
        var porta = int.TryParse(cfg["Email:Porta"], out var pt) && pt > 0 ? pt : 587;
        if (smtp && porta == 465)
            throw new InvalidOperationException("A porta 465 (SSL implícito) não é suportada. Use Email:Porta=587 (STARTTLS) — funciona no Gmail e no Outlook.");
        var de = cfg["Email:De"];
        if (string.IsNullOrWhiteSpace(de))
            de = usuario.Length > 0 ? $"HyTech <{usuario}>" : "HyTech <nao-responder@hytech.local>";

        // ── Site (web/) servido pela própria API ──
        string? siteDir = null;
        if (cfg.GetValue<bool>("Plataforma:ServirSite"))
        {
            siteDir = Path.GetFullPath(Path.Combine(env.ContentRootPath, cfg["Plataforma:SiteDir"] ?? "../web"));
            if (!Directory.Exists(siteDir))
                throw new InvalidOperationException($"Plataforma:ServirSite está ligado, mas a pasta do site não existe: {siteDir}");
        }

        int Inteiro(string chaveCfg, int padrao) => cfg.GetValue<int?>(chaveCfg) is > 0 and var n ? n : padrao;

        return new Opcoes
        {
            Producao = producao,
            Jwt = new JwtOpcoes(chave, cfg["Jwt:Issuer"] ?? "HytechApi", cfg["Jwt:Audience"] ?? "HytechWeb", Inteiro("Jwt:ExpiraMinutos", 120)),
            CorsOrigens = cfg.GetSection("Cors:Origins").Get<string[]>() ?? Array.Empty<string>(),
            Email = new EmailOpcoes(smtp, host, porta, cfg.GetValue("Email:Ssl", true), usuario, senha, de),
            CodigoMinutos = Inteiro("Plataforma:CodigoMinutos", 10),
            SiteDir = siteDir,
            ConfiarNoProxy = cfg.GetValue<bool>("Plataforma:ConfiarNoProxy"),
            LogRequisicoes = cfg.GetValue("Plataforma:LogRequisicoes", producao),
            CustoBcrypt = Inteiro("Plataforma:CustoBcrypt", 11),
            LimiteGeralPorMinuto = Inteiro("Plataforma:LimiteGeralPorMinuto", 300),
            LimiteAuthPorMinuto = Inteiro("Plataforma:LimiteAuthPorMinuto", 10),
            LimiteConteudoPorHora = Inteiro("Plataforma:LimiteConteudoPorHora", 120),
            LimiteTicketsPorHora = Inteiro("Plataforma:LimiteTicketsPorHora", 60),
        };
    }
}
