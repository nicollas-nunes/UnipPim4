// JWT + middlewares de proteção por papel (aluno / professor).
const jwt = require('jsonwebtoken');

function criarAuth(cfg) {
  function criarToken(u) {
    // O token carrega os ids. As rotas leem id_aluno/id_professor DAQUI, nunca do corpo da requisição.
    const payload = {
      nome: u.nome,
      nickname: u.nickname,
      email: u.email,
      papel: u.papel,
      id_aluno: u.id_aluno ?? null,
      id_professor: u.id_professor ?? null,
    };
    const token = jwt.sign(payload, cfg.jwt.key, {
      subject: String(u.id_usuario),
      issuer: cfg.jwt.issuer,
      audience: cfg.jwt.audience,
      expiresIn: cfg.jwt.expiraMinutos * 60,
      algorithm: 'HS256',
    });
    const expiraEm = new Date(Date.now() + cfg.jwt.expiraMinutos * 60000).toISOString();
    return { token, expiraEm };
  }

  function exigirLogin(req, res, next) {
    const h = req.headers.authorization || '';
    const m = /^Bearer (.+)$/i.exec(h);
    if (!m) return res.status(401).json({ erro: 'Faça login para continuar.' });
    try {
      const d = jwt.verify(m[1], cfg.jwt.key, {
        issuer: cfg.jwt.issuer, audience: cfg.jwt.audience, algorithms: ['HS256'],
      });
      req.usuario = {
        id_usuario: Number(d.sub), nome: d.nome, nickname: d.nickname, email: d.email, papel: d.papel,
        id_aluno: d.id_aluno, id_professor: d.id_professor,
      };
      next();
    } catch {
      res.status(401).json({ erro: 'Sessão expirada. Faça login novamente.' });
    }
  }

  const exigirPapel = (...papeis) => (req, res, next) =>
    papeis.includes(req.usuario?.papel) ? next() : res.status(403).json({ erro: 'Acesso negado para este perfil.' });

  return { criarToken, exigirLogin, exigirPapel };
}

module.exports = { criarAuth };
