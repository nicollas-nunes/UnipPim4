using System.Text;
using System.Threading.RateLimiting;
using HytechApi.Data;
using HytechApi.Endpoints;
using HytechApi.Infra;
using HytechApi.Security;
using HytechApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;

namespace HytechApi;

/// <summary>Monta a API inteira. Separado do Program.cs para os testes poderem subir a API com banco e e-mail falsos.</summary>
public static class HytechApp
{
    public static WebApplication Construir(string[] args, Action<WebApplicationBuilder>? antes = null)
    {
        var builder = WebApplication.CreateBuilder(args);
        antes?.Invoke(builder);   // testes: configuração em memória e IDb/IEmail falsos (registrados antes dos padrões abaixo)

        var opc = Opcoes.Carregar(builder.Configuration, builder.Environment);
        builder.WebHost.ConfigureKestrel(o => o.AddServerHeader = false);

        // ── Serviços ──
        builder.Services.AddSingleton(opc);
        builder.Services.TryAddSingleton<IDb, SqlDb>();
        builder.Services.TryAddSingleton<IEmail>(_ => opc.Email.Smtp ? (IEmail)new EmailSmtp(opc) : new EmailConsole());
        builder.Services.AddSingleton<ISenhaHasher>(_ => new SenhaHasherBcrypt(opc.CustoBcrypt));
        builder.Services.AddSingleton<TokenService>();
        builder.Services.AddSingleton<Verificacao>();

        // ── JWT ──
        builder.Services.AddAuthentication("Bearer").AddJwtBearer("Bearer", o =>
        {
            o.MapInboundClaims = false;   // mantém os nomes curtos: sub, name, role
            o.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true, ValidIssuer = opc.Jwt.Issuer,
                ValidateAudience = true, ValidAudience = opc.Jwt.Audience,
                ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(opc.Jwt.Key)),
                ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 },
                ValidateLifetime = true, ClockSkew = TimeSpan.FromMinutes(1),
                NameClaimType = "name", RoleClaimType = "role"
            };
        });
        builder.Services.AddAuthorizationBuilder()
            .AddPolicy("login", p => p.RequireAuthenticatedUser())
            .AddPolicy("aluno", p => p.RequireRole("aluno"))
            .AddPolicy("professor", p => p.RequireRole("professor"))
            .AddPolicy("aluno-ou-professor", p => p.RequireRole("aluno", "professor"))
            .AddPolicy("admin", p => p.RequireRole("admin"));            // rotas /admin/*: só token com papel admin
        builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, RespostaAutorizacao>();

        // ── CORS: só as origens listadas em appsettings (o index.html servido em outro endereço) ──
        builder.Services.AddCors(o => o.AddPolicy("web", p =>
            p.WithOrigins(opc.CorsOrigens).WithMethods("GET", "POST", "PUT", "PATCH", "DELETE").AllowAnyHeader()));

        // ── Limites de requisições ──
        builder.Services.AddRateLimiter(o =>
        {
            static string Ip(HttpContext c) => c.Connection.RemoteIpAddress?.ToString() ?? "anon";
            static string Quem(HttpContext c) => c.User.FindFirst("sub")?.Value is { Length: > 0 } s ? "u" + s : "ip" + Ip(c);
            static RateLimitPartition<string> Janela(string chave, int limite, TimeSpan janela)
                => RateLimitPartition.GetFixedWindowLimiter(chave, _ => new FixedWindowRateLimiterOptions
                    { PermitLimit = limite, Window = janela, QueueLimit = 0 });

            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            // Toda a API: requisições por IP por minuto
            o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(c =>
                Janela("geral:" + Ip(c), opc.LimiteGeralPorMinuto, TimeSpan.FromMinutes(1)));
            // Login / cadastro / esqueci a senha: tentativas por IP por minuto
            o.AddPolicy("auth", c => Janela("auth:" + Ip(c), opc.LimiteAuthPorMinuto, TimeSpan.FromMinutes(1)));
            // Pedidos de código / tentativas de confirmação na conta (o banco ainda limita por conta)
            o.AddPolicy("conta", c => Janela("conta:" + Ip(c), opc.LimiteAuthPorMinuto, TimeSpan.FromMinutes(1)));
            // Criações/edições do professor: por usuário por hora
            o.AddPolicy("escrita-professor", c => Janela("prof:" + Quem(c), opc.LimiteConteudoPorHora, TimeSpan.FromHours(1)));
            // Tickets/mensagens: por usuário por hora
            o.AddPolicy("tickets", c => Janela("tickets:" + Quem(c), opc.LimiteTicketsPorHora, TimeSpan.FromHours(1)));

            o.OnRejected = (ctx, ct) =>
            {
                var politica = ctx.HttpContext.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName;
                var msg = politica switch
                {
                    "auth" or "conta" => "Muitas tentativas. Aguarde um minuto.",
                    "escrita-professor" => "Muitas alterações em pouco tempo. Tente novamente mais tarde.",
                    "tickets" => "Muitas mensagens em pouco tempo. Tente novamente mais tarde.",
                    _ => "Muitas requisições. Aguarde um instante."
                };
                return new ValueTask(Respostas.Erro(ctx.HttpContext, StatusCodes.Status429TooManyRequests, msg));
            };
        });

        if (opc.ConfiarNoProxy)   // atrás de Nginx/IIS/Cloudflare: o IP real vem do proxy
            builder.Services.Configure<ForwardedHeadersOptions>(o =>
            {
                o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                o.KnownNetworks.Clear(); o.KnownProxies.Clear(); o.ForwardLimit = 1;
            });

        var app = builder.Build();
        var logHttp = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Hytech.Http");

        // ── Pipeline (a ordem importa) ──
        if (opc.ConfiarNoProxy) app.UseForwardedHeaders();
        app.UseCabecalhosSeguranca(opc);
        if (opc.LogRequisicoes) app.UseLogRequisicoes(logHttp);
        app.UseTratamentoErros(logHttp);
        app.UseLimiteCorpo();

        if (opc.SiteDir is { } site)   // a própria API entrega o site (um só endereço, sem CORS)
        {
            var arquivos = new PhysicalFileProvider(site);
            app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = arquivos });
            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = arquivos,
                OnPrepareResponse = c => c.Context.Response.Headers.CacheControl = opc.Producao ? "public,max-age=3600" : "no-cache"
            });
        }

        app.UseRouting();
        app.UseCors("web");
        app.UseAuthentication();
        app.UseRateLimiter();
        app.UseAuthorization();

        app.MapGet("/saude", () => Results.Ok(new { status = "ok" }));
        if (opc.SiteDir is null) app.MapGet("/", () => Results.Ok(new { api = "HytechApi", status = "ok" }));
        app.MapAuth();
        app.MapPlataforma();
        app.MapAluno();
        app.MapProfessor();
        app.MapConta();
        app.MapTickets();
        app.MapAdmin();
        app.MapFallback(() => Results.Json(new { erro = "Rota não encontrada." }, statusCode: 404));

        return app;
    }
}
