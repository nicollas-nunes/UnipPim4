using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HytechApi.Data;
using HytechApi.Services;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace HytechApi.Testes;

/// <summary>E-mail guardado na memória: Enviados lista tudo o que seria enviado.</summary>
public sealed class EmailMemoria : IEmail
{
    public readonly List<(string Para, string Assunto, string Texto)> Enviados = new();
    public Task EnviarAsync(string para, string assunto, string texto, string html) { lock (Enviados) Enviados.Add((para, assunto, texto)); return Task.CompletedTask; }
    /// <summary>Último código de 6 dígitos enviado para este endereço.</summary>
    public string? UltimoCodigo(string para)
    {
        lock (Enviados) { var m = Enviados.LastOrDefault(e => e.Para == para); return m.Texto is null ? null : Regex.Match(m.Texto, @"\b(\d{6})\b").Groups[1].Value; }
    }
}

/// <summary>E-mail que sempre falha (testa a mensagem amigável 503).</summary>
public sealed class EmailQuebrado : IEmail
{
    public Task EnviarAsync(string para, string assunto, string texto, string html) => throw new Exception("SMTP caiu: senha=segredo");
}

/// <summary>Resposta JSON com acesso fácil: r["a"]["b"].S, r[0].N, r.Len ...</summary>
public sealed class J
{
    private readonly JsonElement? _e;
    public J(JsonElement? e) => _e = e;
    public J this[string nome] => new(_e is { ValueKind: JsonValueKind.Object } o && o.TryGetProperty(nome, out var v) ? v : null);
    public J this[int i] => new(_e is { ValueKind: JsonValueKind.Array } a && i < a.GetArrayLength() ? a[i] : null);
    public bool Existe => _e is not null;
    public bool Nulo => _e is null or { ValueKind: JsonValueKind.Null };
    public string? S => _e is { ValueKind: JsonValueKind.String } v ? v.GetString() : null;
    public int N => _e is { ValueKind: JsonValueKind.Number } v ? v.GetInt32() : throw new InvalidOperationException("não é número: " + this);
    public bool B => _e is { ValueKind: JsonValueKind.True };
    public int Len => _e is { ValueKind: JsonValueKind.Array } a ? a.GetArrayLength() : 0;
    public IEnumerable<J> Itens => _e is { ValueKind: JsonValueKind.Array } a ? a.EnumerateArray().Select(x => new J(x)).ToList() : Enumerable.Empty<J>();
    public override string ToString() => _e?.GetRawText() ?? "(vazio)";
}

public sealed record Resp(int Status, J Json, HttpResponseMessage Raw);

/// <summary>Sobe a API de verdade (Kestrel numa porta livre) com banco e e-mail falsos.</summary>
public sealed class Servidor : IAsyncDisposable
{
    public const string Senha = "senhaforte1";
    public WebApplication App = null!;
    public HttpClient Http = null!;
    public BancoFalso Db = null!;
    public EmailMemoria Email = null!;
    public string Url = "";

    public static async Task<Servidor> Subir(Dictionary<string, string?>? extra = null, IEmail? email = null, bool bancoNovo = true)
    {
        var s = new Servidor { Db = new BancoFalso(Senha), Email = new EmailMemoria() };
        var cfg = new Dictionary<string, string?>
        {
            ["Jwt:Key"] = new string('x', 40), ["Cors:Origins:0"] = "http://localhost:5500",
            ["Plataforma:LimiteAuthPorMinuto"] = "1000", ["Plataforma:CustoBcrypt"] = "4"
        };
        if (extra is not null) foreach (var kv in extra) cfg[kv.Key] = kv.Value;
        s.App = HytechApp.Construir(new[] { "--environment=Development", "--urls=http://127.0.0.1:0" }, b =>
        {
            b.Configuration.AddInMemoryCollection(cfg);
            b.Services.AddSingleton<IDb>(s.Db);
            b.Services.AddSingleton<IEmail>(email ?? s.Email);
            b.Logging.ClearProviders();
        });
        await s.App.StartAsync();
        s.Url = s.App.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        s.Http = new HttpClient { BaseAddress = new Uri(s.Url) };
        return s;
    }

