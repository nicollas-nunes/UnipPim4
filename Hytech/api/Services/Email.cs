using System.Net;
using System.Net.Mail;

namespace HytechApi.Services;

/// <summary>Envio de e-mail (códigos de verificação).</summary>
public interface IEmail
{
    Task EnviarAsync(string para, string assunto, string texto, string html);
}

/// <summary>Modo console: NÃO envia nada, escreve o e-mail (com o código) no terminal. Só para desenvolvimento/demonstração.</summary>
public sealed class EmailConsole : IEmail
{
    public Task EnviarAsync(string para, string assunto, string texto, string html)
    {
        var linhas = string.Join("\n", texto.Split('\n').Select(l => "│ " + l));
        Console.WriteLine($"\n┌─ E-MAIL (modo console: nada foi enviado de verdade) ─────────\n│ Para:    {para}\n│ Assunto: {assunto}\n│\n{linhas}\n└──────────────────────────────────────────────────────────────\n");
        return Task.CompletedTask;
    }
}

/// <summary>Envia de verdade por SMTP (Gmail, Outlook, SendGrid...). Configurado em Email:* (ver docs/EMAIL_GMAIL.md).</summary>
public sealed class EmailSmtp : IEmail
{
    private readonly EmailOpcoes _o;
    public EmailSmtp(Opcoes opc) => _o = opc.Email;

    public async Task EnviarAsync(string para, string assunto, string texto, string html)
    {
        using var msg = new MailMessage { From = new MailAddress(_o.De), Subject = assunto, Body = texto, IsBodyHtml = false };
        msg.To.Add(para);
        msg.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(html, null, "text/html"));

        using var smtp = new SmtpClient(_o.Host, _o.Porta)
        {
            EnableSsl = _o.Ssl,
            Credentials = new NetworkCredential(_o.Usuario, _o.Senha),
            Timeout = 15000
        };
        await smtp.SendMailAsync(msg);
    }
}
