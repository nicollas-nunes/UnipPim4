// Códigos de verificação por e-mail: gera, guarda (só o hash), envia e confere.
const crypto = require('crypto');
const { ServicoError } = require('./db');

const ASSUNTOS = {
  cadastro:  { titulo: 'Confirme seu cadastro', frase: 'Use o código abaixo para confirmar seu cadastro na HyTech.' },
  recuperar: { titulo: 'Redefinir sua senha', frase: 'Use o código abaixo para criar uma nova senha na HyTech.' },
  senha:     { titulo: 'Confirme a troca de senha', frase: 'Use o código abaixo para confirmar a troca da sua senha na HyTech.' },
  email:     { titulo: 'Confirme seu novo e-mail', frase: 'Use o código abaixo para confirmar este novo e-mail na sua conta HyTech.' },
};

function esc(s) { return String(s).replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c])); }

function criarVerificacao({ db, cfg, email }) {
  // HMAC com a chave do servidor: um vazamento da tabela não revela os códigos (e eles expiram em minutos).
  const hash = (finalidade, chave, codigo) =>
    crypto.createHmac('sha256', cfg.jwt.key).update(`${finalidade}|${chave}|${codigo}`).digest('hex');

  const chaveEmail = (e) => 'e:' + String(e).trim().toLowerCase();
  const chaveUsuario = (id) => 'u:' + id;

  /** Cria o código no banco e envia por e-mail. */
  async function emitir({ finalidade, chave, id_usuario = null, dados = null, para, nome = '' }) {
    const codigo = String(crypto.randomInt(0, 1000000)).padStart(6, '0');
    await db.exec('sp_CriarCodigoVerificacao', {
      finalidade, chave, id_usuario, codigo_hash: hash(finalidade, chave, codigo),
      dados: dados ? JSON.stringify(dados) : null, minutos: cfg.codigoMinutos,
    });
    const t = ASSUNTOS[finalidade];
    const ola = nome ? `Olá, ${nome}!` : 'Olá!';
    const texto = `${ola}\n\n${t.frase}\n\n    ${codigo}\n\nEle vale por ${cfg.codigoMinutos} minutos. Se não foi você, ignore este e-mail: nada será alterado.\n\n— Equipe HyTech`;
    const html = `<div style="font-family:Arial,sans-serif;max-width:480px;margin:auto;padding:24px;border:1px solid #e5e0f5;border-radius:12px">
<h2 style="color:#5b21b6;margin:0 0 12px">${esc(t.titulo)}</h2><p>${esc(ola)}</p><p>${esc(t.frase)}</p>
<p style="font-size:32px;letter-spacing:8px;font-weight:700;background:#f5f3ff;padding:14px;text-align:center;border-radius:8px">${codigo}</p>
<p style="color:#555;font-size:13px">Ele vale por ${cfg.codigoMinutos} minutos. Se não foi você, ignore este e-mail: nada será alterado.</p></div>`;
    try {
      await email.enviar({ para, assunto: `HyTech — ${t.titulo}`, texto, html });
    } catch (e) {
      console.error('Falha ao enviar e-mail:', e.message);
      const err = new Error('Não foi possível enviar o e-mail agora. Tente novamente em instantes.');
      err.status = 503;
      throw err;
    }
  }

  /** Confere o código. Devolve { id_usuario, dados } e já o consome. Erro genérico para não ajudar adivinhação. */
  async function conferir({ finalidade, chave, codigo }) {
    if (!/^\d{6}$/.test(String(codigo || ''))) throw new ServicoError('Digite o código de 6 dígitos.');
    const row = (await db.exec('sp_ObterCodigoVerificacao', { finalidade, chave }))[0];
    if (!row) throw new ServicoError('Código inválido ou expirado. Peça um novo código.');
    const esperado = Buffer.from(row.codigo_hash, 'hex');
    const recebido = Buffer.from(hash(finalidade, chave, codigo), 'hex');
    if (esperado.length !== recebido.length || !crypto.timingSafeEqual(esperado, recebido)) {
      await db.exec('sp_RegistrarTentativaCodigo', { id_codigo: row.id_codigo });
      throw new ServicoError('Código incorreto. Confira e tente de novo.');
    }
    await db.exec('sp_ConsumirCodigo', { id_codigo: row.id_codigo });   // uso único
    return { id_usuario: row.id_usuario, dados: row.dados ? JSON.parse(row.dados) : null };
  }

  /** Dados do último código pendente (mesmo expirado): serve para REENVIAR sem pedir tudo de novo. */
  async function pendente({ finalidade, chave }) {
    const row = (await db.exec('sp_ObterCodigoVerificacao', { finalidade, chave, incluir_expirados: 1 }))[0];
    return row ? { id_usuario: row.id_usuario, dados: row.dados ? JSON.parse(row.dados) : null } : null;
  }

  return { emitir, conferir, pendente, chaveEmail, chaveUsuario };
}

/** "joao.silva@gmail.com" -> "j***@gmail.com" (para mostrar na tela sem expor o e-mail inteiro) */
function mascararEmail(e) {
  const [u, d] = String(e).split('@');
  return (u ? u[0] : '') + '***@' + (d || '');
}

module.exports = { criarVerificacao, mascararEmail };
