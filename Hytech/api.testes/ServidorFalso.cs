using HytechApi.Data;
using HytechApi.Services;

namespace HytechApi.Testes;

/// <summary>
/// Sobe a API com um BANCO FALSO em memória (sem SQL Server) para ver o site funcionando.
/// Contas: aluno@hytech.com / professor@hytech.com / admin@hytech.com (admin só no app desktop). Senha: senha1234
/// Os códigos de e-mail aparecem no terminal. Defina Plataforma__ServirSite=true para a API entregar o site em http://localhost:3000.
/// </summary>
public static class ServidorFalso
{
    sealed class EmailDuplo : IEmail
    {
        public readonly EmailMemoria Memoria = new();
        public async Task EnviarAsync(string para, string assunto, string texto, string html)
        { await Memoria.EnviarAsync(para, assunto, texto, html); await new EmailConsole().EnviarAsync(para, assunto, texto, html); }
    }

    public static async Task Rodar(string[] args)
    {
        var db = new BancoFalso("senha1234", 8);
        var email = new EmailDuplo();
        var app = HytechApp.Construir(args.Concat(new[] { "--environment=Development" }).ToArray(), b =>
        {
            void Padrao(string k, string v) { if (string.IsNullOrEmpty(b.Configuration[k])) b.Configuration[k] = v; }
            Padrao("Jwt:Key", "chave-de-teste-apenas-para-o-banco-falso-123456");
            Padrao("Urls", "http://localhost:3000");
            Padrao("Plataforma:LimiteAuthPorMinuto", "1000");
            Padrao("Plataforma:CustoBcrypt", "8");
            Padrao("Cors:Origins:0", "http://localhost:5500"); Padrao("Cors:Origins:1", "http://127.0.0.1:5500"); Padrao("Cors:Origins:2", "http://localhost:3000");
            b.Services.AddSingleton<IDb>(db);
            b.Services.AddSingleton<IEmail>(email);
        });

        // Só no banco falso: simula o admin do HytechAdmin (aprovar/rejeitar) para testar o site. NUNCA existe na API real.
        app.MapGet("/__dev/ultimo-codigo", (string email_) => Results.Ok(new { codigo = email.Memoria.UltimoCodigo(email_.ToLowerInvariant()) }));
        app.MapPost("/__dev/aprovar", (DevItem i) => { db.Aprovar(i.Tipo, i.Id); return Results.Ok(new { ok = true }); });
        app.MapPost("/__dev/rejeitar", (DevItem i) => { db.Rejeitar(i.Tipo, i.Id, i.Motivo); return Results.Ok(new { ok = true }); });
        app.MapPost("/__dev/completar", async (DevMateria m) =>
        {   // conclui todas as aulas e acerta todas as questões de uma matéria (para testar o certificado)
            var aluno = m.IdAluno ?? 1;
            foreach (var a in db.Aulas.Where(x => x.IdMateria == m.IdMateria).ToList())
                await db.ExecAsync("sp_ConcluirAula", Parametros.P("id_aluno", aluno), Parametros.P("id_aula", a.IdAula));
            foreach (var q in db.Questoes.Where(x => x.IdMateria == m.IdMateria).ToList())
            {
                var ok = db.Alternativas.First(x => x.IdQuestao == q.IdQuestao && x.Correta);
                await db.ExecAsync("sp_ResponderQuestao", Parametros.P("id_aluno", aluno), Parametros.P("id_questao", q.IdQuestao), Parametros.P("id_alternativa", ok.Id));
            }
            return Results.Ok(new { ok = true });
        });

        app.Logger.LogInformation("API (banco FALSO) no ar — contas: aluno@hytech.com, professor@hytech.com; senha: senha1234");
        await app.RunAsync();
    }

    record DevItem(string Tipo, int Id, string? Motivo);
    record DevMateria(int IdMateria, int? IdAluno);
}
