const express = require('express');

// "Banco de Dados" -> "banco-de-dados" (= assets/materias/banco-de-dados.webp)
function slug(texto) {
  return String(texto || '').normalize('NFD').replace(/[\u0300-\u036f]/g, '')
    .toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '');
}

// Aceita só inteiro positivo (ids vindos da URL).
function idInt(v) {
  const n = Number(v);
  return Number.isInteger(n) && n > 0 ? n : null;
}

module.exports = function rotasPlataforma({ db, auth }) {
  const r = express.Router();

  // Valores de gamificação definidos pelo admin (ChavesConfig). O web LÊ daqui.
  r.get('/config/chaves', auth.exigirLogin, async (_req, res) => {
    const c = (await db.exec('sp_ObterConfiguracaoPlataforma'))[0];
    if (!c) return res.status(404).json({ erro: 'Configuração não encontrada.' });
    res.json({
      questaoFacil: c.chaves_questao_facil,
      questaoMedia: c.chaves_questao_media,
      questaoDificil: c.chaves_questao_dificil,
      certificado: c.chaves_certificado,
    });
  });

  // Matérias, com slug (imagens do web), ordem e custo em chaves para desbloquear.
  r.get('/materias', auth.exigirLogin, async (_req, res) => {
    const lista = await db.exec('sp_WebListarMaterias');
    res.json(lista.map(m => ({
      id: m.id_materia, titulo: m.titulo, slug: slug(m.titulo),
      ordem: m.ordem, chavesParaDesbloquear: m.chaves_para_desbloquear,
    })));
  });

  // Aulas (tópicos) aprovadas da matéria, na ordem. Extras vêm por último (isExtra = true).
  r.get('/materias/:id/aulas', auth.exigirLogin, async (req, res) => {
    const id = idInt(req.params.id);
    if (!id) return res.status(400).json({ erro: 'Matéria inválida.' });
    const aulas = await db.exec('sp_WebListarAulasMateria', { id_materia: id });
    res.json(aulas.map(a => ({
      id: a.id_aula, topico: a.topico, titulo: a.titulo, conteudo: a.conteudo,
      exemplo: a.exemplo_codigo, dica: { titulo: a.dica_titulo, corpo: a.dica_corpo },
      ordem: a.ordem, isExtra: !!a.is_extra, chaves: a.chaves_recompensa, autor: a.autor || null, autor: a.autor || null,
    })));
  });

  // Questões aprovadas da matéria com as alternativas. NUNCA inclui o gabarito:
  // a resposta é conferida no banco por POST /aluno/questoes/:id/responder.
  r.get('/materias/:id/questoes', auth.exigirLogin, async (req, res) => {
    const id = idInt(req.params.id);
    if (!id) return res.status(400).json({ erro: 'Matéria inválida.' });
    const [questoes, alts] = await Promise.all([
      db.exec('sp_WebListarQuestoesMateria', { id_materia: id }),
      db.exec('sp_WebListarAlternativasMateria', { id_materia: id }),
    ]);
    res.json(questoes.map(q => ({
      id: q.id_questao, pergunta: q.enunciado, codigo: q.codigo_exemplo || '',
      dificuldade: q.dificuldade, chaves: q.chaves_recompensa,
      alternativas: alts.filter(a => a.id_questao === q.id_questao).map(a => ({ id: a.id_alternativa, texto: a.texto })),
    })));
  });

  // Ranking real (alunos ativos, por pontos acumulados; empate = mesma posição).
  r.get('/ranking', auth.exigirLogin, async (req, res) => {
    const top = Math.min(Math.max(parseInt(req.query.top, 10) || 10, 1), 50);
    const lista = await db.exec('sp_RankingAlunos', { top });
    res.json(lista.map(a => ({
      posicao: a.posicao, nome: a.nome, nickname: a.nickname, foto: a.foto_url || null,
      pontos: a.pontos_acumulados, streak: a.streak_atual, fases: a.fases_concluidas,
      souEu: a.nickname === req.usuario.nickname,
    })));
  });

  return r;
};
module.exports.slug = slug;
module.exports.idInt = idInt;
