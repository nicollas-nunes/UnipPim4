// Fase 5 — área do professor. Banco FALSO. Rodar: npm test
const test = require('node:test');
const assert = require('node:assert');
const { criarApp } = require('../src/app');
const { carregarConfig } = require('../src/config');
const { criarBancoFalso } = require('./banco-falso');
const { textoParaHtml, htmlParaTexto } = require('../src/texto');

const SENHA = 'senhaforte1';
const base = { JWT_KEY: 'x'.repeat(40), CORS_ORIGINS: 'http://localhost:5500', AUTH_LIMITE_POR_MINUTO: '1000' };

async function subir(extra = {}) {
  const db = criarBancoFalso({ senhaPadrao: SENHA });
  const server = criarApp({ cfg: carregarConfig({ ...base, ...extra }), db }).listen(0);
  const url = `http://127.0.0.1:${server.address().port}`;
  const req = async (rota, { metodo = 'GET', corpo, token } = {}) => {
    const r = await fetch(url + rota, { method: metodo,
      headers: { 'Content-Type': 'application/json', ...(token ? { Authorization: 'Bearer ' + token } : {}) },
      body: corpo ? JSON.stringify(corpo) : undefined });
    return { status: r.status, json: await r.json() };
  };
  const login = async (email) => (await req('/auth/login', { metodo: 'POST', corpo: { email, senha: SENHA } })).json.token;
  return { req, login, db, fechar: () => server.close() };
}
const AULA = { idMateria: 1, titulo: 'Minha aula', topico: 'Extra', texto: 'Linha 1\nLinha 2\n\nParágrafo <b>2</b> & fim', codigo: 'print(1)', dicaTitulo: 'Dica', dica: 'Use com cuidado' };
const QUESTAO = { idMateria: 1, enunciado: 'Quanto é 1+1?', dificuldade: 'Fácil', alternativas: ['1', '2', '3', ''], indiceCorreta: 1 };
const DESAFIO = { idMateria: 1, titulo: 'Desafio 1', enunciado: 'Faça algo', dificuldade: 'Médio', codigo: '# base', dica: 'pense' };

test('texto: HTML seguro e ida e volta', () => {
  const h = textoParaHtml(AULA.texto);
  assert.ok(!h.includes('<b>') && h.includes('&lt;b&gt;'));
  assert.equal(htmlParaTexto(h), AULA.texto);
});

test('perfil e permissões', async () => {
  const s = await subir();
  const tp = await s.login('professor@hytech.com'), ta = await s.login('aluno@hytech.com');
  const p = await s.req('/professor/perfil', { token: tp });
  assert.equal(p.status, 200);
  assert.equal(p.json.especialidade, 'Programação');
  assert.ok(p.json.idMateriaEspecialidade);
  assert.equal((await s.req('/professor/perfil', { token: ta })).status, 403);
  assert.equal((await s.req('/professor/perfil')).status, 401);
  assert.equal((await s.req('/aluno/desafios', { token: tp })).status, 403);
  s.fechar();
});

test('aula: rascunho -> pendente -> aprovada (extra, 0 chaves, com autor)', async () => {
  const s = await subir();
  const tp = await s.login('professor@hytech.com'), ta = await s.login('aluno@hytech.com');
  const c = await s.req('/professor/aulas', { metodo: 'POST', token: tp, corpo: AULA });
  assert.equal(c.status, 201);
  const id = c.json.id;
  const vis = async () => (await s.req('/materias/1/aulas', { token: ta })).json;
  assert.ok(!(await vis()).some(a => a.titulo === 'Minha aula'));
  assert.equal((await s.req(`/professor/aulas/${id}/enviar`, { metodo: 'POST', token: tp })).status, 200);
  assert.ok(!(await vis()).some(a => a.titulo === 'Minha aula'));
  const m = (await s.req('/professor/meus', { token: tp })).json;
  assert.equal(m.aulas[0].status, 'pendente');
  assert.equal(m.aulas[0].texto, AULA.texto);
  s.db.aprovar('aula', id);
  const a = (await vis()).find(x => x.titulo === 'Minha aula');
  assert.ok(a, 'aula aprovada aparece');
  assert.ok(a.conteudo.includes('&lt;b&gt;'));
  assert.equal((await s.req('/professor/perfil', { token: tp })).json.resumo.aprovados, 1);
  s.fechar();
});

