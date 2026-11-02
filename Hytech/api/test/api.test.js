// Testes com banco FALSO (sem SQL Server). Rodar: npm test
const test = require('node:test');
const assert = require('node:assert');
const bcrypt = require('bcryptjs');
const { criarApp } = require('../src/app');
const { carregarConfig } = require('../src/config');
const { criarBancoFalso } = require('./banco-falso');

const SENHA = 'senhaforte1';
const cfg = carregarConfig({ JWT_KEY: 'x'.repeat(40), CORS_ORIGINS: 'http://localhost:5500', AUTH_LIMITE_POR_MINUTO: '1000' });

async function subir(opcoes = {}) {
  const db = criarBancoFalso({ senhaPadrao: SENHA });
  const app = criarApp({ cfg: { ...cfg, ...opcoes }, db });
  const server = app.listen(0);
  const base = `http://127.0.0.1:${server.address().port}`;
  const req = async (rota, { metodo = 'GET', corpo, token } = {}) => {
    const r = await fetch(base + rota, {
      method: metodo,
      headers: { 'Content-Type': 'application/json', ...(token ? { Authorization: 'Bearer ' + token } : {}) },
      body: corpo ? JSON.stringify(corpo) : undefined,
    });
    return { status: r.status, json: await r.json() };
  };
  const login = async (email) => (await req('/auth/login', { metodo: 'POST', corpo: { email, senha: SENHA } })).json.token;
  return { req, login, db, fechar: () => server.close() };
}

test('Fase 1 — login, cadastro, config', async (t) => {
  const { req, login, db, fechar } = await subir();
  t.after(fechar);

  await t.test('login ok devolve token e usuário (aluno)', async () => {
    const r = await req('/auth/login', { metodo: 'POST', corpo: { email: 'ALUNO@hytech.com ', senha: SENHA } });
    assert.equal(r.status, 200);
    assert.ok(r.json.token);
    assert.equal(r.json.usuario.papel, 'aluno');
    assert.equal(r.json.usuario.id_aluno, 1);
    assert.ok(db.chamadas.some(c => c[0] === 'sp_RegistrarLogin'));
  });

  await t.test('senha errada e e-mail inexistente dão a MESMA resposta 401', async () => {
    const a = await req('/auth/login', { metodo: 'POST', corpo: { email: 'aluno@hytech.com', senha: 'errada123' } });
    const b = await req('/auth/login', { metodo: 'POST', corpo: { email: 'nao@existe.com', senha: 'errada123' } });
    assert.equal(a.status, 401); assert.equal(b.status, 401);
    assert.deepEqual(a.json, b.json);
  });

  await t.test('conta inativa => 403 (só depois de acertar a senha)', async () => {
    assert.equal((await req('/auth/login', { metodo: 'POST', corpo: { email: 'inativo@hytech.com', senha: SENHA } })).status, 403);
    assert.equal((await req('/auth/login', { metodo: 'POST', corpo: { email: 'inativo@hytech.com', senha: 'errada123' } })).status, 401);
  });

  await t.test('admin NÃO entra pela web', async () => {
    const r = await req('/auth/login', { metodo: 'POST', corpo: { email: 'admin@hytech.com', senha: SENHA } });
    assert.equal(r.status, 403);
    assert.match(r.json.erro, /HytechAdmin/);
  });

  await t.test('cadastro valida campos e cria só aluno (senha vai como hash)', async () => {
    const base = { nome: 'Maria Souza', nickname: 'maria', email: 'maria@x.com', senha: SENHA };
    assert.equal((await req('/auth/cadastro', { metodo: 'POST', corpo: { ...base, senha: '123' } })).status, 400);
    assert.equal((await req('/auth/cadastro', { metodo: 'POST', corpo: { ...base, email: 'ruim' } })).status, 400);
    assert.equal((await req('/auth/cadastro', { metodo: 'POST', corpo: { ...base, dataNascimento: '2999-01-01' } })).status, 400);
    const ok = await req('/auth/cadastro', { metodo: 'POST', corpo: { ...base, dataNascimento: '2008-05-20' } });
    assert.equal(ok.status, 201);
    const proc = db.chamadas.filter(c => c[0] === 'sp_CadastrarAluno').pop();
    assert.ok(bcrypt.compareSync(SENHA, proc[1].senha_hash));
    assert.equal(proc[1].senha, undefined);
  });

  await t.test('regra do banco (RAISERROR) vira 400 com a mensagem', async () => {
    const r = await req('/auth/cadastro', { metodo: 'POST', corpo: { nome: 'A B', nickname: 'ab', email: 'aluno@hytech.com', senha: SENHA } });
    assert.equal(r.status, 400);
    assert.equal(r.json.erro, 'Este e-mail já está cadastrado.');
  });

  await t.test('rotas protegidas exigem token; /auth/me e /config/chaves funcionam', async () => {
    assert.equal((await req('/materias')).status, 401);
    assert.equal((await req('/materias', { token: 'lixo' })).status, 401);
    const token = await login('professor@hytech.com');
    const me = await req('/auth/me', { token });
    assert.equal(me.json.papel, 'professor'); assert.equal(me.json.id_professor, 1);
    assert.deepEqual((await req('/config/chaves', { token })).json, { questaoFacil: 7, questaoMedia: 10, questaoDificil: 15, certificado: 50 });
  });

  await t.test('rota inexistente => 404', async () => {
    assert.equal((await req('/nada')).status, 404);
  });
});

