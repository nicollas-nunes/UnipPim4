// Testes com banco FALSO (sem SQL Server). Rodar: npm test
const test = require('node:test');
const assert = require('node:assert');
const bcrypt = require('bcryptjs');
const { criarApp } = require('../src/app');
const { ServicoError } = require('../src/db');
const { carregarConfig } = require('../src/config');

const cfg = carregarConfig({ JWT_KEY: 'x'.repeat(40), CORS_ORIGINS: 'http://localhost:5500', AUTH_LIMITE_POR_MINUTO: '1000' });
const hashAluno = bcrypt.hashSync('senhaforte1', 4);

const usuarios = {
  'aluno@hytech.com': { id_usuario: 3, nome: 'Pedro Bala', nickname: '@PedroBala', email: 'aluno@hytech.com', status: 'ativo', papel: 'aluno', id_aluno: 7, id_professor: null, senha_hash: hashAluno },
  'prof@hytech.com':  { id_usuario: 2, nome: 'Prof. Silva', nickname: '@profsilva', email: 'prof@hytech.com', status: 'ativo', papel: 'professor', id_aluno: null, id_professor: 4, senha_hash: hashAluno },
  'admin@hytech.com': { id_usuario: 1, nome: 'Admin', nickname: '@admin', email: 'admin@hytech.com', status: 'ativo', papel: 'admin', senha_hash: hashAluno },
  'off@hytech.com':   { id_usuario: 9, nome: 'Off', nickname: '@off', email: 'off@hytech.com', status: 'inativo', papel: 'aluno', id_aluno: 8, senha_hash: hashAluno },
};
const chamadas = [];
const dbFalso = {
  async exec(proc, p = {}) {
    chamadas.push([proc, p]);
    switch (proc) {
      case 'sp_ObterUsuarioParaLogin': return usuarios[p.email.toLowerCase()] ? [usuarios[p.email.toLowerCase()]] : [];
      case 'sp_RegistrarLogin': return [];
      case 'sp_CadastrarAluno':
        if (p.email === 'repetido@hytech.com') throw new ServicoError('Este e-mail já está cadastrado.');
        return [{ id_usuario: 50, id_aluno: 20 }];
      case 'sp_ObterConfiguracaoPlataforma': return [{ chaves_questao_facil: 5, chaves_questao_media: 10, chaves_questao_dificil: 15, chaves_certificado: 50 }];
      case 'sp_ListarMaterias': return [{ id_materia: 1, titulo: 'Programação' }, { id_materia: 3, titulo: 'Banco de Dados' }];
      default: throw new Error('procedure inesperada ' + proc);
    }
  },
};

async function subir() {
  const app = criarApp({ cfg, db: dbFalso });
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
  return { req, fechar: () => server.close() };
}

