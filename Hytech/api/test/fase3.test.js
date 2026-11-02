// Fase 3 — progresso do aluno no banco, ranking e conta. Banco FALSO. Rodar: npm test
const test = require('node:test');
const assert = require('node:assert');
const { criarApp } = require('../src/app');
const { carregarConfig } = require('../src/config');
const { criarBancoFalso } = require('./banco-falso');

const SENHA = 'senhaforte1';
const cfg = carregarConfig({ JWT_KEY: 'x'.repeat(40), CORS_ORIGINS: 'http://localhost:5500', AUTH_LIMITE_POR_MINUTO: '1000' });

async function subir() {
  const db = criarBancoFalso({ senhaPadrao: SENHA });
  const server = criarApp({ cfg, db }).listen(0);
  const base = `http://127.0.0.1:${server.address().port}`;
  const req = async (rota, { metodo = 'GET', corpo, token } = {}) => {
    const r = await fetch(base + rota, {
      method: metodo,
      headers: { 'Content-Type': 'application/json', ...(token ? { Authorization: 'Bearer ' + token } : {}) },
      body: corpo ? JSON.stringify(corpo) : undefined,
    });
    return { status: r.status, json: await r.json() };
  };
  const login = async (email, senha = SENHA) => (await req('/auth/login', { metodo: 'POST', corpo: { email, senha } })).json.token;
  const novoAluno = async (n) => {
    await req('/auth/cadastro', { metodo: 'POST', corpo: { nome: 'Aluno ' + n, nickname: 'aluno' + n, email: `a${n}@x.com`, senha: SENHA } });
    return login(`a${n}@x.com`);
  };
  return { req, login, novoAluno, db, fechar: () => server.close() };
}

// Conclui tudo o que é preciso para o certificado da matéria (aulas não extras + todas as questões)
async function fazerTudoDaMateria(ctx, token, idMateria) {
  const aulas = (await ctx.req(`/materias/${idMateria}/aulas`, { token })).json;
  for (const a of aulas.filter(x => !x.isExtra)) await ctx.req(`/aluno/aulas/${a.id}/concluir`, { metodo: 'POST', token });
  const qs = (await ctx.req(`/materias/${idMateria}/questoes`, { token })).json;
  let ultima;
  for (const q of qs) {
    const certa = ctx.db.alternativas.find(a => a.id_questao === q.id && a.correta);
    ultima = await ctx.req(`/aluno/questoes/${q.id}/responder`, { metodo: 'POST', token, corpo: { idAlternativa: certa.id_alternativa } });
  }
  return ultima;
}

test('Fase 3 — estado do aluno', async (t) => {
  const c = await subir(); t.after(c.fechar);

  await t.test('aluno novo vem zerado; professor e anônimo não acessam', async () => {
    const tk = await c.novoAluno(1);
    const r = await c.req('/aluno/estado', { token: tk });
    assert.equal(r.status, 200);
    assert.deepEqual(r.json.resumo, { pontos: 0, saldoChaves: 0, streak: 0, posicao: r.json.resumo.posicao, fasesConcluidas: 0, questoesConcluidas: 0, certificados: 0 });
    assert.deepEqual(r.json.aulasConcluidas, []);
    assert.equal(r.json.materias.length, 4);
    assert.deepEqual(r.json.materias.map(m => m.desbloqueada), [true, false, false, false]);
    assert.equal(r.json.perfil.nickname, '@aluno1');
    assert.equal((await c.req('/aluno/estado', { token: await c.login('professor@hytech.com') })).status, 403);
    assert.equal((await c.req('/aluno/estado')).status, 401);
  });

  await t.test('concluir aula: 1ª vez credita, 2ª vez não; estado reflete; id vem do token', async () => {
    const tk = await c.novoAluno(2);
    const aulas = (await c.req('/materias/1/aulas', { token: tk })).json;
    const r1 = await c.req(`/aluno/aulas/${aulas[0].id}/concluir`, { metodo: 'POST', token: tk });
    assert.equal(r1.status, 200);
    assert.equal(r1.json.chavesGanhas, aulas[0].chaves);
    assert.equal(r1.json.saldoChaves, aulas[0].chaves);
    const r2 = await c.req(`/aluno/aulas/${aulas[0].id}/concluir`, { metodo: 'POST', token: tk });
    assert.equal(r2.json.chavesGanhas, 0);
    assert.equal(r2.json.saldoChaves, aulas[0].chaves);
    const e = (await c.req('/aluno/estado', { token: tk })).json;
    assert.deepEqual(e.aulasConcluidas, [aulas[0].id]);
    assert.equal(e.resumo.fasesConcluidas, 1);
    assert.equal(e.resumo.streak, 1);
    // outro aluno não enxerga esse progresso
    const outro = (await c.req('/aluno/estado', { token: await c.novoAluno(3) })).json;
    assert.deepEqual(outro.aulasConcluidas, []); assert.equal(outro.resumo.pontos, 0);
  });

  await t.test('concluir: aula inexistente => 400 do banco; id inválido => 400; sem token => 401', async () => {
    const tk = await c.novoAluno(4);
    const r = await c.req('/aluno/aulas/99999/concluir', { metodo: 'POST', token: tk });
    assert.equal(r.status, 400); assert.match(r.json.erro, /inexistente/);
    assert.equal((await c.req('/aluno/aulas/abc/concluir', { metodo: 'POST', token: tk })).status, 400);
    assert.equal((await c.req('/aluno/aulas/1/concluir', { metodo: 'POST' })).status, 401);
  });
});

