// Fase 7 — verificação por e-mail (cadastro, esqueci a senha, trocar senha/e-mail) e desativar conta.
// Banco FALSO + e-mail em memória. Rodar: npm test
const test = require('node:test');
const assert = require('node:assert');
const bcrypt = require('bcryptjs');
const { criarApp } = require('../src/app');
const { carregarConfig } = require('../src/config');
const { criarEmailMemoria } = require('../src/email');
const { criarBancoFalso } = require('./banco-falso');

const SENHA = 'senhaforte1';
const cfg = carregarConfig({ JWT_KEY: 'x'.repeat(40), CORS_ORIGINS: 'http://localhost:5500', AUTH_LIMITE_POR_MINUTO: '1000' });

async function subir() {
  const db = criarBancoFalso({ senhaPadrao: SENHA });
  const email = criarEmailMemoria();
  const server = criarApp({ cfg, db, email }).listen(0);
  const base = `http://127.0.0.1:${server.address().port}`;
  const req = async (rota, { metodo = 'GET', corpo, token } = {}) => {
    const r = await fetch(base + rota, { method: metodo,
      headers: { 'Content-Type': 'application/json', ...(token ? { Authorization: 'Bearer ' + token } : {}) },
      body: corpo ? JSON.stringify(corpo) : undefined });
    return { status: r.status, json: await r.json() };
  };
  const login = async (em, senha = SENHA) => (await req('/auth/login', { metodo: 'POST', corpo: { email: em, senha } })).json.token;
  return { req, login, db, email, fechar: () => server.close() };
}

test('Fase 7 — cadastro com código de e-mail', async (t) => {
  const c = await subir(); t.after(c.fechar);
  const dados = { nome: 'Nova Pessoa', nickname: 'nova', email: 'nova@x.com', senha: SENHA };

  await t.test('cadastro manda o código, mostra e-mail mascarado e NÃO cria a conta ainda', async () => {
    const r = await c.req('/auth/cadastro', { metodo: 'POST', corpo: dados });
    assert.equal(r.status, 202);
    assert.equal(r.json.email, 'n***@x.com');
    assert.equal(c.email.enviados.length, 1);
    assert.equal(c.email.enviados[0].para, 'nova@x.com');
    assert.match(c.email.enviados[0].texto, /\b\d{6}\b/);
    assert.ok(!c.db.usuarios.some(u => u.email === 'nova@x.com'));
  });

  await t.test('o código NÃO é guardado em texto e a senha vai como hash', () => {
    const cod = c.email.ultimoCodigo('nova@x.com');
    const linha = c.db.codigos.find(x => x.chave === 'e:nova@x.com');
    assert.notEqual(linha.codigo_hash, cod);
    assert.ok(!linha.dados.includes(SENHA));
    assert.ok(bcrypt.compareSync(SENHA, JSON.parse(linha.dados).senha_hash));
  });

  await t.test('código errado, e depois de 5 erros nem o certo vale mais', async () => {
    for (let i = 0; i < 5; i++) {
      const r = await c.req('/auth/cadastro/confirmar', { metodo: 'POST', corpo: { email: 'nova@x.com', codigo: '000000' === c.email.ultimoCodigo('nova@x.com') ? '111111' : '000000' } });
      assert.equal(r.status, 400); assert.match(r.json.erro, /incorreto/);
    }
    const certo = await c.req('/auth/cadastro/confirmar', { metodo: 'POST', corpo: { email: 'nova@x.com', codigo: c.email.ultimoCodigo('nova@x.com') } });
    assert.equal(certo.status, 400); assert.match(certo.json.erro, /inválido ou expirado/);
  });

  await t.test('reenviar (depois de esperar) gera código novo e o antigo deixa de valer', async () => {
    const antigo = c.email.ultimoCodigo('nova@x.com');
    const cedo = await c.req('/auth/cadastro/reenviar', { metodo: 'POST', corpo: { email: 'nova@x.com' } });
    assert.equal(cedo.status, 400); assert.match(cedo.json.erro, /Aguarde/);     // limite de 30 s
    c.db.avancarTempo(31);
    const r = await c.req('/auth/cadastro/reenviar', { metodo: 'POST', corpo: { email: 'nova@x.com' } });
    assert.equal(r.status, 200);
    const novo = c.email.ultimoCodigo('nova@x.com');
    if (novo !== antigo) {
      assert.equal((await c.req('/auth/cadastro/confirmar', { metodo: 'POST', corpo: { email: 'nova@x.com', codigo: antigo } })).status, 400);
    }
    const ok = await c.req('/auth/cadastro/confirmar', { metodo: 'POST', corpo: { email: 'nova@x.com', codigo: novo } });
    assert.equal(ok.status, 201);
    assert.ok(await c.login('nova@x.com'));
  });

  await t.test('código usado não funciona de novo; formato inválido => 400', async () => {
    const usado = c.email.ultimoCodigo('nova@x.com');
    assert.equal((await c.req('/auth/cadastro/confirmar', { metodo: 'POST', corpo: { email: 'nova@x.com', codigo: usado } })).status, 400);
    assert.equal((await c.req('/auth/cadastro/confirmar', { metodo: 'POST', corpo: { email: 'nova@x.com', codigo: 'abc' } })).status, 400);
  });

  await t.test('código expira', async () => {
    await c.req('/auth/cadastro', { metodo: 'POST', corpo: { ...dados, nickname: 'exp', email: 'exp@x.com' } });
    const cod = c.email.ultimoCodigo('exp@x.com');
    c.db.avancarTempo(11 * 60);
    const r = await c.req('/auth/cadastro/confirmar', { metodo: 'POST', corpo: { email: 'exp@x.com', codigo: cod } });
    assert.equal(r.status, 400); assert.match(r.json.erro, /expirado/);
    // mas dá para reenviar sem preencher tudo de novo
    assert.equal((await c.req('/auth/cadastro/reenviar', { metodo: 'POST', corpo: { email: 'exp@x.com' } })).status, 200);
  });

  await t.test('e-mail ou usuário já existentes são recusados ANTES de mandar e-mail', async () => {
    const antes = c.email.enviados.length;
    assert.equal((await c.req('/auth/cadastro', { metodo: 'POST', corpo: { ...dados, email: 'aluno@hytech.com' } })).json.erro, 'Este e-mail já está cadastrado.');
    assert.equal((await c.req('/auth/cadastro', { metodo: 'POST', corpo: { ...dados, email: 'outro@x.com', nickname: '@PedroBala' } })).json.erro, 'Este nome de usuário já está em uso.');
    assert.equal(c.email.enviados.length, antes);
  });

  await t.test('reenviar sem cadastro pendente => 400', async () => {
    assert.equal((await c.req('/auth/cadastro/reenviar', { metodo: 'POST', corpo: { email: 'ninguem@x.com' } })).status, 400);
  });
});