test('aula do professor não impede certificado e não paga chaves', async () => {
  const s = await subir();
  const tp = await s.login('professor@hytech.com');
  const id = (await s.req('/professor/aulas', { metodo: 'POST', token: tp, corpo: { ...AULA, enviar: true } })).json.id;
  s.db.aprovar('aula', id);
  const au = s.db.aulas.find(a => a.id_aula === id);
  assert.equal(au.is_extra, 1); assert.equal(au.chaves_recompensa, 0);
  s.fechar();
});

test('questão: validações, envio, aprovação e sem gabarito para o aluno', async () => {
  const s = await subir();
  const tp = await s.login('professor@hytech.com'), ta = await s.login('aluno@hytech.com');
  assert.equal((await s.req('/professor/questoes', { metodo: 'POST', token: tp, corpo: { ...QUESTAO, dificuldade: 'X' } })).status, 400);
  assert.equal((await s.req('/professor/questoes', { metodo: 'POST', token: tp, corpo: { ...QUESTAO, alternativas: ['a','b','c','d','e','f'] } })).status, 400);
  const sem = await s.req('/professor/questoes', { metodo: 'POST', token: tp, corpo: { ...QUESTAO, enviar: true, indiceCorreta: -1 } });
  assert.equal(sem.status, 400);                          // banco: marque a correta
  const c = await s.req('/professor/questoes', { metodo: 'POST', token: tp, corpo: { ...QUESTAO, enviar: true } });
  assert.equal(c.status, 201);
  const m = (await s.req('/professor/meus', { token: tp })).json.questoes.find(q => q.id === c.json.id);
  assert.equal(m.status, 'pendente'); assert.equal(m.indiceCorreta, 1);
  s.db.aprovar('questao', c.json.id);
  const q = (await s.req('/materias/1/questoes', { token: ta })).json.find(x => x.pergunta === QUESTAO.enunciado);
  assert.ok(q);
  assert.ok(!JSON.stringify(q).includes('correta'));
  s.fechar();
});

test('dono: outro professor não mexe; aprovado não edita/exclui; rejeitado edita e reenvia com motivo', async () => {
  const s = await subir();
  s.db.usuarios.push({ id_usuario: 50, nome: 'Prof Dois', nickname: '@p2', email: 'p2@hytech.com', status: 'ativo', papel: 'professor', id_professor: 2, especialidade: 'Programação', senha_hash: s.db.usuarios[0].senha_hash });
  const t1 = await s.login('professor@hytech.com'), t2 = await s.login('p2@hytech.com');
  const id = (await s.req('/professor/aulas', { metodo: 'POST', token: t1, corpo: AULA })).json.id;
  assert.equal((await s.req(`/professor/aulas/${id}`, { metodo: 'PUT', token: t2, corpo: AULA })).status, 400);
  assert.equal((await s.req(`/professor/aulas/${id}/enviar`, { metodo: 'POST', token: t2 })).status, 400);
  assert.equal((await s.req(`/professor/aulas/${id}`, { metodo: 'DELETE', token: t2 })).status, 400);
  assert.equal((await s.req('/professor/meus', { token: t2 })).json.aulas.length, 0);
  await s.req(`/professor/aulas/${id}/enviar`, { metodo: 'POST', token: t1 });
  s.db.rejeitar('aula', id, 'Faltou exemplo');
  const m = (await s.req('/professor/meus', { token: t1 })).json.aulas[0];
  assert.equal(m.status, 'rejeitado'); assert.equal(m.motivo, 'Faltou exemplo');
  assert.equal((await s.req(`/professor/aulas/${id}`, { metodo: 'PUT', token: t1, corpo: { ...AULA, titulo: 'Corrigida', enviar: true } })).status, 200);
  s.db.aprovar('aula', id);
  assert.equal((await s.req(`/professor/aulas/${id}`, { metodo: 'PUT', token: t1, corpo: AULA })).status, 400);
  assert.equal((await s.req(`/professor/aulas/${id}`, { metodo: 'DELETE', token: t1 })).status, 400);
  s.fechar();
});