test('Fase 3 — certificado e desbloqueio de matéria', async (t) => {
  const c = await subir(); t.after(c.fechar);

  await t.test('desbloquear Design sem a anterior concluída => 400; sem chaves => 400', async () => {
    const tk = await c.novoAluno(10);
    let r = await c.req('/aluno/materias/2/desbloquear', { metodo: 'POST', token: tk });
    assert.equal(r.status, 400); assert.match(r.json.erro, /matéria anterior/);
  });

  await t.test('matéria 1 completa => certificado (+50 do banco); desbloquear Design cobra 19 e não cobra 2x', async () => {
    const tk = await c.novoAluno(11);
    const pontosAntes = (await c.req('/aluno/estado', { token: tk })).json.resumo.pontos;
    const ultima = await fazerTudoDaMateria(c, tk, 1);
    assert.equal(ultima.json.certificadoEmitido, true);
    const e = (await c.req('/aluno/estado', { token: tk })).json;
    assert.equal(e.certificados.length, 1); assert.equal(e.certificados[0].idMateria, 1);
    const ganhoSemCert = e.resumo.pontos - pontosAntes - 50;
    assert.ok(ganhoSemCert > 0, 'pontos = aulas + questões + 50 do certificado');

    const saldoAntes = e.resumo.saldoChaves;
    const d = await c.req('/aluno/materias/2/desbloquear', { metodo: 'POST', token: tk });
    assert.equal(d.status, 200); assert.equal(d.json.chavesGastas, 19); assert.equal(d.json.saldoChaves, saldoAntes - 19);
    const d2 = await c.req('/aluno/materias/2/desbloquear', { metodo: 'POST', token: tk });
    assert.equal(d2.json.chavesGastas, 0); assert.equal(d2.json.saldoChaves, saldoAntes - 19);

    const e2 = (await c.req('/aluno/estado', { token: tk })).json;
    assert.equal(e2.materias.find(m => m.id === 2).desbloqueada, true);
    assert.equal(e2.resumo.pontos, e.resumo.pontos, 'gastar chaves NÃO diminui os pontos (nível/ranking)');
    assert.equal(e2.resumo.saldoChaves, saldoAntes - 19);
  });

  await t.test('refazer tudo não paga o certificado de novo', async () => {
    const tk = await c.novoAluno(12);
    await fazerTudoDaMateria(c, tk, 1);
    const p1 = (await c.req('/aluno/estado', { token: tk })).json.resumo.pontos;
    await fazerTudoDaMateria(c, tk, 1);
    assert.equal((await c.req('/aluno/estado', { token: tk })).json.resumo.pontos, p1);
  });

  await t.test('chaves insuficientes (custo maior que o saldo) => 400', async () => {
    const tk = await c.novoAluno(13);
    await fazerTudoDaMateria(c, tk, 1);
    await c.req('/aluno/materias/2/desbloquear', { metodo: 'POST', token: tk });
    await fazerTudoDaMateria(c, tk, 2);
    const e = (await c.req('/aluno/estado', { token: tk })).json;
    // força saldo baixo no banco falso
    const idAluno = c.db.usuarios.find(u => u.email === 'a13@x.com').id_aluno;
    c.db.estadoDe(idAluno).gastas = e.resumo.pontos - 1;
    const r = await c.req('/aluno/materias/3/desbloquear', { metodo: 'POST', token: tk });
    assert.equal(r.status, 400); assert.equal(r.json.erro, 'Chaves insuficientes.');
  });
});

