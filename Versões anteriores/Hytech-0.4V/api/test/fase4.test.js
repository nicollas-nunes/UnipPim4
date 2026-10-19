// Fase 4 — tickets de suporte. Banco FALSO. Rodar: npm test
const test = require('node:test');
const assert = require('node:assert');
const { criarApp } = require('../src/app');
const { carregarConfig } = require('../src/config');
const { criarBancoFalso } = require('./banco-falso');

const SENHA = 'senhaforte1';
const base = { JWT_KEY: 'x'.repeat(40), CORS_ORIGINS: 'http://localhost:5500', AUTH_LIMITE_POR_MINUTO: '1000' };

async function subir(extra = {}) {
  const db = criarBancoFalso({ senhaPadrao: SENHA });
  const cfg = carregarConfig({ ...base, ...extra });
  const server = criarApp({ cfg, db }).listen(0);
  const url = `http://127.0.0.1:${server.address().port}`;
  const req = async (rota, { metodo = 'GET', corpo, token } = {}) => {
    const r = await fetch(url + rota, {
      method: metodo,
      headers: { 'Content-Type': 'application/json', ...(token ? { Authorization: 'Bearer ' + token } : {}) },
      body: corpo ? JSON.stringify(corpo) : undefined,
    });
    return { status: r.status, json: await r.json() };
  };
  const login = async (email) => (await req('/auth/login', { metodo: 'POST', corpo: { email, senha: SENHA } })).json.token;
  const novoAluno = async (n) => {
    await req('/auth/cadastro', { metodo: 'POST', corpo: { nome: 'Aluno ' + n, nickname: 'aluno' + n, email: `a${n}@x.com`, senha: SENHA } });
    return login(`a${n}@x.com`);
  };
  return { req, login, novoAluno, db, fechar: () => server.close() };
}
const abrir = (c, token, idMateria = 1, extra = {}) =>
  c.req('/tickets', { metodo: 'POST', token, corpo: { idMateria, assunto: 'Dúvida', mensagem: 'Não entendi variáveis', ...extra } });

test('Fase 4 — abrir ticket e roteamento', async (t) => {
  const c = await subir(); t.after(c.fechar);
  const aluno = await c.novoAluno(1);

  await t.test('Programação vai para o professor; Design cai no admin (sem professor da matéria)', async () => {
    const a = await abrir(c, aluno, 1);
    assert.equal(a.status, 201);
    const det = (await c.req('/tickets/' + a.json.id, { token: aluno })).json;
    assert.equal(det.papelDestinatario, 'professor');
    assert.equal(det.professor, 'Prof. Silva');
    assert.equal(det.materia, 'Programação');
    assert.equal(det.status, 'aberto');
    assert.equal(det.mensagens.length, 1);
    assert.equal(det.mensagens[0].texto, 'Não entendi variáveis');
    assert.equal(det.mensagens[0].remetente, 'aluno');
    const b = await abrir(c, aluno, 2);
    const det2 = (await c.req('/tickets/' + b.json.id, { token: aluno })).json;
    assert.equal(det2.papelDestinatario, 'admin');
  });

  await t.test('o solicitante vem do TOKEN, não do corpo', async () => {
    await abrir(c, aluno, 1, { id_usuario_solicitante: 999, idAluno: 999 });
    const ultima = c.db.chamadas.filter(x => x[0] === 'sp_AbrirTicket').pop();
    assert.notEqual(ultima[1].id_usuario_solicitante, 999);
    assert.ok(ultima[1].id_usuario_solicitante > 0);
  });

  await t.test('validações, permissão e login', async () => {
    assert.equal((await abrir(c, aluno, 1, { assunto: '   ' })).status, 400);
    assert.equal((await abrir(c, aluno, 1, { assunto: 'x'.repeat(151) })).status, 400);
    assert.equal((await abrir(c, aluno, 1, { mensagem: '' })).status, 400);
    assert.equal((await abrir(c, aluno, 1, { mensagem: 'x'.repeat(4001) })).status, 400);
    assert.equal((await abrir(c, aluno, 'abc')).status, 400);
    assert.equal((await abrir(c, aluno, null)).status, 400);
    const prof = await c.login('professor@hytech.com');
    assert.equal((await abrir(c, prof, 1)).status, 403);
    assert.equal((await abrir(c, undefined, 1)).status, 401);
  });

  await t.test('admin não acessa pela web', async () => {
    assert.equal((await c.req('/auth/login', { metodo: 'POST', corpo: { email: 'admin@hytech.com', senha: SENHA } })).status, 403);
  });
});

