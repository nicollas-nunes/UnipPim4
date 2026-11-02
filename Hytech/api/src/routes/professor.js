// Área do professor: criar/editar/enviar/excluir aula, questão e desafio; perfil; desempenho dos alunos.
// O id do professor vem SEMPRE do token. Quem aprova é o admin (HytechAdmin-WinForms).
const express = require('express');
const rateLimit = require('express-rate-limit');
const { idInt } = require('./plataforma');
const { textoParaHtml, htmlParaTexto } = require('../texto');

const DIFICULDADES = ['Fácil', 'Médio', 'Difícil'];
const TIPOS = { aulas: 'aula', questoes: 'questao', desafios: 'desafio' };
const aparar = (s, n) => String(s ?? '').trim().slice(0, n);

module.exports = function rotasProfessor({ db, auth, cfg }) {
  const r = express.Router();
  r.use(auth.exigirLogin, auth.exigirPapel('professor'));

  const limiteEscrita = rateLimit({
    windowMs: 60 * 60 * 1000, limit: cfg.limiteConteudoPorHora || 120, standardHeaders: true, legacyHeaders: false,
    keyGenerator: (req) => 'p' + req.usuario.id_usuario,
    validate: { keyGeneratorIpFallback: false },
    message: { erro: 'Muitas alterações em pouco tempo. Tente novamente mais tarde.' },
  });

  async function perfil(req) {
    const p = (await db.exec('sp_WebObterPerfilProfessor', { id_usuario: req.usuario.id_usuario }))[0];
    if (!p) { const e = new Error('Professor não encontrado.'); e.status = 404; throw e; }
    return p;
  }
  // O professor só cria conteúdo da própria especialidade.
  async function checarMateria(req, idMateria) {
    const p = await perfil(req);
    if (!p.id_materia_especialidade) return 'Peça ao administrador para definir sua especialidade.';
    if (Number(idMateria) !== Number(p.id_materia_especialidade)) return 'Você só pode criar conteúdo da sua especialidade.';
    return null;
  }

  r.get('/perfil', async (req, res) => {
    const p = await perfil(req);
    res.json({
      nome: p.nome, nickname: p.nickname, email: p.email, foto: p.foto_url || null,
      especialidade: p.especialidade || '', idMateriaEspecialidade: p.id_materia_especialidade || null,
      resumo: { enviados: p.enviados, aprovados: p.aprovados, aguardando: p.pendentes, alunosAtivos: p.alunos_ativos },
    });
  });

  r.get('/meus', async (req, res) => {
    const [aulas = [], questoes = [], alts = [], desafios = []] =
      await db.execMulti('sp_WebListarMeusConteudos', { id_professor: req.usuario.id_professor });
    res.json({
      aulas: aulas.map(a => ({
        id: a.id_aula, idMateria: a.id_materia, materia: a.materia, topico: a.topico || '', titulo: a.titulo,
        texto: htmlParaTexto(a.conteudo), codigo: a.exemplo_codigo || '', dicaTitulo: a.dica_titulo || '',
        dica: a.dica_corpo || '', status: a.status, data: a.data || null, motivo: a.motivo || null,
      })),
      questoes: questoes.map(q => {
        const lista = alts.filter(x => x.id_questao === q.id_questao);
        return {
          id: q.id_questao, idMateria: q.id_materia, materia: q.materia, enunciado: q.enunciado,
          dificuldade: q.dificuldade, codigo: q.codigo_exemplo || '', status: q.status, data: q.data || null,
          motivo: q.motivo || null, alternativas: lista.map(x => x.texto), indiceCorreta: lista.findIndex(x => !!x.correta),
        };
      }),
      desafios: desafios.map(d => ({
        id: d.id_desafio, idMateria: d.id_materia, materia: d.materia, titulo: d.titulo, enunciado: d.enunciado || '',
        dica: d.dica || '', dificuldade: d.dificuldade, codigo: d.codigo_base || '', status: d.status,
        data: d.data || null, motivo: d.motivo || null,
      })),
    });
  });

  // ── Aulas ──
  function lerAula(b) {
    const titulo = String(b.titulo ?? '').trim();
    if (!titulo || titulo.length > 100) return { erro: 'Informe o título (até 100 caracteres).' };
    const topico = String(b.topico ?? '').trim();
    if (topico.length > 150) return { erro: 'Tópico muito longo (até 150 caracteres).' };
    const texto = String(b.texto ?? '');
    if (texto.length > 20000) return { erro: 'Texto muito longo (até 20.000 caracteres).' };
    const idMateria = idInt(b.idMateria);
    if (!idMateria) return { erro: 'Escolha a matéria.' };
    return { v: { idMateria, titulo, topico: topico || null, conteudo: textoParaHtml(texto),
      exemplo: aparar(b.codigo, 5000) || null, dicaTitulo: aparar(b.dicaTitulo, 150) || null, dica: aparar(b.dica, 1000) || null } };
  }
  async function salvarAula(req, res, idAula) {
    const { v, erro } = lerAula(req.body || {});
    if (erro) return res.status(400).json({ erro });
    const e2 = await checarMateria(req, v.idMateria);
    if (e2) return res.status(403).json({ erro: e2 });
    const x = (await db.exec('sp_WebSalvarAula', {
      id_aula: idAula, id_professor: req.usuario.id_professor, id_materia: v.idMateria, titulo: v.titulo,
      topico: v.topico, conteudo: v.conteudo, exemplo_codigo: v.exemplo, dica_titulo: v.dicaTitulo,
      dica_corpo: v.dica, enviar: req.body?.enviar ? 1 : 0,
    }))[0];
    res.status(idAula ? 200 : 201).json({ id: x.id_aula });
  }
  r.post('/aulas', limiteEscrita, (req, res) => salvarAula(req, res, null));
  r.put('/aulas/:id', limiteEscrita, (req, res) => {
    const id = idInt(req.params.id); if (!id) return res.status(400).json({ erro: 'Aula inválida.' });
    return salvarAula(req, res, id);
  });

  // ── Questões ──
  function lerQuestao(b) {
    const enunciado = String(b.enunciado ?? '').trim();
    if (!enunciado || enunciado.length > 2000) return { erro: 'Informe o enunciado (até 2000 caracteres).' };
    if (!DIFICULDADES.includes(b.dificuldade)) return { erro: 'Escolha a dificuldade.' };
    const idMateria = idInt(b.idMateria); if (!idMateria) return { erro: 'Escolha a matéria.' };
    const alts = Array.isArray(b.alternativas) ? b.alternativas.map(a => String(a ?? '').trim()) : [];
    if (alts.length > 5 || alts.some(a => a.length > 255)) return { erro: 'No máximo 5 alternativas de até 255 caracteres.' };
    // NÃO remover as vazias: o índice da correta é a posição original.
    const idx = Number.isInteger(b.indiceCorreta) ? b.indiceCorreta : -1;
    return { v: { idMateria, enunciado, dif: b.dificuldade, alts, idx, codigo: aparar(b.codigo, 5000) || null } };
  }
  async function salvarQuestao(req, res, idQuestao) {
    const { v, erro } = lerQuestao(req.body || {}); if (erro) return res.status(400).json({ erro });
    const e2 = await checarMateria(req, v.idMateria); if (e2) return res.status(403).json({ erro: e2 });
    const x = (await db.exec('sp_SalvarQuestao', {
      id_questao: idQuestao, id_professor: req.usuario.id_professor, id_materia: v.idMateria, enunciado: v.enunciado,
      dificuldade: v.dif, codigo_exemplo: v.codigo, alternativas_json: JSON.stringify(v.alts),
      indice_correta: v.idx, enviar: req.body?.enviar ? 1 : 0,
    }))[0];
    res.status(idQuestao ? 200 : 201).json({ id: x.id_questao });
  }
  r.post('/questoes', limiteEscrita, (req, res) => salvarQuestao(req, res, null));
  r.put('/questoes/:id', limiteEscrita, (req, res) => {
    const id = idInt(req.params.id); if (!id) return res.status(400).json({ erro: 'Questão inválida.' });
    return salvarQuestao(req, res, id);
  });

  // ── Desafios ──
  function lerDesafio(b) {
    const titulo = String(b.titulo ?? '').trim(), enunciado = String(b.enunciado ?? '').trim();
    if (!titulo || titulo.length > 150) return { erro: 'Informe o título (até 150 caracteres).' };
    if (!enunciado || enunciado.length > 4000) return { erro: 'Informe o enunciado (até 4000 caracteres).' };
    if (!DIFICULDADES.includes(b.dificuldade)) return { erro: 'Escolha a dificuldade.' };
    const idMateria = idInt(b.idMateria); if (!idMateria) return { erro: 'Escolha a matéria.' };
    return { v: { idMateria, titulo, enunciado, dif: b.dificuldade, codigo: aparar(b.codigo, 5000) || null, dica: aparar(b.dica, 1000) || null } };
  }
  async function salvarDesafio(req, res, idDesafio) {
    const { v, erro } = lerDesafio(req.body || {}); if (erro) return res.status(400).json({ erro });
    const e2 = await checarMateria(req, v.idMateria); if (e2) return res.status(403).json({ erro: e2 });
    const x = (await db.exec('sp_SalvarDesafio', {
      id_desafio: idDesafio, id_professor: req.usuario.id_professor, id_materia: v.idMateria, titulo: v.titulo,
      enunciado: v.enunciado, dificuldade: v.dif, codigo_base: v.codigo, dica: v.dica, enviar: req.body?.enviar ? 1 : 0,
    }))[0];
    res.status(idDesafio ? 200 : 201).json({ id: x.id_desafio });
  }
  r.post('/desafios', limiteEscrita, (req, res) => salvarDesafio(req, res, null));
  r.put('/desafios/:id', limiteEscrita, (req, res) => {
    const id = idInt(req.params.id); if (!id) return res.status(400).json({ erro: 'Desafio inválido.' });
    return salvarDesafio(req, res, id);
  });

  // ── Enviar para aprovação / excluir (aula | questao | desafio) ──
  r.post('/:tipo/:id/enviar', limiteEscrita, async (req, res) => {
    const tipo = TIPOS[req.params.tipo], id = idInt(req.params.id);
    if (!tipo || !id) return res.status(400).json({ erro: 'Item inválido.' });
    await db.exec('sp_EnviarParaAprovacao', { tipo, id, id_professor: req.usuario.id_professor });
    res.json({ ok: true });
  });
  r.delete('/:tipo/:id', limiteEscrita, async (req, res) => {
    const tipo = TIPOS[req.params.tipo], id = idInt(req.params.id);
    if (!tipo || !id) return res.status(400).json({ erro: 'Item inválido.' });
    await db.exec('sp_ExcluirConteudoProfessor', { tipo, id, id_professor: req.usuario.id_professor });
    res.json({ ok: true });
  });

  // ── Desempenho dos alunos (padrão: matéria da especialidade; ?materia=todas ou ?materia=<id>) ──
  r.get('/desempenho', async (req, res) => {
    const p = await perfil(req);
    const q = req.query.materia;
    const idMateria = q === 'todas' ? null : (idInt(q) || p.id_materia_especialidade || null);
    const lista = await db.exec('sp_DesempenhoAlunos', { id_materia: idMateria });
    res.json(lista.map(a => ({ nome: a.nome, email: a.email, chaves: a.chaves, fases: a.fases,
      acerto: a.acerto, certificados: a.certs, ultimaAtividade: a.ultima || null })));
  });

  return r;
};