test('excluir rascunho e limites/especialidade', async () => {
  const s = await subir();
  const tp = await s.login('professor@hytech.com');
  const id = (await s.req('/professor/desafios', { metodo: 'POST', token: tp, corpo: DESAFIO })).json.id;
  assert.equal((await s.req(`/professor/desafios/${id}`, { metodo: 'DELETE', token: tp })).status, 200);
  assert.equal((await s.req('/professor/meus', { token: tp })).json.desafios.length, 0);
  assert.equal((await s.req('/professor/aulas', { metodo: 'POST', token: tp, corpo: { ...AULA, titulo: 'x'.repeat(101) } })).status, 400);
  assert.equal((await s.req('/professor/aulas', { metodo: 'POST', token: tp, corpo: { ...AULA, idMateria: 2 } })).status, 403);
  assert.equal((await s.req('/professor/aulas/abc', { metodo: 'PUT', token: tp, corpo: AULA })).status, 400);
  assert.equal((await s.req('/professor/xyz/1', { metodo: 'DELETE', token: tp })).status, 400);
  s.fechar();
});

test('desafio: professor cria, aprova, aluno conclui uma única vez', async () => {
  const s = await subir();
  const tp = await s.login('professor@hytech.com'), ta = await s.login('aluno@hytech.com');
  const id = (await s.req('/professor/desafios', { metodo: 'POST', token: tp, corpo: { ...DESAFIO, enviar: true } })).json.id;
  assert.equal((await s.req('/aluno/desafios', { token: ta })).json.length, 0);
  s.db.aprovar('desafio', id);
  const l = (await s.req('/aluno/desafios', { token: ta })).json;
  assert.equal(l.length, 1); assert.equal(l[0].concluido, false); assert.equal(l[0].autor, 'Prof. Silva');
  const c1 = (await s.req(`/aluno/desafios/${id}/concluir`, { metodo: 'POST', token: ta })).json;
  assert.equal(c1.chavesGanhas, 10); assert.equal(c1.jaConcluido, false);
  const c2 = (await s.req(`/aluno/desafios/${id}/concluir`, { metodo: 'POST', token: ta })).json;
  assert.equal(c2.chavesGanhas, 0); assert.equal(c2.jaConcluido, true);
  assert.equal((await s.req('/aluno/desafios/9999/concluir', { metodo: 'POST', token: ta })).status, 400);
  s.fechar();
});

test('desempenho dos alunos', async () => {
  const s = await subir();
  const tp = await s.login('professor@hytech.com'), ta = await s.login('aluno@hytech.com');
  const d = await s.req('/professor/desempenho', { token: tp });
  assert.equal(d.status, 200); assert.ok(d.json.length >= 1);
  assert.ok(d.json[0].nome && 'fases' in d.json[0] && 'certificados' in d.json[0]);
  assert.equal((await s.req('/professor/desempenho?materia=todas', { token: tp })).status, 200);
  assert.equal((await s.req('/professor/desempenho', { token: ta })).status, 403);
  s.fechar();
});

test('limite de escrita do professor -> 429', async () => {
  const s = await subir({ LIMITE_CONTEUDO_POR_HORA: '2' });
  const tp = await s.login('professor@hytech.com');
  const r = [];
  for (let i = 0; i < 4; i++) r.push((await s.req('/professor/desafios', { metodo: 'POST', token: tp, corpo: DESAFIO })).status);
  assert.ok(r.includes(429), r.join());
  s.fechar();
});