test('Fase 7 — esqueci a senha', async (t) => {
  const c = await subir(); t.after(c.fechar);

  await t.test('resposta igual para e-mail existente e inexistente; só o existente recebe e-mail', async () => {
    const a = await c.req('/auth/esqueci', { metodo: 'POST', corpo: { email: 'aluno@hytech.com' } });
    const b = await c.req('/auth/esqueci', { metodo: 'POST', corpo: { email: 'nao@existe.com' } });
    assert.equal(a.status, 200); assert.deepEqual(a.json, b.json);
    assert.equal(c.email.enviados.length, 1);
    assert.equal(c.email.enviados[0].para, 'aluno@hytech.com');
    assert.equal((await c.req('/auth/esqueci', { metodo: 'POST', corpo: { email: 'ruim' } })).status, 400);
  });

  await t.test('conta inativa e admin NÃO recebem código', async () => {
    const n = c.email.enviados.length;
    await c.req('/auth/esqueci', { metodo: 'POST', corpo: { email: 'inativo@hytech.com' } });
    await c.req('/auth/esqueci', { metodo: 'POST', corpo: { email: 'admin@hytech.com' } });
    assert.equal(c.email.enviados.length, n);
  });

  await t.test('redefinir: nova curta, código errado, ok; vale para aluno e professor', async () => {
    const cod = c.email.ultimoCodigo('aluno@hytech.com');
    const red = (corpo) => c.req('/auth/esqueci/redefinir', { metodo: 'POST', corpo: { email: 'aluno@hytech.com', ...corpo } });
    assert.equal((await red({ codigo: cod, novaSenha: '123' })).status, 400);
    assert.equal((await red({ codigo: cod === '123456' ? '654321' : '123456', novaSenha: 'novasenha99' })).status, 400);
    assert.equal((await red({ codigo: cod, novaSenha: 'novasenha99' })).status, 200);
    assert.equal((await c.req('/auth/login', { metodo: 'POST', corpo: { email: 'aluno@hytech.com', senha: SENHA } })).status, 401);
    assert.equal((await c.req('/auth/login', { metodo: 'POST', corpo: { email: 'aluno@hytech.com', senha: 'novasenha99' } })).status, 200);
    assert.equal((await red({ codigo: cod, novaSenha: 'outra12345' })).status, 400);       // código de uso único

    await c.req('/auth/esqueci', { metodo: 'POST', corpo: { email: 'professor@hytech.com' } });
    const p = await c.req('/auth/esqueci/redefinir', { metodo: 'POST', corpo: { email: 'professor@hytech.com', codigo: c.email.ultimoCodigo('professor@hytech.com'), novaSenha: 'profnova123' } });
    assert.equal(p.status, 200);
    assert.equal((await c.req('/auth/login', { metodo: 'POST', corpo: { email: 'professor@hytech.com', senha: 'profnova123' } })).status, 200);
  });

  await t.test('código de OUTRA finalidade/e-mail não serve', async () => {
    await c.req('/auth/esqueci', { metodo: 'POST', corpo: { email: 'professor@hytech.com' } });   // (pode cair no limite de 30 s: ok)
    c.db.avancarTempo(31);
    await c.req('/auth/esqueci', { metodo: 'POST', corpo: { email: 'professor@hytech.com' } });
    const codProf = c.email.ultimoCodigo('professor@hytech.com');
    const r = await c.req('/auth/esqueci/redefinir', { metodo: 'POST', corpo: { email: 'aluno@hytech.com', codigo: codProf, novaSenha: 'hackeado123' } });
    assert.equal(r.status, 400);
    assert.equal((await c.req('/auth/cadastro/confirmar', { metodo: 'POST', corpo: { email: 'professor@hytech.com', codigo: codProf } })).status, 400);
  });
});

