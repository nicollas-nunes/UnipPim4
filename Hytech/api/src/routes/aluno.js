const express = require('express');
const { idInt } = require('./plataforma');

module.exports = function rotasAluno({ db, auth }) {
  const r = express.Router();
  r.use(auth.exigirLogin, auth.exigirPapel('aluno'));

  // Responde uma questão. Quem diz se acertou (e quantas chaves ganhou) é o BANCO
  // (sp_ResponderQuestao). O id do aluno vem do token, nunca do corpo da requisição.
  r.post('/questoes/:id/responder', async (req, res) => {
    const idQuestao = idInt(req.params.id);
    const idAlternativa = idInt(req.body?.idAlternativa);
    if (!idQuestao || !idAlternativa) return res.status(400).json({ erro: 'Informe a questão e a alternativa.' });

    const x = (await db.exec('sp_ResponderQuestao', {
      id_aluno: req.usuario.id_aluno, id_questao: idQuestao, id_alternativa: idAlternativa,
    }))[0];

    res.json({
      acertou: !!x.acertou,
      jaConcluida: !!x.ja_concluida,
      chavesGanhas: x.chaves_ganhas,
      certificadoEmitido: !!x.certificado_emitido,
      saldoChaves: x.saldo_chaves,
    });
  });

  // ── Estado completo do aluno: tudo o que o site precisa para reconstruir a tela depois do login/F5 ──
  r.get('/estado', async (req, res) => {
    const id = req.usuario.id_aluno;
    const perfil = (await db.exec('sp_ObterPerfilAluno', { id_aluno: id }))[0];
    if (!perfil) return res.status(404).json({ erro: 'Aluno não encontrado.' });
    const [aulas = [], questoes = [], materias = [], certs = []] = await db.execMulti('sp_ProgressoAluno', { id_aluno: id });
    res.json({
      resumo: {
        pontos: perfil.pontos_acumulados,        // total ganho na vida: vale para nível e ranking
        saldoChaves: perfil.chaves_saldo,        // o que ainda pode gastar
        streak: perfil.streak_atual,
        posicao: perfil.posicao_ranking,
        fasesConcluidas: perfil.fases_concluidas,
        questoesConcluidas: perfil.questoes_concluidas,
        certificados: perfil.certificados,
      },
      perfil: { nome: perfil.nome, nickname: perfil.nickname, email: perfil.email, foto: perfil.foto_url || null },
      aulasConcluidas: aulas.map(a => a.id_aula),
      questoesConcluidas: questoes.map(q => q.id_questao),
      materias: materias.map(m => ({ id: m.id_materia, desbloqueada: !!m.desbloqueada })),
      certificados: certs.map(c => ({ idMateria: c.id_materia, dataEmissao: c.data_emissao })),
    });
  });

  // ── "Marcar como lido": o banco credita as chaves da aula (uma vez só) e pode emitir certificado ──
  r.post('/aulas/:id/concluir', async (req, res) => {
    const id = idInt(req.params.id);
    if (!id) return res.status(400).json({ erro: 'Aula inválida.' });
    const x = (await db.exec('sp_ConcluirAula', { id_aluno: req.usuario.id_aluno, id_aula: id }))[0];
    res.json({ chavesGanhas: x.chaves_ganhas, certificadoEmitido: !!x.certificado_emitido, saldoChaves: x.saldo_chaves });
  });

  // ── Desbloquear matéria gastando chaves (o banco confere saldo e se a anterior foi concluída) ──
  r.post('/materias/:id/desbloquear', async (req, res) => {
    const id = idInt(req.params.id);
    if (!id) return res.status(400).json({ erro: 'Matéria inválida.' });
    const x = (await db.exec('sp_DesbloquearMateria', { id_aluno: req.usuario.id_aluno, id_materia: id }))[0];
    res.json({ desbloqueada: !!x.desbloqueada, chavesGastas: x.chaves_gastas, saldoChaves: x.saldo_chaves });
  });

  // ── Desafios dos professores (só os aprovados) ──
  r.get('/desafios', async (req, res) => {
    const lista = await db.exec('sp_WebListarDesafiosAluno', { id_aluno: req.usuario.id_aluno });
    res.json(lista.map(d => ({ id: d.id_desafio, idMateria: d.id_materia, materia: d.materia, titulo: d.titulo,
      enunciado: d.enunciado || '', dica: d.dica || '', dificuldade: d.dificuldade, codigo: d.codigo_base || '',
      autor: d.autor || '', concluido: !!d.concluido })));
  });
  r.post('/desafios/:id/concluir', async (req, res) => {
    const id = idInt(req.params.id); if (!id) return res.status(400).json({ erro: 'Desafio inválido.' });
    const x = (await db.exec('sp_WebConcluirDesafio', { id_aluno: req.usuario.id_aluno, id_desafio: id }))[0];
    res.json({ jaConcluido: !!x.ja_concluido, chavesGanhas: x.chaves_ganhas, saldoChaves: x.saldo_chaves });
  });

  return r;
};