test('Fase 4 — lista e detalhe (isolamento)', async (t) => {
  const c = await subir(); t.after(c.fechar);
  const a1 = await c.novoAluno(1), a2 = await c.novoAluno(2);
  const prof = await c.login('professor@hytech.com');
  const t1 = (await abrir(c, a1, 1, { assunto: 'Do aluno 1' })).json.id;
  const t2 = (await abrir(c, a2, 1, { assunto: 'Do aluno 2' })).json.id;
  await abrir(c, a2, 2, { assunto: 'Vai para o admin' });

  await t.test('aluno só vê os próprios; professor vê só os que recebeu', async () => {
    const l1 = (await c.req('/tickets', { token: a1 })).json;
    assert.deepEqual(l1.map(x => x.assunto), ['Do aluno 1']);
    assert.equal(l1[0].souSolicitante, true);
    const lp = (await c.req('/tickets', { token: prof })).json;
    assert.deepEqual(lp.map(x => x.assunto).sort(), ['Do aluno 1', 'Do aluno 2']);   // o de Design foi para o admin
    assert.equal(lp[0].souSolicitante, false);
  });

  await t.test('quem não participa não lê o ticket (400 do banco); id inválido => 400; sem token => 401', async () => {
    const r = await c.req('/tickets/' + t1, { token: a2 });
    assert.equal(r.status, 400); assert.match(r.json.erro, /não participa/);
    assert.equal((await c.req('/tickets/abc', { token: a1 })).status, 400);
    assert.equal((await c.req('/tickets/' + t2)).status, 401);
    assert.equal((await c.req('/tickets/' + t2, { token: prof })).status, 200);
  });

  await t.test('mais recente primeiro (última atividade)', async () => {
    const a3 = await c.novoAluno(3);
    const x = (await abrir(c, a3, 1, { assunto: 'Primeiro' })).json.id;
    await abrir(c, a3, 1, { assunto: 'Segundo' });
    await c.req(`/tickets/${x}/mensagens`, { metodo: 'POST', token: a3, corpo: { texto: 'mais uma' } });
    const l = (await c.req('/tickets', { token: a3 })).json;
    assert.deepEqual(l.map(i => i.assunto), ['Primeiro', 'Segundo']);
  });
});

test('Fase 4 — conversa, status e encerramento', async (t) => {
  const c = await subir(); t.after(c.fechar);
  const aluno = await c.novoAluno(1), outro = await c.novoAluno(2);
  const prof = await c.login('professor@hytech.com');
  const id = (await abrir(c, aluno, 1)).json.id;
  const msg = (token, texto, i = id) => c.req(`/tickets/${i}/mensagens`, { metodo: 'POST', token, corpo: { texto } });
  const status = async () => (await c.req('/tickets/' + id, { token: aluno })).json.status;

  await t.test('professor responde => respondido; aluno escreve de novo => aberto', async () => {
    assert.equal((await msg(prof, 'Veja a aula 1')).status, 201);
    assert.equal(await status(), 'respondido');
    assert.equal((await msg(aluno, 'Obrigado, mas ainda tenho dúvida')).status, 201);
    assert.equal(await status(), 'aberto');
    const det = (await c.req('/tickets/' + id, { token: aluno })).json;
    assert.deepEqual(det.mensagens.map(m => m.remetente), ['aluno', 'professor', 'aluno']);
    assert.deepEqual(det.mensagens.map(m => m.minha), [true, false, true]);
    assert.equal(det.mensagens[1].papel, 'professor');
  });

  await t.test('validações e quem não participa', async () => {
    assert.equal((await msg(aluno, '   ')).status, 400);
    assert.equal((await msg(aluno, 'x'.repeat(4001))).status, 400);
    assert.equal((await msg(aluno, 'oi', 'abc')).status, 400);
    const r = await msg(outro, 'intruso'); assert.equal(r.status, 400); assert.match(r.json.erro, /não participa/);
    assert.equal((await msg(undefined, 'oi')).status, 401);
  });

  await t.test('só o professor destinatário encerra; depois, ninguém escreve', async () => {
    assert.equal((await c.req(`/tickets/${id}/fechar`, { metodo: 'POST', token: aluno })).status, 403);
    const x = await c.req(`/tickets/${id}/fechar`, { metodo: 'POST', token: prof });
    assert.equal(x.status, 200);
    assert.equal(await status(), 'fechado');
    const r = await msg(aluno, 'ainda aí?'); assert.equal(r.status, 400); assert.match(r.json.erro, /encerrado/);
    const r2 = await msg(prof, 'ok?'); assert.equal(r2.status, 400);
    assert.equal((await c.req(`/tickets/${id}/fechar`, { metodo: 'POST', token: prof })).status, 200);   // idempotente
  });

  await t.test('outro professor-sem-ticket não encerra o ticket alheio', async () => {
    const id2 = (await abrir(c, aluno, 1)).json.id;
    c.db.usuarios.push({ id_usuario: 77, nome: 'Outro Prof', nickname: '@outro', email: 'outroprof@hytech.com', status: 'ativo', papel: 'professor', id_aluno: null, id_professor: 7, especialidade: 'Redes', senha_hash: c.db.usuarios[0].senha_hash });
    const tk = await c.login('outroprof@hytech.com');
    const r = await c.req(`/tickets/${id2}/fechar`, { metodo: 'POST', token: tk });
    assert.equal(r.status, 400); assert.match(r.json.erro, /sem permissão/);
    assert.equal((await msg(tk, 'oi', id2)).status, 400);
  });

  await t.test('texto com HTML é guardado como texto (o escape é do site)', async () => {
    const id3 = (await abrir(c, aluno, 1, { mensagem: '<script>alert(1)</script>' })).json.id;
    const det = (await c.req('/tickets/' + id3, { token: aluno })).json;
    assert.equal(det.mensagens[0].texto, '<script>alert(1)</script>');
  });
});

test('Fase 4 — limite de mensagens por hora', async () => {
  const c = await subir({ TICKETS_LIMITE_POR_HORA: '3' });
  const aluno = await c.novoAluno(1);
  const codigos = [];
  for (let i = 0; i < 5; i++) codigos.push((await abrir(c, aluno, 1)).status);
  c.fechar();
  assert.deepEqual(codigos, [201, 201, 201, 429, 429]);
});
