using System.Diagnostics;
using System.Security.Claims;
using HytechApi.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http.Features;

namespace HytechApi.Infra;

/// <summary>Respostas de erro em JSON { "erro": "..." } (o site lê esse campo).</summary>
public static class Respostas
{
    public static Task Erro(HttpContext ctx, int status, string mensagem)
    {
        ctx.Response.StatusCode = status;
        return ctx.Response.WriteAsJsonAsync(new { erro = mensagem });
    }
}

/// <summary>401 / 403 com a mesma mensagem JSON que a API antiga dava (o site reage ao 401 pedindo novo login).</summary>
public sealed class RespostaAutorizacao : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _padrao = new();

    public async Task HandleAsync(RequestDelegate next, HttpContext ctx, AuthorizationPolicy policy, PolicyAuthorizationResult resultado)
    {
        if (resultado.Challenged)
        {
            var tinhaToken = ctx.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase);
            await Respostas.Erro(ctx, StatusCodes.Status401Unauthorized,
                tinhaToken ? "Sessão expirada. Faça login novamente." : "Faça login para continuar.");
            return;
        }
        if (resultado.Forbidden)
        {
            await Respostas.Erro(ctx, StatusCodes.Status403Forbidden, "Acesso negado para este perfil.");
            return;
        }
        await _padrao.HandleAsync(next, ctx, policy, resultado);
    }
}

public static class Middlewares
{
    /// <summary>Cabeçalhos de segurança (o que o "helmet" fazia no Node). O site usa scripts/estilos embutidos e a fonte do Google.</summary>
    public static IApplicationBuilder UseCabecalhosSeguranca(this IApplicationBuilder app, Opcoes opc)
        => app.Use((ctx, next) =>
        {
            var h = ctx.Response.Headers;
            h["Content-Security-Policy"] =
                "default-src 'self'; base-uri 'self'; font-src 'self' https://fonts.gstatic.com; form-action 'self'; " +
                "frame-ancestors 'none'; img-src 'self' data:; object-src 'none'; connect-src 'self'; " +
                "script-src 'self' 'unsafe-inline'; script-src-attr 'unsafe-inline'; " +
                "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com" + (opc.Producao ? "; upgrade-insecure-requests" : "");
            h["X-Content-Type-Options"] = "nosniff";
            h["X-Frame-Options"] = "DENY";
            h["Referrer-Policy"] = "no-referrer";
            h["Cross-Origin-Opener-Policy"] = "same-origin";
            h["Cross-Origin-Resource-Policy"] = "cross-origin";   // a API é chamada de outra origem (o CORS controla quem pode)
            h["X-DNS-Prefetch-Control"] = "off";
            h["X-Permitted-Cross-Domain-Policies"] = "none";
            if (opc.Producao && ctx.Request.IsHttps) h["Strict-Transport-Security"] = "max-age=15552000; includeSubDomains";
            return next();
        });

    /// <summary>Só método, caminho (sem query), status e tempo: nunca corpo, token ou e-mail.</summary>
    public static IApplicationBuilder UseLogRequisicoes(this IApplicationBuilder app, ILogger log)
        => app.Use(async (ctx, next) =>
        {
            var t = Stopwatch.StartNew();
            try { await next(); }
            finally { log.LogInformation("{Metodo} {Caminho} {Status} {Ms}ms", ctx.Request.Method, ctx.Request.Path.Value, ctx.Response.StatusCode, t.ElapsedMilliseconds); }
        });

    /// <summary>
    /// Erros: regra do banco -> 400 com a mensagem; ErroHttp -> o status dele; corpo grande -> 413;
    /// o resto vira 500 SEM detalhes (o detalhe vai só para o log do servidor).
    /// </summary>
    public static IApplicationBuilder UseTratamentoErros(this IApplicationBuilder app, ILogger log)
        => app.Use(async (ctx, next) =>
        {
            try { await next(); }
            catch (Exception ex) when (!ctx.Response.HasStarted)
            {
                switch (ex)
                {
                    case ServicoException s: await Respostas.Erro(ctx, 400, s.Message); break;
                    case ErroHttp e: await Respostas.Erro(ctx, e.Status, e.Message); break;
                    case BadHttpRequestException b when b.StatusCode == 413: await Respostas.Erro(ctx, 413, "Conteúdo grande demais."); break;
                    case BadHttpRequestException: await Respostas.Erro(ctx, 400, "Requisição inválida."); break;
                    default:
                        log.LogError(ex, "Erro não tratado em {Metodo} {Caminho}", ctx.Request.Method, ctx.Request.Path.Value);
                        await Respostas.Erro(ctx, 500, "Erro interno. Tente novamente."); break;
                }
            }
        });

    /// <summary>Corpo pequeno por padrão (100 KB); só a foto de perfil (base64) precisa de mais (1,5 MB).</summary>
    public static IApplicationBuilder UseLimiteCorpo(this IApplicationBuilder app)
        => app.Use((ctx, next) =>
        {
            var limite = ctx.Request.Path.StartsWithSegments("/conta/perfil") ? 1_536_000L : 102_400L;
            if (ctx.Request.ContentLength > limite) throw new ErroHttp(413, "Conteúdo grande demais.");
            var f = ctx.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (f is { IsReadOnly: false }) f.MaxRequestBodySize = limite;
            return next();
        });
}