    public async Task<Resp> Req(string rota, string metodo = "GET", object? corpo = null, string? token = null, Dictionary<string, string>? cab = null)
    {
        var msg = new HttpRequestMessage(new HttpMethod(metodo), rota);
        if (corpo is not null) msg.Content = new StringContent(corpo as string ?? JsonSerializer.Serialize(corpo), Encoding.UTF8, "application/json");
        if (token is not null) msg.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
        if (cab is not null) foreach (var kv in cab) msg.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
        var r = await Http.SendAsync(msg);
        var txt = await r.Content.ReadAsStringAsync();
        JsonElement? el = null;
        try { if (txt.Length > 0) el = JsonDocument.Parse(txt).RootElement.Clone(); } catch (JsonException) { }
        return new Resp((int)r.StatusCode, new J(el), r);
    }

    public Task<Resp> Post(string rota, object? corpo = null, string? token = null) => Req(rota, "POST", corpo ?? new { }, token);
    public Task<Resp> Get(string rota, string? token = null) => Req(rota, "GET", null, token);

    public async Task<string> Login(string email, string senha = Senha)
        => (await Post("/auth/login", new { email, senha })).Json["token"].S ?? throw new Exception("login falhou para " + email);

    public async Task<string> NovoAluno(int n)
    {
        await Post("/auth/cadastro", new { nome = "Aluno " + n, nickname = "aluno" + n, email = $"a{n}@x.com", senha = Senha });
        await Post("/auth/cadastro/confirmar", new { email = $"a{n}@x.com", codigo = Email.UltimoCodigo($"a{n}@x.com") });
        return await Login($"a{n}@x.com");
    }

    public async ValueTask DisposeAsync() { Http.Dispose(); await App.StopAsync(); await App.DisposeAsync(); }
}

/// <summary>Mini executor de testes (sem pacotes externos).</summary>
public static class T
{
    public static int Ok, Falhas;
    static readonly List<string> _falhas = new();

    public class FalhaException : Exception { public FalhaException(string m) : base(m) { } }

    public static async Task Caso(string nome, Func<Task> teste)
    {
        try { await teste(); Ok++; Console.WriteLine("  ✓ " + nome); }
        catch (Exception ex)
        {
            Falhas++; var m = $"{nome}\n      {ex.GetType().Name}: {ex.Message}";
            _falhas.Add(m); Console.WriteLine("  ✗ " + m);
        }
    }

    public static void Grupo(string nome) => Console.WriteLine("\n" + nome);

    public static void Igual<A>(A esperado, A real, string? msg = null)
    {
        if (!EqualityComparer<A>.Default.Equals(esperado, real))
            throw new FalhaException($"{msg ?? "valores diferentes"}: esperado «{esperado}», veio «{real}»");
    }
    public static void Verdade(bool cond, string msg) { if (!cond) throw new FalhaException(msg); }
    public static void Casa(string padrao, string? texto, string? msg = null)
    { if (texto is null || !Regex.IsMatch(texto, padrao)) throw new FalhaException($"{msg ?? "texto não casa"}: «{texto}» não casa com /{padrao}/"); }
    public static void Status(int esperado, Resp r, string? msg = null)
    { if (r.Status != esperado) throw new FalhaException($"{msg ?? "status"}: esperado {esperado}, veio {r.Status} — {r.Json}"); }
    public static async Task Lanca<E>(Func<Task> f, string? casa = null) where E : Exception
    {
        try { await f(); } catch (E ex) { if (casa is not null) Casa(casa, ex.Message); return; }
        throw new FalhaException("deveria ter lançado " + typeof(E).Name);
    }
    public static void Lanca<E>(Action f, string? casa = null) where E : Exception
    {
        try { f(); } catch (E ex) { if (casa is not null) Casa(casa, ex.Message); return; }
        throw new FalhaException("deveria ter lançado " + typeof(E).Name);
    }

    public static int Resumo()
    {
        Console.WriteLine($"\n{Ok} passaram, {Falhas} falharam.");
        if (Falhas > 0) { Console.WriteLine("\nFalhas:"); foreach (var f in _falhas) Console.WriteLine("  - " + f); }
        return Falhas == 0 ? 0 : 1;
    }
}
