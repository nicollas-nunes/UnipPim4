const express = require('express');
const rateLimit = require('express-rate-limit');
const { idInt } = require('./plataforma');

// Tickets de suporte: o aluno abre, o professor (escolhido pelo banco) responde e encerra.
// Aluno e professor usam as mesmas rotas; o banco só deixa participante ler/escrever.
module.exports = function rotasTickets({ db, auth, cfg }) {
  const r = express.Router();
  r.use(auth.exigirLogin, auth.exigirPapel('aluno', 'professor'));

  // Limite de escrita por usuário (aluno/professor logado), não por IP.
  const limiteEscrita = rateLimit({
    windowMs: 60 * 60 * 1000, limit: cfg.limiteTicketsPorHora, standardHeaders: true, legacyHeaders: false,
    keyGenerator: (req) => 'u' + req.usuario.id_usuario,
    validate: { keyGeneratorIpFallback: false },
    message: { erro: 'Muitas mensagens em pouco tempo. Tente novamente mais tarde.' },
  });

  const formatar = (t, meuId) => ({
    id: t.id_ticket, assunto: t.assunto, status: t.status,
    criadoEm: t.data_abertura, ultimaAtividade: t.ultima_atividade || t.data_abertura,
    idMateria: t.id_materia, materia: t.materia || '',
    aluno: t.solicitante, professor: t.destinatario, papelDestinatario: t.papel_destinatario,
    souSolicitante: t.id_usuario_solicitante === meuId,
  });

  // Lista: aluno vê os que abriu; professor, os que recebeu.
  r.get('/', async (req, res) => {
    const lista = await db.exec('sp_WebListarTickets', { id_usuario: req.usuario.id_usuario });
    res.json(lista.map(t => formatar(t, req.usuario.id_usuario)));
  });

  // Detalhe + conversa (só participante; senão o banco responde 400).
  r.get('/:id', async (req, res) => {
    const id = idInt(req.params.id);
    if (!id) return res.status(400).json({ erro: 'Ticket inválido.' });
    const meu = req.usuario.id_usuario;
    const [t] = await db.exec('sp_WebObterTicket', { id_ticket: id, id_usuario: meu });
    if (!t) return res.status(404).json({ erro: 'Ticket não encontrado.' });
    const msgs = await db.exec('sp_MensagensTicket', { id_ticket: id, id_usuario: meu });
    res.json({
      ...formatar(t, meu),
      mensagens: msgs.map(m => ({
        id: m.id_mensagem, remetente: m.remetente,     // 'aluno' | 'professor': define o lado do balão
        nome: m.nome, papel: m.papel, texto: m.texto, dataHora: m.data_hora,
        minha: m.id_usuario_remetente === meu,
      })),
    });
  });

  // Aluno abre ticket. O banco escolhe o destinatário (professor da matéria; senão o admin).
  r.post('/', auth.exigirPapel('aluno'), limiteEscrita, async (req, res) => {
    const assunto = String(req.body?.assunto ?? '').trim();
    const texto = String(req.body?.mensagem ?? '').trim();
    const idMateria = idInt(req.body?.idMateria);
    if (!assunto || assunto.length > 150) return res.status(400).json({ erro: 'Informe o assunto (até 150 caracteres).' });
    if (!texto || texto.length > 4000) return res.status(400).json({ erro: 'Escreva a mensagem (até 4000 caracteres).' });
    if (!idMateria) return res.status(400).json({ erro: 'Escolha a matéria.' });

    const x = (await db.exec('sp_AbrirTicket', {
      id_usuario_solicitante: req.usuario.id_usuario,        // do TOKEN, nunca do corpo
      assunto, descricao: texto, id_materia: idMateria,
    }))[0];
    res.status(201).json({ id: x.id_ticket });
  });

  // Nova mensagem (aluno ou professor do ticket). O status muda por trigger no banco.
  r.post('/:id/mensagens', limiteEscrita, async (req, res) => {
    const id = idInt(req.params.id);
    const texto = String(req.body?.texto ?? '').trim();
    if (!id) return res.status(400).json({ erro: 'Ticket inválido.' });
    if (!texto || texto.length > 4000) return res.status(400).json({ erro: 'Escreva a mensagem (até 4000 caracteres).' });
    await db.exec('sp_ResponderTicket', { id_ticket: id, id_usuario_remetente: req.usuario.id_usuario, texto });
    res.status(201).json({ ok: true });
  });

  // Só o professor destinatário encerra (o banco confere).
  r.post('/:id/fechar', auth.exigirPapel('professor'), async (req, res) => {
    const id = idInt(req.params.id);
    if (!id) return res.status(400).json({ erro: 'Ticket inválido.' });
    await db.exec('sp_FecharTicket', { id_ticket: id, id_usuario: req.usuario.id_usuario });
    res.json({ ok: true });
  });

  return r;
};
