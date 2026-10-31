const express = require('express');
const bcrypt = require('bcryptjs');

const EMAIL_RE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
const FOTO_MAX = 1_000_000; // ~1 MB de texto (o site reduz a foto para ~256px antes de enviar)

// Dados da própria conta. Serve aluno E professor (admin nem chega aqui: não entra pela web).
module.exports = function rotasConta({ db, auth }) {
  const r = express.Router();
  r.use(auth.exigirLogin);

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
    if (nome === null && nick === null && email === null && foto === null) return erro('Nada para alterar.');

    await db.exec('sp_AtualizarPerfil', {
      id_usuario: req.usuario.id_usuario, nome, nickname: nick, email, foto_url: foto,
    });

    // Lê o usuário já normalizado pelo banco (e-mail minúsculo, nickname com '@') e emite um token NOVO:
    // o token carrega nome/e-mail, então precisa refletir a mudança.
    const emailAtual = (email || req.usuario.email).toLowerCase();
    const u = (await db.exec('sp_ObterUsuarioParaLogin', { email: emailAtual }))[0];
    const { token, expiraEm } = auth.criarToken(u);
    res.json({
      perfil: { nome: u.nome, nickname: u.nickname, email: u.email, foto: u.foto_url || null },
      token, expiraEm,
    });
  });

  // POST /conta/senha   { senhaAtual, novaSenha }
  r.post('/senha', async (req, res) => {
    const atual = req.body?.senhaAtual || '';
    const nova = req.body?.novaSenha || '';
    if (!atual) return res.status(400).json({ erro: 'Informe a senha atual.' });
    if (nova.length < 8) return res.status(400).json({ erro: 'A nova senha precisa ter pelo menos 8 caracteres.' });

    const u = (await db.exec('sp_ObterUsuarioParaLogin', { email: req.usuario.email }))[0];
    if (!u || !(await bcrypt.compare(atual, u.senha_hash))) {
      return res.status(400).json({ erro: 'Senha atual incorreta.' });
    }
    await db.exec('sp_AlterarSenha', { id_usuario: req.usuario.id_usuario, senha_hash: await bcrypt.hash(nova, 11) });
    res.json({ ok: true });
  });

  return r;
};