test('Fase 2 — conteúdo e resposta de questões', async (t) => {
  const { req, login, db, fechar } = await subir();
  t.after(fechar);
  const token = await login('aluno@hytech.com');

  await t.test('/materias traz slug, ordem e custo de desbloqueio (0/19/38/57)', async () => {
    const r = await req('/materias', { token });
    assert.deepEqual(r.json.map(m => m.slug), ['programacao', 'design', 'banco-de-dados', 'redes']);
    assert.deepEqual(r.json.map(m => m.chavesParaDesbloquear), [0, 19, 38, 57]);
  });

  await t.test('/materias/:id/aulas devolve 4 tópicos (o último é extra) com dica e exemplo', async () => {
    const r = await req('/materias/1/aulas', { token });
    assert.equal(r.status, 200);
    assert.equal(r.json.length, 4);
    assert.deepEqual(r.json.map(a => a.isExtra), [false, false, false, true]);
    assert.equal(r.json[0].topico, 'Tópico 1');
    assert.equal(r.json[0].titulo, 'Variáveis em Python');
    assert.deepEqual(r.json.map(a => a.chaves), [3, 3, 3, 5]);
    assert.ok(r.json[0].dica.titulo && r.json[0].exemplo && r.json[0].conteudo.includes('<p>'));
  });

  await t.test('/materias/:id/questoes tem 3 questões × 5 alternativas e NUNCA o gabarito', async () => {
    const r = await req('/materias/1/questoes', { token });
    assert.equal(r.json.length, 3);
    for (const q of r.json) {
      assert.equal(q.alternativas.length, 5);
      for (const a of q.alternativas) { assert.ok(a.id && a.texto); assert.equal(a.correta, undefined); }
    }
    assert.ok(!JSON.stringify(r.json).toLowerCase().includes('correta'), 'o JSON não pode mencionar "correta"');
  });

  await t.test('id de matéria inválido => 400', async () => {
    assert.equal((await req('/materias/abc/aulas', { token })).status, 400);
    assert.equal((await req('/materias/-1/questoes', { token })).status, 400);
  });

  await t.test('responder: erra, acerta (ganha chaves do banco), repete (não ganha de novo)', async () => {
    const qs = (await req('/materias/1/questoes', { token })).json;
    const q = qs[0];
    const certaTexto = db.alternativas.find(a => a.id_questao === q.id && a.correta).id_alternativa;
    const errada = q.alternativas.find(a => a.id !== certaTexto).id;

    const e = await req(`/aluno/questoes/${q.id}/responder`, { metodo: 'POST', token, corpo: { idAlternativa: errada } });
    assert.equal(e.status, 200); assert.equal(e.json.acertou, false); assert.equal(e.json.chavesGanhas, 0);

    const c = await req(`/aluno/questoes/${q.id}/responder`, { metodo: 'POST', token, corpo: { idAlternativa: certaTexto } });
    assert.equal(c.json.acertou, true); assert.equal(c.json.chavesGanhas, 7); assert.equal(c.json.saldoChaves, 7);

    const again = await req(`/aluno/questoes/${q.id}/responder`, { metodo: 'POST', token, corpo: { idAlternativa: certaTexto } });
    assert.equal(again.json.jaConcluida, true); assert.equal(again.json.chavesGanhas, 0);
  });

  await t.test('o id do aluno vem do TOKEN, não do corpo', async () => {
    const q = (await req('/materias/2/questoes', { token })).json[0];
    const alt = q.alternativas[0].id;
    await req(`/aluno/questoes/${q.id}/responder`, { metodo: 'POST', token, corpo: { idAlternativa: alt, id_aluno: 999, idAluno: 999 } });
    const ultima = db.chamadas.filter(c => c[0] === 'sp_ResponderQuestao').pop();
    assert.equal(ultima[1].id_aluno, 1);
  });

  await t.test('responder: validações e permissão', async () => {
    assert.equal((await req('/aluno/questoes/1/responder', { metodo: 'POST', token, corpo: {} })).status, 400);
    assert.equal((await req('/aluno/questoes/1/responder', { metodo: 'POST', corpo: { idAlternativa: 1 } })).status, 401);
    const tp = await login('professor@hytech.com');
    assert.equal((await req('/aluno/questoes/1/responder', { metodo: 'POST', token: tp, corpo: { idAlternativa: 1 } })).status, 403);
    // alternativa de OUTRA questão => erro de regra do banco vira 400
    const r = await req('/aluno/questoes/1/responder', { metodo: 'POST', token, corpo: { idAlternativa: 60 } });
    assert.equal(r.status, 400);
    assert.match(r.json.erro, /Alternativa não pertence/);
  });
});

test('limite de tentativas: 429 depois de estourar', async () => {
  const { fechar } = await subir({ limiteAuthPorMinuto: 3 });
  // sobe outro app só para checar o limite
  const db = criarBancoFalso({ senhaPadrao: SENHA });
  const srv = criarApp({ cfg: { ...cfg, limiteAuthPorMinuto: 3 }, db }).listen(0);
  const url = `http://127.0.0.1:${srv.address().port}/auth/login`;
  const codigos = [];
  for (let i = 0; i < 5; i++) {
    const r = await fetch(url, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ email: 'x@x.com', senha: 'errada123' }) });
    codigos.push(r.status);
  }
  srv.close(); fechar();
  assert.deepEqual(codigos, [401, 401, 401, 429, 429]);
});