test('API Hytech (fase 1)', async (t) => {
  const { req, fechar } = await subir();
  t.after(fechar);

  await t.test('login ok devolve token e usuário (aluno)', async () => {
    const r = await req('/auth/login', { metodo: 'POST', corpo: { email: 'ALUNO@hytech.com ', senha: 'senhaforte1' } });
    assert.equal(r.status, 200);
    assert.ok(r.json.token);
    assert.equal(r.json.usuario.papel, 'aluno');
    assert.equal(r.json.usuario.id_aluno, 7);
    assert.ok(chamadas.some(c => c[0] === 'sp_RegistrarLogin'));
  });

  await t.test('senha errada e e-mail inexistente dão a MESMA resposta 401', async () => {
    const a = await req('/auth/login', { metodo: 'POST', corpo: { email: 'aluno@hytech.com', senha: 'errada123' } });
    const b = await req('/auth/login', { metodo: 'POST', corpo: { email: 'nao@existe.com', senha: 'errada123' } });
    assert.equal(a.status, 401); assert.equal(b.status, 401);
    assert.deepEqual(a.json, b.json);
  });

  await t.test('conta inativa => 403 (só depois de acertar a senha)', async () => {
    const r = await req('/auth/login', { metodo: 'POST', corpo: { email: 'off@hytech.com', senha: 'senhaforte1' } });
    assert.equal(r.status, 403);
    const r2 = await req('/auth/login', { metodo: 'POST', corpo: { email: 'off@hytech.com', senha: 'errada123' } });
    assert.equal(r2.status, 401);
  });

  await t.test('admin NÃO entra pela web', async () => {
    const r = await req('/auth/login', { metodo: 'POST', corpo: { email: 'admin@hytech.com', senha: 'senhaforte1' } });
    assert.equal(r.status, 403);
    assert.match(r.json.erro, /HytechAdmin/);
  });

  await t.test('cadastro valida campos e cria só aluno', async () => {
    const base = { nome: 'Maria Souza', nickname: 'maria', email: 'maria@x.com', senha: 'senhaforte1' };
    assert.equal((await req('/auth/cadastro', { metodo: 'POST', corpo: { ...base, senha: '123' } })).status, 400);
    assert.equal((await req('/auth/cadastro', { metodo: 'POST', corpo: { ...base, email: 'ruim' } })).status, 400);
    assert.equal((await req('/auth/cadastro', { metodo: 'POST', corpo: { ...base, dataNascimento: '2999-01-01' } })).status, 400);
    const ok = await req('/auth/cadastro', { metodo: 'POST', corpo: { ...base, dataNascimento: '2008-05-20' } });
    assert.equal(ok.status, 201);
    assert.equal(ok.json.id_aluno, 20);
    const proc = chamadas.filter(c => c[0] === 'sp_CadastrarAluno').pop();
    assert.ok(bcrypt.compareSync('senhaforte1', proc[1].senha_hash), 'a senha vai como hash, nunca em texto');
    assert.equal(proc[1].senha, undefined);
  });

  await t.test('regra do banco (RAISERROR) vira 400 com a mensagem', async () => {
    const r = await req('/auth/cadastro', { metodo: 'POST', corpo: { nome: 'A B', nickname: 'ab', email: 'repetido@hytech.com', senha: 'senhaforte1' } });
    assert.equal(r.status, 400);
    assert.equal(r.json.erro, 'Este e-mail já está cadastrado.');
  });

  await t.test('rotas protegidas exigem token; com token funcionam', async () => {
    assert.equal((await req('/materias')).status, 401);
    assert.equal((await req('/materias', { token: 'lixo' })).status, 401);
    const { json: { token } } = await req('/auth/login', { metodo: 'POST', corpo: { email: 'prof@hytech.com', senha: 'senhaforte1' } });
    const me = await req('/auth/me', { token });
    assert.equal(me.json.papel, 'professor'); assert.equal(me.json.id_professor, 4);
    const m = await req('/materias', { token });
    assert.deepEqual(m.json.map(x => x.slug), ['programacao', 'banco-de-dados']);
    const c = await req('/config/chaves', { token });
    assert.deepEqual(c.json, { questaoFacil: 5, questaoMedia: 10, questaoDificil: 15, certificado: 50 });
  });

  await t.test('rota inexistente => 404', async () => {
    assert.equal((await req('/nada')).status, 404);
  });

  await t.test('limite de tentativas: 429 depois de estourar', async () => {
    const app = criarApp({ cfg: { ...cfg, limiteAuthPorMinuto: 3 }, db: dbFalso });
    const srv = app.listen(0);
    const url = `http://127.0.0.1:${srv.address().port}/auth/login`;
    const codigos = [];
    for (let i = 0; i < 5; i++) {
      const r = await fetch(url, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ email: 'x@x.com', senha: 'errada123' }) });
      codigos.push(r.status);
    }
    srv.close();
    assert.deepEqual(codigos, [401, 401, 401, 429, 429]);
  });
});
