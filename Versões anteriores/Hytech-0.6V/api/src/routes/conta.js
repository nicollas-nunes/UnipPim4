const express = require('express');
const bcrypt = require('bcryptjs');
const rateLimit = require('express-rate-limit');
const { mascararEmail } = require('../verificacao');

const EMAIL_RE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
const FOTO_MAX = 1_000_000; // ~1 MB de texto (o site reduz a foto para ~256px antes de enviar)

// Dados da própria conta. Serve aluno E professor (admin nem chega aqui: não entra pela web).
module.exports = function rotasConta({ db, auth, verif, cfg }) {
  const r = express.Router();
  r.use(auth.exigirLogin);
  // Pedidos de código / tentativas de confirmação: limite por IP (o banco ainda limita por conta).
  const limite = rateLimit({ windowMs: 60 * 1000, limit: cfg.limiteAuthPorMinuto, standardHeaders: true, legacyHeaders: false,
    message: { erro: 'Muitas tentativas. Aguarde um minuto.' } });

  async function usuarioAtual(req) {
    return (await db.exec('sp_ObterUsuarioParaLogin', { email: req.usuario.email }))[0];
  }
  // Resposta padrão após mudar nome/e-mail: perfil novo + token NOVO (o token carrega nome/e-mail).
  async function respostaPerfil(emailAtual) {
    const u = (await db.exec('sp_ObterUsuarioParaLogin', { email: emailAtual.toLowerCase() }))[0];
    const { token, expiraEm } = auth.criarToken(u);
    return { perfil: { nome: u.nome, nickname: u.nickname, email: u.email, foto: u.foto_url || null }, token, expiraEm };
  }

  // PUT /conta/perfil   { nome?, nickname?, email?, foto? }   (o que não vier não muda)
  r.put('/perfil', async (req, res) => {
    const b = req.body || {};
    const erro = (m) => res.status(400).json({ erro: m });
    const nome = b.nome == null ? null : String(b.nome).trim();
    const nick = b.nickname == null ? null : String(b.nickname).trim();
    const email = b.email == null ? null : String(b.email).trim();
    const foto = b.foto == null ? null : String(b.foto);

    if (nome !== null && (nome.length < 1 || nome.length > 100)) return erro('Informe o nome (até 100 caracteres).');
    if (nick !== null && (nick.length < 1 || nick.length > 49)) return erro('Informe o nome de usuário (até 49 caracteres).');
    if (email !== null && (!EMAIL_RE.test(email) || email.length > 100)) return erro('E-mail inválido.');
    if (foto !== null) {
      if (!/^data:image\/(png|jpe?g|webp);base64,/i.test(foto)) return erro('Foto inválida (use PNG, JPG ou WEBP).');
      if (foto.length > FOTO_MAX) return erro('Foto grande demais.');
    }
    // O e-mail NÃO muda por aqui: precisa de confirmação por código (POST /conta/email).
    if (email !== null && email.toLowerCase() !== req.usuario.email.toLowerCase()) {
      return erro('Para trocar o e-mail é preciso confirmar com um código enviado ao novo endereço.');
    }
    if (nome === null && nick === null && foto === null) return erro('Nada para alterar.');

    await db.exec('sp_AtualizarPerfil', {
      id_usuario: req.usuario.id_usuario, nome, nickname: nick, foto_url: foto,
    });
    res.json(await respostaPerfil(req.usuario.email));
  });

  // ── Trocar senha (2 passos) ──
  // POST /conta/senha            { senhaAtual, novaSenha, confirmarSenha } -> confere a senha atual e manda um código ao e-mail
  // POST /conta/senha/confirmar  { codigo }                                -> troca a senha
  r.post('/senha', limite, async (req, res) => {
    const atual = req.body?.senhaAtual || '';
    const nova = req.body?.novaSenha || '';
    if (!atual) return res.status(400).json({ erro: 'Informe a senha atual.' });
    if (nova.length < 8) return res.status(400).json({ erro: 'A nova senha precisa ter pelo menos 8 caracteres.' });
    if (req.body?.confirmarSenha !== undefined && req.body.confirmarSenha !== nova) {
      return res.status(400).json({ erro: 'A confirmação da nova senha não confere.' });
    }
    if (nova === atual) return res.status(400).json({ erro: 'A nova senha deve ser diferente da atual.' });

    const u = await usuarioAtual(req);
    if (!u || !(await bcrypt.compare(atual, u.senha_hash))) {
      return res.status(400).json({ erro: 'Senha atual incorreta.' });
    }
    await verif.emitir({
      finalidade: 'senha', chave: verif.chaveUsuario(u.id_usuario), id_usuario: u.id_usuario,
      dados: { senha_hash: await bcrypt.hash(nova, 11) }, para: u.email, nome: u.nome,
    });
    res.json({ codigoEnviado: true, email: mascararEmail(u.email), minutos: cfg.codigoMinutos });
  });

  r.post('/senha/confirmar', limite, async (req, res) => {
    const { dados } = await verif.conferir({ finalidade: 'senha', chave: verif.chaveUsuario(req.usuario.id_usuario), codigo: req.body?.codigo });
    await db.exec('sp_AlterarSenha', { id_usuario: req.usuario.id_usuario, senha_hash: dados.senha_hash });
    res.json({ ok: true });
  });

  // ── Trocar e-mail (2 passos): o código vai para o NOVO endereço ──
  // POST /conta/email            { novoEmail, senha }
  // POST /conta/email/confirmar  { codigo }  -> devolve perfil + token novos
  r.post('/email', limite, async (req, res) => {
    const novo = String(req.body?.novoEmail || '').trim().toLowerCase();
    const erro = (m) => res.status(400).json({ erro: m });
    if (!EMAIL_RE.test(novo) || novo.length > 100) return erro('E-mail inválido.');
    if (novo === req.usuario.email.toLowerCase()) return erro('Este já é o seu e-mail atual.');
    const u = await usuarioAtual(req);
    if (!u || !(await bcrypt.compare(req.body?.senha || '', u.senha_hash))) return erro('Senha incorreta.');
    const disp = (await db.exec('sp_VerificarDisponibilidade', { email: novo, id_usuario_ignorar: u.id_usuario }))[0];
    if (disp?.email_em_uso) return erro('Este e-mail já está em uso.');

    await verif.emitir({
      finalidade: 'email', chave: verif.chaveUsuario(u.id_usuario), id_usuario: u.id_usuario,
      dados: { novo_email: novo }, para: novo, nome: u.nome,
    });
    res.json({ codigoEnviado: true, email: mascararEmail(novo), minutos: cfg.codigoMinutos });
  });

  r.post('/email/confirmar', limite, async (req, res) => {
    const { dados } = await verif.conferir({ finalidade: 'email', chave: verif.chaveUsuario(req.usuario.id_usuario), codigo: req.body?.codigo });
    await db.exec('sp_AtualizarPerfil', { id_usuario: req.usuario.id_usuario, email: dados.novo_email });
    res.json(await respostaPerfil(dados.novo_email));
  });

  // POST /conta/reenviar { finalidade: 'senha' | 'email' } -> manda o código de novo (mesmos dados)
  r.post('/reenviar', limite, async (req, res) => {
    const finalidade = req.body?.finalidade;
    if (!['senha', 'email'].includes(finalidade)) return res.status(400).json({ erro: 'Finalidade inválida.' });
    const chave = verif.chaveUsuario(req.usuario.id_usuario);
    const p = await verif.pendente({ finalidade, chave });
    if (!p) return res.status(400).json({ erro: 'Nenhuma troca em andamento. Comece de novo.' });
    const para = finalidade === 'email' ? p.dados.novo_email : req.usuario.email;
    await verif.emitir({ finalidade, chave, id_usuario: req.usuario.id_usuario, dados: p.dados, para, nome: req.usuario.nome });
    res.json({ codigoEnviado: true, email: mascararEmail(para), minutos: cfg.codigoMinutos });
  });

  // ── Desativar a própria conta (LGPD: nada é apagado; o admin pode reativar) ──
  // POST /conta/desativar { nickname, senha }   (só aluno; professor fala com o administrador)
  r.post('/desativar', limite, async (req, res) => {
    if (req.usuario.papel !== 'aluno') {
      return res.status(403).json({ erro: 'Professores devem pedir a desativação da conta ao administrador.' });
    }
    const norm = (n) => String(n || '').trim().replace(/^@/, '').toLowerCase();
    const u = await usuarioAtual(req);
    if (!u || norm(req.body?.nickname) !== norm(u.nickname)) {
      return res.status(400).json({ erro: 'O nome de usuário informado não confere.' });
    }
    if (!(await bcrypt.compare(req.body?.senha || '', u.senha_hash))) {
      return res.status(400).json({ erro: 'Senha incorreta.' });
    }
    await db.exec('sp_DesativarConta', { id_usuario: u.id_usuario });
    res.json({ ok: true });
  });

  return r;
};
