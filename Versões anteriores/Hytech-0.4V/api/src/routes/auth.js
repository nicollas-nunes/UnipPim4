const express = require('express');
const rateLimit = require('express-rate-limit');
const bcrypt = require('bcryptjs');

// Hash de mentira (válido), só para gastar o mesmo tempo mesmo quando o e-mail não existe.
const HASH_FALSO = bcrypt.hashSync('hytech-hash-falso', 11);

const EMAIL_RE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

function dataValida(iso) {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(iso)) return null;
  const d = new Date(iso + 'T00:00:00Z');
  if (Number.isNaN(d.getTime()) || d.toISOString().slice(0, 10) !== iso) return null;
  if (d > new Date() || d.getUTCFullYear() < 1900) return null;
  return d;
}

module.exports = function rotasAuth({ db, auth, sql, cfg }) {
  const r = express.Router();
  const limite = rateLimit({
    windowMs: 60 * 1000, limit: cfg.limiteAuthPorMinuto, standardHeaders: true, legacyHeaders: false,
    message: { erro: 'Muitas tentativas. Aguarde um minuto.' },
  });

  // ── Login: sp_ObterUsuarioParaLogin + bcrypt + sp_RegistrarLogin ──
  r.post('/login', limite, async (req, res) => {
    const email = (req.body?.email || '').trim();
    const senha = req.body?.senha || '';
    if (!email || !senha) return res.status(400).json({ erro: 'Informe e-mail e senha.' });

    const u = (await db.exec('sp_ObterUsuarioParaLogin', { email }))[0];

    // Sempre confere um hash e responde igual: não revela se o e-mail existe.
    const ok = await bcrypt.compare(senha, u?.senha_hash || HASH_FALSO);
    if (!u || !ok) return res.status(401).json({ erro: 'E-mail ou senha incorretos.' });

    if (u.status !== 'ativo') return res.status(403).json({ erro: 'Conta inativa. Fale com o administrador.' });
    // O admin usa o aplicativo desktop (WinForms). A web é só aluno e professor.
    if (u.papel === 'admin') {
      return res.status(403).json({ erro: 'Administradores acessam pelo aplicativo HytechAdmin (Windows).' });
    }
    if (!u.papel) return res.status(403).json({ erro: 'Conta sem perfil definido.' });

    await db.exec('sp_RegistrarLogin', { id_usuario: u.id_usuario }); // log + streak do aluno

    const { token, expiraEm } = auth.criarToken(u);
    res.json({
      token, expiraEm,
      usuario: {
        id: u.id_usuario, nome: u.nome, nickname: u.nickname, email: u.email,
        papel: u.papel, foto: u.foto_url || null,
        id_aluno: u.id_aluno ?? null, id_professor: u.id_professor ?? null,
      },
    });
  });

  // ── Cadastro público: SOMENTE aluno. Professor é criado pelo admin (WinForms). ──
  r.post('/cadastro', limite, async (req, res) => {
    const nome = (req.body?.nome || '').trim();
    const nick = (req.body?.nickname || '').trim();
    const email = (req.body?.email || '').trim();
    const senha = req.body?.senha || '';
    const nasc = req.body?.dataNascimento || null; // 'AAAA-MM-DD' (opcional)

    const erro = (m) => res.status(400).json({ erro: m });
    if (nome.length === 0 || nome.length > 100) return erro('Informe o nome (até 100 caracteres).');
    if (nick.length === 0 || nick.length > 49) return erro('Informe o nome de usuário (até 49 caracteres).');
    if (!EMAIL_RE.test(email) || email.length > 100) return erro('E-mail inválido.');
    if (senha.length < 8) return erro('A senha precisa ter pelo menos 8 caracteres.');
    let dNasc = null;
    if (nasc) {
      dNasc = dataValida(nasc);
      if (!dNasc) return erro('Data de nascimento inválida.');
    }

    const hash = await bcrypt.hash(senha, 11);
    const ids = (await db.exec('sp_CadastrarAluno', {
      nome, nickname: nick, email, senha_hash: hash, data_nascimento: dNasc,
    }, { data_nascimento: sql.Date }))[0];

    res.status(201).json({ id_usuario: ids.id_usuario, id_aluno: ids.id_aluno });
  });

  // ── Quem sou eu (valida o token) ──
  r.get('/me', auth.exigirLogin, (req, res) => {
    const { id_usuario, nome, email, papel, id_aluno, id_professor } = req.usuario;
    res.json({ id: id_usuario, nome, email, papel, id_aluno, id_professor });
  });

  return r;
};