test('Fase 3 — ranking', async (t) => {
  const c = await subir(); t.after(c.fechar);

  await t.test('ordena por pontos, marca souEu, respeita top e exige login', async () => {
    const tk = await c.novoAluno(20);
    const a = (await c.req('/materias/1/aulas', { token: tk })).json;
    await c.req(`/aluno/aulas/${a[0].id}/concluir`, { metodo: 'POST', token: tk });
    const r = await c.req('/ranking?top=3', { token: tk });
    assert.equal(r.status, 200); assert.equal(r.json.length, 3);
    assert.deepEqual(r.json.map(x => x.posicao), [1, 2, 3]);
    assert.ok(r.json[0].pontos >= r.json[1].pontos);
    assert.equal(r.json.some(x => x.souEu), false, 'aluno novo com 3 pontos não está no top 3');
    const todos = (await c.req('/ranking?top=50', { token: tk })).json;
    const eu = todos.find(x => x.souEu); assert.ok(eu); assert.equal(eu.nickname, '@aluno20');
    assert.equal((await c.req('/ranking?top=999', { token: tk })).json.length <= 50, true);
    assert.equal((await c.req('/ranking')).status, 401);
  });

  await t.test('empate divide a posição', async () => {
    const t1 = await c.novoAluno(21), t2 = await c.novoAluno(22);
    const a = (await c.req('/materias/1/aulas', { token: t1 })).json;
    for (const tk of [t1, t2]) await c.req(`/aluno/aulas/${a[0].id}/concluir`, { metodo: 'POST', token: tk });
    const todos = (await c.req('/ranking?top=50', { token: t1 })).json;
    const p21 = todos.find(x => x.nickname === '@aluno21').posicao, p22 = todos.find(x => x.nickname === '@aluno22').posicao;
    assert.equal(p21, p22);
  });
});

test('Fase 3 — conta (perfil e senha)', async (t) => {
  const c = await subir(); t.after(c.fechar);
  const FOTO = 'data:image/jpeg;base64,/9j/4AAQSkZJRgABAQ==';

  await t.test('editar nome/nickname/e-mail/foto; token novo; serve aluno e professor', async () => {
    const tk = await c.novoAluno(30);
    const r = await c.req('/conta/perfil', { metodo: 'PUT', token: tk, corpo: { nome: 'Novo Nome', nickname: 'novonick', email: 'NOVO30@x.com', foto: FOTO } });
    assert.equal(r.status, 200);
    assert.equal(r.json.perfil.nickname, '@novonick'); assert.equal(r.json.perfil.email, 'novo30@x.com');
    const me = await c.req('/auth/me', { token: r.json.token });
    assert.equal(me.json.email, 'novo30@x.com'); assert.equal(me.json.nome, 'Novo Nome');
    const est = (await c.req('/aluno/estado', { token: r.json.token })).json;
    assert.equal(est.perfil.foto, FOTO); assert.equal(est.perfil.nickname, '@novonick');
    const prof = await c.req('/conta/perfil', { metodo: 'PUT', token: await c.login('professor@hytech.com'), corpo: { nome: 'Prof Novo' } });
    assert.equal(prof.status, 200);
  });

  await t.test('duplicidade e validações viram 400', async () => {
    const tk = await c.novoAluno(31);
    let r = await c.req('/conta/perfil', { metodo: 'PUT', token: tk, corpo: { email: 'aluno@hytech.com' } });
    assert.equal(r.status, 400); assert.equal(r.json.erro, 'Este e-mail já está em uso.');
    r = await c.req('/conta/perfil', { metodo: 'PUT', token: tk, corpo: { nickname: '@PedroBala' } });
    assert.equal(r.json.erro, 'Este nome de usuário já está em uso.');
    assert.equal((await c.req('/conta/perfil', { metodo: 'PUT', token: tk, corpo: { email: 'ruim' } })).status, 400);
    assert.equal((await c.req('/conta/perfil', { metodo: 'PUT', token: tk, corpo: {} })).status, 400);
    assert.equal((await c.req('/conta/perfil', { metodo: 'PUT', token: tk, corpo: { foto: 'data:image/png;base64,' + 'A'.repeat(1_500_000) } })).status, 400);
    assert.equal((await c.req('/conta/perfil', { metodo: 'PUT', token: tk, corpo: { foto: 'javascript:alert(1)' } })).status, 400);
    assert.equal((await c.req('/conta/perfil', { metodo: 'PUT', corpo: { nome: 'x' } })).status, 401);
  });

  await t.test('trocar senha: atual errada, nova curta, ok; só a nova funciona; vale após trocar e-mail', async () => {
    const tk = await c.novoAluno(32);
    assert.equal((await c.req('/conta/senha', { metodo: 'POST', token: tk, corpo: { senhaAtual: 'errada123', novaSenha: 'outrasenha1' } })).json.erro, 'Senha atual incorreta.');
    assert.equal((await c.req('/conta/senha', { metodo: 'POST', token: tk, corpo: { senhaAtual: SENHA, novaSenha: '123' } })).status, 400);
    const trocou = await c.req('/conta/perfil', { metodo: 'PUT', token: tk, corpo: { email: 'a32novo@x.com' } });
    const ok = await c.req('/conta/senha', { metodo: 'POST', token: trocou.json.token, corpo: { senhaAtual: SENHA, novaSenha: 'outrasenha1' } });
    assert.equal(ok.status, 200);
    assert.equal((await c.req('/auth/login', { metodo: 'POST', corpo: { email: 'a32novo@x.com', senha: SENHA } })).status, 401);
    assert.equal((await c.req('/auth/login', { metodo: 'POST', corpo: { email: 'a32novo@x.com', senha: 'outrasenha1' } })).status, 200);
  });
});