test('Fase 7 — trocar e-mail e reenviar código (usuário logado)', async (t) => {
  const c = await subir(); t.after(c.fechar);
  const tk = await c.login('aluno@hytech.com');

  await t.test('exige login, e-mail válido, diferente, livre e a senha correta', async () => {
    assert.equal((await c.req('/conta/email', { metodo: 'POST', corpo: { novoEmail: 'a@b.com', senha: SENHA } })).status, 401);
    const pedir = (corpo) => c.req('/conta/email', { metodo: 'POST', token: tk, corpo });
    assert.equal((await pedir({ novoEmail: 'ruim', senha: SENHA })).status, 400);
    assert.equal((await pedir({ novoEmail: 'aluno@hytech.com', senha: SENHA })).status, 400);
    assert.equal((await pedir({ novoEmail: 'professor@hytech.com', senha: SENHA })).json.erro, 'Este e-mail já está em uso.');
    assert.equal((await pedir({ novoEmail: 'novo@x.com', senha: 'errada123' })).json.erro, 'Senha incorreta.');
    assert.equal(c.email.enviados.length, 0);
  });

  await t.test('o código vai para o NOVO e-mail; só depois do código o e-mail muda; token novo', async () => {
    const r = await c.req('/conta/email', { metodo: 'POST', token: tk, corpo: { novoEmail: 'Novo@X.com', senha: SENHA } });
    assert.equal(r.status, 200); assert.equal(r.json.email, 'n***@x.com');
    assert.equal(c.email.enviados.pop().para, 'novo@x.com');
    assert.equal(c.db.usuarios.find(u => u.id_usuario === 3).email, 'aluno@hytech.com');
    c.db.avancarTempo(31);
    assert.equal((await c.req('/conta/reenviar', { metodo: 'POST', token: tk, corpo: { finalidade: 'email' } })).status, 200);
    const cod = c.email.ultimoCodigo('novo@x.com');
    assert.equal((await c.req('/conta/email/confirmar', { metodo: 'POST', token: tk, corpo: { codigo: cod === '000000' ? '111111' : '000000' } })).status, 400);
    const ok = await c.req('/conta/email/confirmar', { metodo: 'POST', token: tk, corpo: { codigo: cod } });
    assert.equal(ok.status, 200); assert.equal(ok.json.perfil.email, 'novo@x.com');
    assert.equal((await c.req('/auth/me', { token: ok.json.token })).json.email, 'novo@x.com');
    assert.ok(await c.login('novo@x.com'));
  });

  await t.test('o código de e-mail de um usuário não serve para outro; reenviar sem pedido => 400', async () => {
    const tp = await c.login('professor@hytech.com');
    assert.equal((await c.req('/conta/reenviar', { metodo: 'POST', token: tp, corpo: { finalidade: 'email' } })).status, 400);
    assert.equal((await c.req('/conta/reenviar', { metodo: 'POST', token: tp, corpo: { finalidade: 'xxx' } })).status, 400);
    assert.equal((await c.req('/conta/email/confirmar', { metodo: 'POST', token: tp, corpo: { codigo: '123456' } })).status, 400);
  });
});

