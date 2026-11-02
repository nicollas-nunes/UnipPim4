const express = require('express');
const cors = require('cors');
const { criarAuth } = require('./auth');
const { ServicoError, sql } = require('./db');

/** Monta o app Express. `db` é injetado para facilitar os testes (banco falso). */
function criarApp({ cfg, db }) {
  const app = express();
  const auth = criarAuth(cfg);

  app.disable('x-powered-by');
  app.use(cors({ origin: cfg.corsOrigens, methods: ['GET', 'POST', 'PUT', 'DELETE'] }));
  app.use(express.json({ limit: '5mb' })); // 5mb: foto de perfil em base64 (fase futura)

  app.get('/', (_req, res) => res.json({ api: 'HytechApi', status: 'ok' }));

  const ctx = { db, auth, sql, cfg };
  app.use('/auth', require('./routes/auth')(ctx));
  app.use('/', require('./routes/plataforma')(ctx));

  app.use((_req, res) => res.status(404).json({ erro: 'Rota não encontrada.' }));

  // Erros: regra do banco -> 400 com a mensagem; o resto vira 500 sem detalhes.
  // (Express 5 já repassa erros de rotas async para cá.)
  // eslint-disable-next-line no-unused-vars
  app.use((err, _req, res, _next) => {
    if (err instanceof ServicoError) return res.status(400).json({ erro: err.message });
    if (err?.type === 'entity.parse.failed') return res.status(400).json({ erro: 'JSON inválido.' });
    console.error(err);
    res.status(500).json({ erro: 'Erro interno. Tente novamente.' });
  });

  return app;
}

module.exports = { criarApp };
