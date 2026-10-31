const express = require('express');
const cors = require('cors');
const helmet = require('helmet');
const rateLimit = require('express-rate-limit');
const { criarAuth } = require('./auth');
const { ServicoError, sql } = require('./db');

/** Monta o app Express. `db` é injetado para facilitar os testes (banco falso). */
function criarApp({ cfg, db }) {
  const app = express();
  const auth = criarAuth(cfg);

  app.disable('x-powered-by');
  if (cfg.trustProxy) app.set('trust proxy', cfg.trustProxy);   // atrás de Nginx/IIS: o IP real vem do proxy

  // Cabeçalhos de segurança. O site (index.html) usa scripts/estilos embutidos e a fonte do Google.
  app.use(helmet({
    contentSecurityPolicy: {
      useDefaults: true,
      directives: {
        'script-src': ["'self'", "'unsafe-inline'"],
        'script-src-attr': ["'unsafe-inline'"],            // onclick="..." do site
        'style-src': ["'self'", "'unsafe-inline'", 'https://fonts.googleapis.com'],
        'font-src': ["'self'", 'https://fonts.gstatic.com'],
        'img-src': ["'self'", 'data:'],
        'connect-src': ["'self'"],
        'frame-ancestors': ["'none'"],
        'upgrade-insecure-requests': cfg.producao ? [] : null,
      },
    },
    crossOriginResourcePolicy: { policy: 'cross-origin' },  // a API é chamada de outra origem (CORS controla)
  }));

  if (cfg.logRequisicoes) {   // só método, caminho (sem query), status e tempo: nunca corpo, token ou e-mail
    app.use((req, res, next) => {
      const t = Date.now();
      res.on('finish', () => console.log(`${new Date().toISOString()} ${req.method} ${req.path} ${res.statusCode} ${Date.now() - t}ms`));
      next();
    });
  }

  app.use(cors({ origin: cfg.corsOrigens, methods: ['GET', 'POST', 'PUT', 'DELETE'] }));
  app.use(rateLimit({ windowMs: 60 * 1000, limit: cfg.limiteGeralPorMinuto, standardHeaders: true, legacyHeaders: false,
    message: { erro: 'Muitas requisições. Aguarde um instante.' } }));
  // Corpo pequeno por padrão; só a foto de perfil (base64) precisa de mais.
  app.use('/conta/perfil', express.json({ limit: '1500kb' }));
  app.use(express.json({ limit: '100kb' }));

  app.get('/saude', (_req, res) => res.json({ status: 'ok' }));
  if (!cfg.siteDir) app.get('/', (_req, res) => res.json({ api: 'HytechApi', status: 'ok' }));

  const ctx = { db, auth, sql, cfg };
  app.use('/auth', require('./routes/auth')(ctx));
  app.use('/', require('./routes/plataforma')(ctx));
  app.use('/aluno', require('./routes/aluno')(ctx));
  app.use('/professor', require('./routes/professor')(ctx));
  app.use('/conta', require('./routes/conta')(ctx));
  app.use('/tickets', require('./routes/tickets')(ctx));

  if (cfg.siteDir) app.use(express.static(cfg.siteDir, { index: 'index.html', dotfiles: 'ignore', maxAge: cfg.producao ? '1h' : 0 }));

  app.use((_req, res) => res.status(404).json({ erro: 'Rota não encontrada.' }));

  // Erros: regra do banco -> 400 com a mensagem; o resto vira 500 sem detalhes.
  // (Express 5 já repassa erros de rotas async para cá.)
  // eslint-disable-next-line no-unused-vars
  app.use((err, _req, res, _next) => {
    if (err instanceof ServicoError) return res.status(400).json({ erro: err.message });
    if (err?.status === 404) return res.status(404).json({ erro: err.message });
    if (err?.type === 'entity.too.large') return res.status(413).json({ erro: 'Conteúdo grande demais.' });
    if (err?.type === 'entity.parse.failed') return res.status(400).json({ erro: 'JSON inválido.' });
    console.error(err);
    res.status(500).json({ erro: 'Erro interno. Tente novamente.' });
  });

  return app;
}

module.exports = { criarApp };
