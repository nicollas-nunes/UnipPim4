// Sobe a API com um BANCO FALSO em memória (sem SQL Server) para testar o site.
//   npm run dev:falso      -> API em http://localhost:3000
// Contas de teste: aluno@hytech.com / professor@hytech.com / admin@hytech.com (admin é bloqueado na web)
// Senha: senha1234
const { criarApp } = require('../src/app');
const { carregarConfig } = require('../src/config');
const { criarBancoFalso } = require('./banco-falso');

const cfg = carregarConfig({
  JWT_KEY: 'chave-de-teste-apenas-para-o-banco-falso-123456',
  CORS_ORIGINS: process.env.CORS_ORIGINS || 'http://localhost:5500,http://127.0.0.1:5500',
  PORT: process.env.PORT || '3000',
  AUTH_LIMITE_POR_MINUTO: '1000',
  SERVIR_SITE: process.env.SERVIR_SITE || '',
});
const express = require('express');
const db = criarBancoFalso({ custoHash: 8 });
const app = criarApp({ cfg, db });
// Só no banco falso: simula o admin do WinForms (aprovar/rejeitar) para testar o site. NUNCA existe na API real.
const dev = express();
dev.use('/__dev', express.json(), (req, res) => {
  const { tipo, id, motivo } = req.body || {};
  if (req.path === '/aprovar') db.aprovar(tipo, Number(id));
  else if (req.path === '/rejeitar') db.rejeitar(tipo, Number(id), motivo);
  else return res.status(404).json({ erro: 'x' });
  res.json({ ok: true });
});
dev.use(app);
dev.listen(cfg.porta, () => console.log('API (banco FALSO) em http://localhost:' + cfg.porta));
