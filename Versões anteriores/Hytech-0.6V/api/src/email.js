// Envio de e-mail. Dois modos:
//   console -> NÃO envia nada: escreve o e-mail (com o código) no terminal. Só para desenvolvimento/testes.
//   smtp    -> envia de verdade (Gmail, Outlook, SendGrid...) com nodemailer. Configurado no .env (SMTP_*).
function criarEmail(cfg) {
  const c = cfg.email;

  if (c.modo === 'smtp') {
    const nodemailer = require('nodemailer');
    const transporte = nodemailer.createTransport({
      host: c.host, port: c.porta, secure: c.seguro,
      auth: { user: c.usuario, pass: c.senha },
      connectionTimeout: 10000, greetingTimeout: 10000, socketTimeout: 15000,
    });
    return {
      modo: 'smtp',
      async enviar({ para, assunto, texto, html }) {
        await transporte.sendMail({ from: c.de, to: para, subject: assunto, text: texto, html });
      },
    };
  }

  return {
    modo: 'console',
    async enviar({ para, assunto, texto }) {
      console.log(`\n┌─ E-MAIL (modo console: nada foi enviado de verdade) ─────────\n│ Para:    ${para}\n│ Assunto: ${assunto}\n│\n${texto.split('\n').map(l => '│ ' + l).join('\n')}\n└──────────────────────────────────────────────────────────────\n`);
    },
  };
}

/** E-mail guardado na memória (testes): `enviados` lista tudo o que seria enviado. */
function criarEmailMemoria() {
  const enviados = [];
  return {
    modo: 'memoria', enviados,
    async enviar(msg) { enviados.push({ ...msg, em: Date.now() }); },
    /** Último código de 6 dígitos enviado para este endereço. */
    ultimoCodigo(para) {
      const m = enviados.filter(e => e.para === para).pop();
      return m ? /\b(\d{6})\b/.exec(m.texto)[1] : null;
    },
  };
}

module.exports = { criarEmail, criarEmailMemoria };
