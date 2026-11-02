// Fase 6 — segurança e publicação. Banco FALSO. Rodar: npm test
const test = require('node:test');
const assert = require('node:assert');
const path = require('node:path');
const { criarApp } = require('../src/app');
const { carregarConfig } = require('../src/config');
const { criarBancoFalso } = require('./banco-falso');

const base = { JWT_KEY: 'x'.repeat(40), CORS_ORIGINS: 'http://localhost:5500', AUTH_LIMITE_POR_MINUTO: '1000' };
async function subir(extra = {}) {
  const server = criarApp({ cfg: carregarConfig({ ...base, ...extra }), db: criarBancoFalso() }).listen(0);
  const url = `http://127.0.0.1:${server.address().port}`;
  return { url, fechar: () => server.close() };
}

test('cabeçalhos de segurança e sem x-powered-by', async () => {
  const s = await subir();
  const r = await fetch(s.url + '/saude');
  assert.equal(r.status, 200);
  assert.ok(r.headers.get('content-security-policy').includes("frame-ancestors 'none'"));
  assert.equal(r.headers.get('x-content-type-options'), 'nosniff');
  assert.equal(r.headers.get('x-powered-by'), null);
  s.fechar();
});

test('corpo grande demais -> 413; foto de perfil tem limite maior', async () => {
  const s = await subir();
  const grande = JSON.stringify({ email: 'a@b.com', senha: 'x'.repeat(200000) });
  const r = await fetch(s.url + '/auth/login', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: grande });
  assert.equal(r.status, 413);
  const l = await (await fetch(s.url + '/auth/login', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ email: 'aluno@hytech.com', senha: 'senha1234' }) })).json();
  assert.ok(l.token);
  const foto = 'data:image/png;base64,' + 'A'.repeat(900000);
  const p = await fetch(s.url + '/conta/perfil', { method: 'PUT', headers: { 'Content-Type': 'application/json', Authorization: 'Bearer ' + l.token }, body: JSON.stringify({ foto }) });
  assert.notEqual(p.status, 413);
  s.fechar();
});

test('limite geral por IP -> 429', async () => {
  const s = await subir({ LIMITE_GERAL_POR_MINUTO: '5' });
  const codigos = [];
  for (let i = 0; i < 8; i++) codigos.push((await fetch(s.url + '/saude')).status);
  assert.ok(codigos.includes(429), codigos.join());
  s.fechar();
});

test('CORS: só origens permitidas', async () => {
  const s = await subir();
  const ok = await fetch(s.url + '/saude', { headers: { Origin: 'http://localhost:5500' } });
  const ruim = await fetch(s.url + '/saude', { headers: { Origin: 'http://malvado.example' } });
  assert.equal(ok.headers.get('access-control-allow-origin'), 'http://localhost:5500');
  assert.equal(ruim.headers.get('access-control-allow-origin'), null);
  s.fechar();
});

test('produção recusa .env de exemplo', () => {
  const prod = { ...base, NODE_ENV: 'production', DB_USER: 'u', DB_PASSWORD: 'senhaReal#9' };
  assert.doesNotThrow(() => carregarConfig(prod));
  assert.throws(() => carregarConfig({ ...prod, JWT_KEY: 'troque-por-um-texto-aleatorio-bem-longo-123456' }), /JWT_KEY/);
  assert.throws(() => carregarConfig({ ...prod, DB_PASSWORD: 'troque-esta-senha' }), /DB_PASSWORD/);
  assert.throws(() => carregarConfig({ ...prod, DB_USER: '' }), /DB_USER/);
});

test('SERVIR_SITE: a API entrega o site e a API continua em pé', async () => {
  const s = await subir({ SERVIR_SITE: 'true', SITE_DIR: path.join(__dirname, '..', '..', 'web') });
  const h = await fetch(s.url + '/');
  assert.equal(h.status, 200);
  assert.ok((await h.text()).includes('<title>'));
  assert.equal((await fetch(s.url + '/js/api.js')).status, 200);
  assert.equal((await fetch(s.url + '/materias')).status, 401);          // rota da API intacta
  assert.equal((await fetch(s.url + '/nao-existe')).status, 404);
  s.fechar();
});

test('erro interno não vaza detalhes', async () => {
  const db = criarBancoFalso();
  db.exec = async () => { throw new Error('SELECT secreto FROM tabela'); };
  const server = criarApp({ cfg: carregarConfig(base), db }).listen(0);
  const r = await fetch(`http://127.0.0.1:${server.address().port}/auth/login`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ email: 'a@b.com', senha: 'x' }) });
  const t = await r.text();
  assert.equal(r.status, 500);
  assert.ok(!t.includes('secreto'));
  server.close();
});