test('Fase 7 — desativar conta (LGPD: dados ficam, status vira inativo)', async (t) => {
  const c = await subir(); t.after(c.fechar);

  await t.test('exige login; professor recebe 403 (fala com o admin)', async () => {
    assert.equal((await c.req('/conta/desativar', { metodo: 'POST', corpo: {} })).status, 401);
    const r = await c.req('/conta/desativar', { metodo: 'POST', token: await c.login('professor@hytech.com'), corpo: { nickname: 'profsilva', senha: SENHA } });
    assert.equal(r.status, 403);
    assert.equal(c.db.usuarios.find(u => u.email === 'professor@hytech.com').status, 'ativo');
  });

  await t.test('nickname e senha precisam conferir', async () => {
    const tk = await c.login('aluno@hytech.com');
    const d = (corpo) => c.req('/conta/desativar', { metodo: 'POST', token: tk, corpo });
    assert.match((await d({ nickname: 'outro', senha: SENHA })).json.erro, /não confere/);
    assert.equal((await d({ nickname: 'PedroBala', senha: 'errada123' })).json.erro, 'Senha incorreta.');
    assert.equal(c.db.usuarios.find(u => u.email === 'aluno@hytech.com').status, 'ativo');
  });

  await t.test('desativa: não apaga ninguém, e o login passa a ser recusado', async () => {
    const tk = await c.login('aluno@hytech.com');
    const total = c.db.usuarios.length;
    const r = await c.req('/conta/desativar', { metodo: 'POST', token: tk, corpo: { nickname: '@pedrobala', senha: SENHA } });
    assert.equal(r.status, 200);
    assert.equal(c.db.usuarios.length, total);                                   // nada foi apagado
    assert.equal(c.db.usuarios.find(u => u.email === 'aluno@hytech.com').status, 'inativo');
    const l = await c.req('/auth/login', { metodo: 'POST', corpo: { email: 'aluno@hytech.com', senha: SENHA } });
    assert.equal(l.status, 403); assert.match(l.json.erro, /desativada/);
    // e não recebe código de recuperação enquanto estiver desativada
    const n = c.email.enviados.length;
    await c.req('/auth/esqueci', { metodo: 'POST', corpo: { email: 'aluno@hytech.com' } });
    assert.equal(c.email.enviados.length, n);
  });
});

test('Fase 7 — e-mail: modo console e falha de envio', async (t) => {
  await t.test('config: sem SMTP => modo console; com SMTP => smtp', () => {
    assert.equal(carregarConfig({ JWT_KEY: 'x'.repeat(40) }).email.modo, 'console');
    const g = carregarConfig({ JWT_KEY: 'x'.repeat(40), SMTP_HOST: 'smtp.gmail.com', SMTP_USER: 'a@gmail.com', SMTP_PASS: 'abcd efgh' });
    assert.equal(g.email.modo, 'smtp'); assert.equal(g.email.porta, 465); assert.equal(g.email.seguro, true);
    assert.equal(g.email.de, 'HyTech <a@gmail.com>');
  });

  await t.test('falha ao enviar => 503 com mensagem amigável (sem detalhes internos)', async () => {
    const db = criarBancoFalso({ senhaPadrao: SENHA });
    const quebrado = { modo: 'x', async enviar() { throw new Error('SMTP caiu: senha=segredo'); } };
    const server = criarApp({ cfg, db, email: quebrado }).listen(0);
    const origErro = console.error; console.error = () => {};
    try {
      const r = await fetch(`http://127.0.0.1:${server.address().port}/auth/cadastro`, { method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ nome: 'A B', nickname: 'ab1', email: 'ab1@x.com', senha: SENHA }) });
      const j = await r.json();
      assert.equal(r.status, 503); assert.ok(!JSON.stringify(j).includes('segredo'));
    } finally { console.error = origErro; server.close(); }
  });
});
