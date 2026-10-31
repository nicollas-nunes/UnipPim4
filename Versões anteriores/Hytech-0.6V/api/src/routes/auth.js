const express = require('express');
const rateLimit = require('express-rate-limit');
const bcrypt = require('bcryptjs');
const { ServicoError } = require('../db');

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

const { mascararEmail } = require('../verificacao');

module.exports = function rotasAuth({ db, auth, sql, cfg, verif }) {
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

    if (u.status !== 'ativo') return res.status(403).json({ erro: 'Conta desativada. Para reativar, fale com o administrador.' });
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
  // Passo 1: valida e manda um código por e-mail. A conta só é criada no passo 2 (confirmar).
  r.post('/cadastro', limite, async (req, res) => {
    const nome = (req.body?.nome || '').trim();
    const nick = (req.body?.nickname || '').trim();
    const email = (req.body?.email || '').trim().toLowerCase();
    const senha = req.body?.senha || '';
    const nasc = req.body?.dataNascimento || null; // 'AAAA-MM-DD' (opcional)

    const erro = (m) => res.status(400).json({ erro: m });
    if (nome.length === 0 || nome.length > 100) return erro('Informe o nome (até 100 caracteres).');
    if (nick.length === 0 || nick.length > 49) return erro('Informe o nome de usuário (até 49 caracteres).');
    if (!EMAIL_RE.test(email) || email.length > 100) return erro('E-mail inválido.');
    if (senha.length < 8) return erro('A senha precisa ter pelo menos 8 caracteres.');
    if (nasc && !dataValida(nasc)) return erro('Data de nascimento inválida.');

    const disp = (await db.exec('sp_VerificarDisponibilidade', { email, nickname: nick }))[0];
    if (disp?.email_em_uso) return erro('Este e-mail já está cadastrado.');
    if (disp?.nickname_em_uso) return erro('Este nome de usuário já está em uso.');

    await verif.emitir({
      finalidade: 'cadastro', chave: verif.chaveEmail(email), para: email, nome,
      dados: { nome, nickname: nick, senha_hash: await bcrypt.hash(senha, 11), data_nascimento: nasc },
    });
    res.status(202).json({ verificacao: 'pendente', email: mascararEmail(email), minutos: cfg.codigoMinutos });
  });

  // Passo 2: confere o código e cria a conta.
  r.post('/cadastro/confirmar', limite, async (req, res) => {
    const email = (req.body?.email || '').trim().toLowerCase();
    const { dados } = await verif.conferir({ finalidade: 'cadastro', chave: verif.chaveEmail(email), codigo: req.body?.codigo });
    const ids = (await db.exec('sp_CadastrarAluno', {
      nome: dados.nome, nickname: dados.nickname, email, senha_hash: dados.senha_hash,
      data_nascimento: dados.data_nascimento ? new Date(dados.data_nascimento + 'T00:00:00Z') : null,
    }, { data_nascimento: sql.Date }))[0];
    res.status(201).json({ id_usuario: ids.id_usuario, id_aluno: ids.id_aluno });
  });

  r.post('/cadastro/reenviar', limite, async (req, res) => {
    const email = (req.body?.email || '').trim().toLowerCase();
    const chave = verif.chaveEmail(email);
    const p = await verif.pendente({ finalidade: 'cadastro', chave });
    if (!p) return res.status(400).json({ erro: 'Cadastro não encontrado ou expirado. Preencha o formulário de novo.' });
    await verif.emitir({ finalidade: 'cadastro', chave, para: email, nome: p.dados.nome, dados: p.dados });
    res.json({ verificacao: 'pendente', email: mascararEmail(email), minutos: cfg.codigoMinutos });
  });

  // ── Esqueci a senha (aluno e professor) ──
  // Sempre responde igual: não revela se o e-mail existe.
  r.post('/esqueci', limite, async (req, res) => {
    const email = (req.body?.email || '').trim().toLowerCase();
    if (!EMAIL_RE.test(email)) return res.status(400).json({ erro: 'E-mail inválido.' });
    const u = (await db.exec('sp_ObterUsuarioParaLogin', { email }))[0];
    if (u && u.status === 'ativo' && (u.papel === 'aluno' || u.papel === 'professor')) {
      try {
        await verif.emitir({ finalidade: 'recuperar', chave: verif.chaveEmail(email), id_usuario: u.id_usuario, para: email, nome: u.nome });
      } catch (e) { if (!(e instanceof ServicoError)) throw e; }   // limite de pedidos: mesma resposta
    }
    res.json({ ok: true, mensagem: 'Se este e-mail tiver uma conta, enviamos um código para ele.', minutos: cfg.codigoMinutos });
  });

  r.post('/esqueci/redefinir', limite, async (req, res) => {
    const email = (req.body?.email || '').trim().toLowerCase();
    const nova = req.body?.novaSenha || '';
    if (nova.length < 8) return res.status(400).json({ erro: 'A nova senha precisa ter pelo menos 8 caracteres.' });
    const { id_usuario } = await verif.conferir({ finalidade: 'recuperar', chave: verif.chaveEmail(email), codigo: req.body?.codigo });
    await db.exec('sp_AlterarSenha', { id_usuario, senha_hash: await bcrypt.hash(nova, 11) });
    res.json({ ok: true });
  });

  // ── Quem sou eu (valida o token) ──
  r.get('/me', auth.exigirLogin, (req, res) => {
    const { id_usuario, nome, email, papel, id_aluno, id_professor } = req.usuario;
    res.json({ id: id_usuario, nome, email, papel, id_aluno, id_professor });
  });

  return r;
};
