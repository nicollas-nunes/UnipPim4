using System.Text;
using System.Threading.RateLimiting;
using HytechApi.Data;
using HytechApi.Endpoints;
using HytechApi.Security;
using Microsoft.IdentityModel.Tokens;

// Utilitário para o seed do admin:  dotnet run -- --hash "SuaSenhaForte"   -> imprime o hash BCrypt e sai.
if (args.Length == 2 && args[0] == "--hash")
{
    Console.WriteLine(new SenhaHasherBcrypt().Hash(args[1]));
    return;
}

var builder = WebApplication.CreateBuilder(args);

// ── JWT: a chave NUNCA fica no repositório (user-secrets em dev, variável de ambiente em produção) ──
var jwt = builder.Configuration.GetSection("Jwt");
var chave = jwt["Key"];
if (string.IsNullOrWhiteSpace(chave) || chave.Length < 32)
    throw new InvalidOperationException(
        "Jwt:Key ausente ou curta demais (mínimo 32 caracteres). Em dev: dotnet user-secrets set \"Jwt:Key\" \"<texto-aleatorio-longo>\"");

builder.Services.AddSingleton<Db>();
builder.Services.AddSingleton<ISenhaHasher, SenhaHasherBcrypt>();
builder.Services.AddSingleton<TokenService>();

builder.Services.AddAuthentication("Bearer").AddJwtBearer("Bearer", o =>
{
    o.MapInboundClaims = false;   // mantém os nomes curtos: sub, name, role
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true, ValidIssuer = jwt["Issuer"],
        ValidateAudience = true, ValidAudience = jwt["Audience"],
        ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(chave)),
        ValidateLifetime = true, ClockSkew = TimeSpan.FromMinutes(1),
        NameClaimType = "name", RoleClaimType = "role"
    };
});
builder.Services.AddAuthorization();

// ── CORS: só as origens listadas em appsettings (o index.html estático) ──
var origens = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? Array.Empty<string>();
builder.Services.AddCors(o => o.AddPolicy("web", p => p.WithOrigins(origens).AllowAnyHeader().AllowAnyMethod()));

// ── Limite de tentativas em login/cadastro (10 por minuto por IP) ──
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("auth", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "anon",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
});

var app = builder.Build();

// Regra de negócio do banco (RAISERROR) -> 400 com a mensagem. Outros erros viram 500 sem detalhes.
app.Use(async (ctx, next) =>
{
    try { await next(); }
    catch (ServicoException ex)
    {
        ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
        await ctx.Response.WriteAsJsonAsync(new { erro = ex.Message });
    }
});

app.UseCors("web");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => Results.Ok(new { api = "HytechApi", status = "ok" }));
app.MapAuth();
app.MapPlataforma();

app.Run();
