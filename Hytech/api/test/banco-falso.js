// BANCO FALSO em memória (sem SQL Server): usado pelos testes e por `npm run dev:falso`.
const bcrypt = require('bcryptjs');
const { ServicoError } = require('../src/db');
const conteudo = require('./conteudo-falso.json').M; // mesmo conteúdo que foi importado no SQL

function criarBancoFalso({ senhaPadrao = 'senha1234', custoHash = 4 } = {}) {
  const hash = bcrypt.hashSync(senhaPadrao, custoHash);
  const usuarios = [
    { id_usuario: 3, nome: 'Pedro Bala', nickname: '@PedroBala', email: 'aluno@hytech.com', status: 'ativo', papel: 'aluno', id_aluno: 1, id_professor: null, senha_hash: hash },
    { id_usuario: 2, nome: 'Prof. Silva', nickname: '@profsilva', email: 'professor@hytech.com', status: 'ativo', papel: 'professor', id_aluno: null, id_professor: 1, especialidade: 'Programação', senha_hash: hash },
    { id_usuario: 1, nome: 'Administrador', nickname: '@admin', email: 'admin@hytech.com', status: 'ativo', papel: 'admin', senha_hash: hash },
    { id_usuario: 9, nome: 'Conta Inativa', nickname: '@off', email: 'inativo@hytech.com', status: 'inativo', papel: 'aluno', id_aluno: 8, id_professor: null, senha_hash: hash },
  ];
  let proxId = 100;
  let proxAula = 1, proxQuestao = 1, proxAlt = 1;
  const CHAVES_QUESTAO = 7; // diferente do padrão (5) de propósito: prova que o site lê do banco

  // Monta tabelas a partir do conteúdo importado
  const materias = [], aulas = [], questoes = [], alternativas = [];
  Object.keys(conteudo).sort((a, b) => a - b).forEach(k => {
    const m = conteudo[k];
    const idMat = Number(k);
    materias.push({ id_materia: idMat, titulo: m.nome, ordem: idMat, chaves_para_desbloquear: m.chavesParaDesbloquear });
    m.topicos.forEach((t, i) => {
      const [topico, titulo] = t.titulo.includes('—') ? t.titulo.split('—').map(x => x.trim()) : [null, t.titulo];
      aulas.push({ id_aula: proxAula++, id_materia: idMat, topico, titulo, conteudo: t.conteudo, exemplo_codigo: t.exemplo,
        dica_titulo: t.dica.titulo, dica_corpo: t.dica.corpo, ordem: i + 1, is_extra: t.isExtra ? 1 : 0, chaves_recompensa: t.chaves });
    });
    m.questoes.forEach(q => {
      const id = proxQuestao++;
      questoes.push({ id_questao: id, id_materia: idMat, enunciado: q.pergunta, codigo_exemplo: q.codigo, dificuldade: 'Fácil', chaves_recompensa: CHAVES_QUESTAO });
      q.alternativas.forEach(a => alternativas.push({ id_alternativa: proxAlt++, id_questao: id, texto: a.texto, correta: a.correta }));
    });
  });
  const CHAVES_CERTIFICADO = 50;

  // ── Tickets (espelha TicketSuporte e TicketMensagem) ──
  const tickets = [], mensagens = [];
  let proxTicket = 1, proxMsg = 1;
  const papelDe = (id) => (usuarios.find(u => u.id_usuario === id) || {}).papel || '';
  const nomeDe = (id) => (usuarios.find(u => u.id_usuario === id) || {}).nome || '';
  const participa = (t, uid) => t && (t.id_usuario_solicitante === uid || t.id_usuario_destinatario === uid);
  const ultimaAtividade = (t) => mensagens.filter(m => m.id_ticket === t.id_ticket).map(m => m.data_hora).sort().pop() || t.data_abertura;
  const cabecalho = (t) => ({
    id_ticket: t.id_ticket, assunto: t.assunto, status: t.status, data_abertura: t.data_abertura,
    id_materia: t.id_materia, materia: (materias.find(m => m.id_materia === t.id_materia) || {}).titulo || null,
    id_usuario_solicitante: t.id_usuario_solicitante, solicitante: nomeDe(t.id_usuario_solicitante),
    id_usuario_destinatario: t.id_usuario_destinatario, destinatario: nomeDe(t.id_usuario_destinatario),
    papel_destinatario: papelDe(t.id_usuario_destinatario), ultima_atividade: ultimaAtividade(t),
  });
  let relogio = Date.now();
  const agora = () => new Date(relogio += 1000).toISOString();   // relógio que sempre avança (ordem estável nos testes)
  const chamadas = [];

  // ── Estado de cada aluno (espelha as tabelas Aluno, Aluno_Aula, Aluno_Questao, Aluno_Materia, Certificado) ──
  const alunos = {};
  const estadoDe = (id) => (alunos[id] ||= { pontos: 0, gastas: 0, aulas: new Set(), questoes: new Set(), desbloq: new Set(), certs: new Map(), streak: 0 });
  const saldo = (id) => estadoDe(id).pontos - estadoDe(id).gastas;
  const usuarioDoAluno = (id) => usuarios.find(u => u.id_aluno === id);

  // Alunos fictícios para o ranking não ficar vazio (só no banco falso)
  [['Alberto Caeiro', 76], ['Álvaro de Campos', 62], ['Ricardo Reis', 51], ['Bernardo Soares', 39], ['Charles R. Anon', 24]].forEach(([nome, pts], i) => {
    const id = 20 + i;
    usuarios.push({ id_usuario: 200 + i, nome, nickname: '@' + nome.split(' ')[0].toLowerCase(), email: `ranking${i}@hytech.com`, status: 'ativo', papel: 'aluno', id_aluno: id, id_professor: null, senha_hash: hash });
    estadoDe(id).pontos = pts;
  });

  // Regra do sp_ProcessarCertificados: todas as aulas NÃO extras + todas as questões da matéria
  function tentarCertificado(idAluno, idMateria) {
    const e = estadoDe(idAluno);
    if (e.certs.has(idMateria)) return false;
    const aulasObrig = aulas.filter(a => a.id_materia === idMateria && !a.is_extra);
    const questoesMat = questoes.filter(q => q.id_materia === idMateria);
    if (!aulasObrig.length) return false;
    if (!aulasObrig.every(a => e.aulas.has(a.id_aula))) return false;
    if (!questoesMat.every(q => e.questoes.has(q.id_questao))) return false;
    e.certs.set(idMateria, new Date().toISOString());
    e.pontos += CHAVES_CERTIFICADO;
    return true;
  }
  const fases = (e) => e.aulas.size + e.questoes.size;
  const posicao = (id) => 1 + Object.entries(alunos).filter(([k, v]) => { const u = usuarioDoAluno(Number(k)); return u && u.status === 'ativo' && v.pontos > estadoDe(id).pontos; }).length;
  const desbloqueada = (e, m) => m.chaves_para_desbloquear === 0 || e.desbloq.has(m.id_materia);


  // ── Conteúdo do professor (Fase 5): Aula/Questao/Desafio com status e Aprovacao ──
  const desafios = []; let proxDesafio = 1;
  const alunoDesafio = new Set();           // "idAluno:idDesafio"
  const CHAVES_DESAFIO = { 'Fácil': 5, 'Médio': 10, 'Difícil': 15 };
  const aprovado = (x) => (x.status_aprovacao || x.status || 'aprovado') === 'aprovado';
  const stDe = (x) => x.status_aprovacao || x.status || 'aprovado';
  const setSt = (x, v) => { if ('status' in x && !('status_aprovacao' in x)) x.status = v; else x.status_aprovacao = v; };
  const lista = { aula: aulas, questao: questoes, desafio: desafios };
  const idDe = { aula: 'id_aula', questao: 'id_questao', desafio: 'id_desafio' };
  const achar = (tipo, id) => lista[tipo].find(x => x[idDe[tipo]] === id);
  const erroDe = { aula: 'Aula', questao: 'Questão', desafio: 'Desafio' };
  const profDoUsuarioPorProf = (idProf) => usuarios.find(u => u.papel === 'professor' && u.id_professor === idProf);
  const profDoUsuario = (id) => usuarios.find(u => u.id_usuario === id && u.papel === 'professor');
  const matDe = (id) => (materias.find(m => m.id_materia === id) || {}).titulo || '';
  const alternativasValidas = (q) => alternativas.filter(a => a.id_questao === q.id_questao && String(a.texto).trim());
  function enviarParaAprovacao(tipo, id, idProf) {
    const x = achar(tipo, id);
    if (!x || x.id_professor !== idProf || !['rascunho', 'rejeitado'].includes(stDe(x)))
      throw new ServicoError('Item não encontrado, não é seu, ou já foi enviado.');
    if (tipo === 'questao') {
      const alts = alternativasValidas(x);
      if (alts.length < 2) throw new ServicoError('Preencha pelo menos 2 alternativas.');
      if (!alts.some(a => a.correta)) throw new ServicoError('Marque a alternativa correta.');
    }
    setSt(x, 'pendente'); x.data = agora(); x.motivo = null;       // trigger cria a Aprovacao
  }
  function podeEditar(x, idProf) { return x && x.id_professor === idProf && ['rascunho', 'rejeitado'].includes(stDe(x)); }
  function gravarAlternativas(q, json, idxCorreta) {
    for (let i = alternativas.length - 1; i >= 0; i--) if (alternativas[i].id_questao === q.id_questao) alternativas.splice(i, 1);
    JSON.parse(json || '[]').forEach((t, i) => { if (String(t).trim()) alternativas.push({ id_alternativa: proxAlt++, id_questao: q.id_questao, texto: String(t).trim(), correta: i === idxCorreta }); });
  }

  return {
    usuarios, materias, aulas, questoes, alternativas, desafios, chamadas, estadoDe,
    // Simulam o admin do WinForms (só existem no banco falso)
    aprovar(tipo, id) { setSt(achar(tipo, id), 'aprovado'); const x = achar(tipo, id); x.motivo = null; if (tipo === 'questao' && x.chaves_recompensa == null) x.chaves_recompensa = CHAVES_QUESTAO; },
    rejeitar(tipo, id, motivo) { const x = achar(tipo, id); setSt(x, 'rejeitado'); x.motivo = motivo || null; },
    async execMulti(proc, p = {}) {
      chamadas.push([proc, p]);
      if (proc === 'sp_WebListarMeusConteudos') {
        const meus = (x) => x.id_professor === p.id_professor;
        return [
          aulas.filter(meus).map(a => ({ id_aula: a.id_aula, id_materia: a.id_materia, materia: matDe(a.id_materia), topico: a.topico, titulo: a.titulo,
            conteudo: a.conteudo, exemplo_codigo: a.exemplo_codigo, dica_titulo: a.dica_titulo, dica_corpo: a.dica_corpo, status: stDe(a), data: a.data || null, motivo: a.motivo || null })).reverse(),
          questoes.filter(meus).map(q => ({ id_questao: q.id_questao, id_materia: q.id_materia, materia: matDe(q.id_materia), enunciado: q.enunciado, dificuldade: q.dificuldade,
            codigo_exemplo: q.codigo_exemplo, status: stDe(q), data: q.data || agora(), motivo: q.motivo || null })).reverse(),
          alternativas.filter(a => (questoes.find(q => q.id_questao === a.id_questao) || {}).id_professor === p.id_professor),
          desafios.filter(meus).map(d => ({ id_desafio: d.id_desafio, id_materia: d.id_materia, materia: matDe(d.id_materia), titulo: d.titulo, enunciado: d.enunciado,
            dica: d.dica, dificuldade: d.dificuldade, codigo_base: d.codigo_base, status: stDe(d), data: d.data || agora(), motivo: d.motivo || null })).reverse(),
        ];
      }
      if (proc !== 'sp_ProgressoAluno') throw new Error('execMulti não simulado para ' + proc);
      const e = estadoDe(p.id_aluno);
      return [
        [...e.aulas].map(id_aula => ({ id_aula })),
        [...e.questoes].map(id_questao => ({ id_questao })),
        materias.map(m => ({ id_materia: m.id_materia, titulo: m.titulo, ordem: m.ordem, desbloqueada: desbloqueada(e, m) })),
        [...e.certs].map(([id_materia, data_emissao]) => ({ id_materia, data_emissao })),
      ];
    },
    async exec(proc, p = {}) {
      chamadas.push([proc, p]);
      switch (proc) {
        case 'sp_ObterUsuarioParaLogin': return usuarios.filter(u => u.email === String(p.email).trim().toLowerCase());
        case 'sp_RegistrarLogin': return [];
        case 'sp_CadastrarAluno': {
          const email = String(p.email).trim().toLowerCase();
          if (usuarios.some(u => u.email === email)) throw new ServicoError('Este e-mail já está cadastrado.');
          const nick = p.nickname.startsWith('@') ? p.nickname : '@' + p.nickname;
          if (usuarios.some(u => u.nickname === nick)) throw new ServicoError('Este nome de usuário já está em uso.');
          const id = ++proxId;
          usuarios.push({ id_usuario: id, nome: p.nome, nickname: nick, email, status: 'ativo', papel: 'aluno', id_aluno: id, id_professor: null, senha_hash: p.senha_hash });
          return [{ id_usuario: id, id_aluno: id }];
        }
        case 'sp_ObterConfiguracaoPlataforma':
          return [{ chaves_questao_facil: CHAVES_QUESTAO, chaves_questao_media: 10, chaves_questao_dificil: 15, chaves_certificado: CHAVES_CERTIFICADO }];
        case 'sp_WebListarMaterias': return materias;
        case 'sp_WebListarAulasMateria': return aulas.filter(a => a.id_materia === p.id_materia && aprovado(a)).map(a => ({ ...a, autor: a.id_professor ? (profDoUsuarioPorProf(a.id_professor) || {}).nome || null : null }));
        case 'sp_WebListarQuestoesMateria': return questoes.filter(q => q.id_materia === p.id_materia && aprovado(q));
        case 'sp_WebListarAlternativasMateria': {
          const ids = new Set(questoes.filter(q => q.id_materia === p.id_materia && aprovado(q)).map(q => q.id_questao));
          return alternativas.filter(a => ids.has(a.id_questao)).map(({ id_alternativa, id_questao, texto }) => ({ id_alternativa, id_questao, texto }));
        }
        case 'sp_ResponderQuestao': {
          const q = questoes.find(x => x.id_questao === p.id_questao);
          if (!q) throw new ServicoError('Questão inexistente ou ainda não aprovada.');
          const alt = alternativas.find(a => a.id_alternativa === p.id_alternativa && a.id_questao === p.id_questao);
          if (!alt) throw new ServicoError('Alternativa não pertence a essa questão.');
          const e = estadoDe(p.id_aluno);
          const antes = e.pontos;
          const ja = e.questoes.has(p.id_questao);
          let cert = false;
          if (alt.correta && !ja) { e.questoes.add(p.id_questao); e.pontos += q.chaves_recompensa; cert = tentarCertificado(p.id_aluno, q.id_materia); }
          e.streak = Math.max(e.streak, 1);
          return [{ acertou: alt.correta, ja_concluida: ja, chaves_ganhas: e.pontos - antes, certificado_emitido: cert, saldo_chaves: saldo(p.id_aluno) }];
        }
        case 'sp_ConcluirAula': {
          const au = aulas.find(a => a.id_aula === p.id_aula);
          if (!au) throw new ServicoError('Aula inexistente ou ainda não aprovada.');
          const e = estadoDe(p.id_aluno);
          const antes = e.pontos;
          let cert = false;
          if (!e.aulas.has(p.id_aula)) { e.aulas.add(p.id_aula); e.pontos += au.chaves_recompensa; cert = tentarCertificado(p.id_aluno, au.id_materia); }
          e.streak = Math.max(e.streak, 1);
          return [{ chaves_ganhas: e.pontos - antes, certificado_emitido: cert, saldo_chaves: saldo(p.id_aluno) }];
        }
        case 'sp_DesbloquearMateria': {
          const m = materias.find(x => x.id_materia === p.id_materia);
          if (!m) throw new ServicoError('Matéria não encontrada.');
          const e = estadoDe(p.id_aluno);
          if (desbloqueada(e, m)) return [{ desbloqueada: true, chaves_gastas: 0, saldo_chaves: saldo(p.id_aluno) }];
          const anterior = materias.filter(x => x.ordem < m.ordem).sort((a, b) => b.ordem - a.ordem)[0];
          if (anterior && !e.certs.has(anterior.id_materia)) throw new ServicoError('Conclua a matéria anterior antes de desbloquear esta.');
          if (saldo(p.id_aluno) < m.chaves_para_desbloquear) throw new ServicoError('Chaves insuficientes.');
          e.gastas += m.chaves_para_desbloquear; e.desbloq.add(m.id_materia);
          return [{ desbloqueada: true, chaves_gastas: m.chaves_para_desbloquear, saldo_chaves: saldo(p.id_aluno) }];
        }
        case 'sp_ObterPerfilAluno': {
          const u = usuarioDoAluno(p.id_aluno); const e = estadoDe(p.id_aluno);
          if (!u) return [];
          return [{ nome: u.nome, nickname: u.nickname, email: u.email, foto_url: u.foto_url || null, data_cadastro: new Date().toISOString(),
            pontos_acumulados: e.pontos, chaves_saldo: saldo(p.id_aluno), streak_atual: e.streak, fases_concluidas: fases(e),
            questoes_concluidas: e.questoes.size, certificados: e.certs.size, posicao_ranking: posicao(p.id_aluno) }];
        }
        case 'sp_RankingAlunos': {
          const lista = usuarios.filter(u => u.papel === 'aluno' && u.status === 'ativo' && u.id_aluno)
            .map(u => ({ u, e: estadoDe(u.id_aluno) })).sort((a, b) => b.e.pontos - a.e.pontos || a.u.nome.localeCompare(b.u.nome));
          return lista.map(({ u, e }) => ({ posicao: 1 + lista.filter(x => x.e.pontos > e.pontos).length, nome: u.nome, nickname: u.nickname,
            foto_url: u.foto_url || null, pontos_acumulados: e.pontos, streak_atual: e.streak, fases_concluidas: fases(e) })).slice(0, p.top ?? 10);
        }
        case 'sp_AtualizarPerfil': {
          const u = usuarios.find(x => x.id_usuario === p.id_usuario);
          if (!u) throw new ServicoError('Usuário não encontrado.');
          const email = p.email == null ? null : String(p.email).trim().toLowerCase();
          const nick = p.nickname == null ? null : (p.nickname.startsWith('@') ? p.nickname : '@' + p.nickname);
          if (email && usuarios.some(x => x.email === email && x.id_usuario !== u.id_usuario)) throw new ServicoError('Este e-mail já está em uso.');
          if (nick && usuarios.some(x => x.nickname === nick && x.id_usuario !== u.id_usuario)) throw new ServicoError('Este nome de usuário já está em uso.');
          if (p.nome != null) u.nome = p.nome; if (nick) u.nickname = nick; if (email) u.email = email; if (p.foto_url != null) u.foto_url = p.foto_url;
          return [];
        }
        case 'sp_AlterarSenha': {
          const u = usuarios.find(x => x.id_usuario === p.id_usuario);
          if (!u) throw new ServicoError('Usuário não encontrado.');
          u.senha_hash = p.senha_hash; return [];
        }
        case 'sp_AbrirTicket': {
          let dest = p.id_usuario_destinatario || null;
          if (!dest && p.id_materia) {
            const mat = materias.find(m => m.id_materia === p.id_materia);
            const profs = usuarios.filter(u => u.papel === 'professor' && u.status === 'ativo' && mat && u.especialidade === mat.titulo)
              .sort((a, b) => tickets.filter(t => t.id_usuario_destinatario === a.id_usuario && t.status !== 'fechado').length
                            - tickets.filter(t => t.id_usuario_destinatario === b.id_usuario && t.status !== 'fechado').length);
            if (profs[0]) dest = profs[0].id_usuario;
          }
          if (!dest) { const ad = usuarios.find(u => u.papel === 'admin' && u.status === 'ativo'); if (ad) dest = ad.id_usuario; }
          if (!dest) throw new ServicoError('Não foi possível definir o destinatário do ticket.');
          const t = { id_ticket: proxTicket++, id_usuario_solicitante: p.id_usuario_solicitante, id_usuario_destinatario: dest,
            id_materia: p.id_materia || null, assunto: p.assunto, descricao: p.descricao, status: 'aberto', data_abertura: agora() };
          tickets.push(t);
          mensagens.push({ id_mensagem: proxMsg++, id_ticket: t.id_ticket, id_usuario_remetente: p.id_usuario_solicitante, texto: p.descricao, data_hora: agora() });
          return [{ id_ticket: t.id_ticket, id_usuario_destinatario: dest }];
        }
        case 'sp_ResponderTicket': {
          const t = tickets.find(x => x.id_ticket === p.id_ticket);
          if (!t) throw new ServicoError('Ticket não encontrado.');
          if (t.status === 'fechado') throw new ServicoError('Ticket encerrado: não aceita novas mensagens.');
          if (!participa(t, p.id_usuario_remetente) && papelDe(p.id_usuario_remetente) !== 'admin') throw new ServicoError('Você não participa deste ticket.');
          mensagens.push({ id_mensagem: proxMsg++, id_ticket: t.id_ticket, id_usuario_remetente: p.id_usuario_remetente, texto: p.texto, data_hora: agora() });
          t.status = p.id_usuario_remetente === t.id_usuario_solicitante ? 'aberto' : 'respondido';   // trigger TR_TicketMensagem_AtualizaStatus
          return [];
        }
        case 'sp_MensagensTicket': {
          const t = tickets.find(x => x.id_ticket === p.id_ticket);
          if (p.id_usuario != null && !participa(t, p.id_usuario) && papelDe(p.id_usuario) !== 'admin') throw new ServicoError('Você não participa deste ticket.');
          return mensagens.filter(m => m.id_ticket === p.id_ticket).map(m => ({
            id_mensagem: m.id_mensagem, id_usuario_remetente: m.id_usuario_remetente, nome: nomeDe(m.id_usuario_remetente),
            texto: m.texto, data_hora: m.data_hora, remetente: m.id_usuario_remetente === t.id_usuario_solicitante ? 'aluno' : 'professor',
            papel: papelDe(m.id_usuario_remetente) }));
        }
        case 'sp_WebObterTicket': {
          const t = tickets.find(x => x.id_ticket === p.id_ticket);
          if (!participa(t, p.id_usuario)) throw new ServicoError('Você não participa deste ticket.');
          return [cabecalho(t)];
        }
        case 'sp_WebListarTickets':
          return tickets.filter(t => participa(t, p.id_usuario)).map(cabecalho)
            .sort((a, b) => b.ultima_atividade.localeCompare(a.ultima_atividade) || b.id_ticket - a.id_ticket);
        case 'sp_FecharTicket': {
          const t = tickets.find(x => x.id_ticket === p.id_ticket);
          if (!t || (t.id_usuario_destinatario !== p.id_usuario && papelDe(p.id_usuario) !== 'admin')) throw new ServicoError('Ticket não encontrado ou sem permissão.');
          t.status = 'fechado'; return [];
        }
        case 'sp_WebObterPerfilProfessor': {
          const u = profDoUsuario(p.id_usuario);
          if (!u) return [];
          const meus = [...aulas, ...questoes, ...desafios].filter(x => x.id_professor === u.id_professor && stDe(x) !== 'rascunho');
          const mat = materias.find(m => m.titulo === u.especialidade);
          return [{ id_professor: u.id_professor, nome: u.nome, nickname: u.nickname, email: u.email, foto_url: u.foto_url || null, especialidade: u.especialidade,
            id_materia_especialidade: mat ? mat.id_materia : null, enviados: meus.length, aprovados: meus.filter(x => stDe(x) === 'aprovado').length,
            pendentes: meus.filter(x => stDe(x) === 'pendente').length,
            alunos_ativos: usuarios.filter(x => x.papel === 'aluno' && x.status === 'ativo' && x.id_aluno).length }];
        }
        case 'sp_WebSalvarAula': {
          if (!materias.some(m => m.id_materia === p.id_materia)) throw new ServicoError('Matéria não encontrada.');
          let a;
          if (p.id_aula == null) {
            a = { id_aula: proxAula++, id_materia: p.id_materia, id_professor: p.id_professor, status_aprovacao: 'rascunho', ordem: null };
            aulas.push(a);
          } else {
            a = achar('aula', p.id_aula);
            if (!podeEditar(a, p.id_professor)) throw new ServicoError('Só é possível editar rascunhos ou conteúdos rejeitados do próprio professor.');
          }
          Object.assign(a, { id_materia: p.id_materia, titulo: p.titulo, topico: p.topico, conteudo: p.conteudo, exemplo_codigo: p.exemplo_codigo,
            dica_titulo: p.dica_titulo, dica_corpo: p.dica_corpo, is_extra: 1, chaves_recompensa: 0 });
          if (p.enviar) enviarParaAprovacao('aula', a.id_aula, p.id_professor);
          return [{ id_aula: a.id_aula }];
        }
        case 'sp_SalvarQuestao': {
          if (!materias.some(m => m.id_materia === p.id_materia)) throw new ServicoError('Matéria não encontrada.');
          let q;
          if (p.id_questao == null) {
            q = { id_questao: proxQuestao++, id_materia: p.id_materia, id_professor: p.id_professor, status_aprovacao: 'rascunho', chaves_recompensa: null };
            questoes.push(q);
          } else {
            q = achar('questao', p.id_questao);
            if (!podeEditar(q, p.id_professor)) throw new ServicoError('Só é possível editar rascunhos ou conteúdos rejeitados do próprio professor.');
          }
          Object.assign(q, { id_materia: p.id_materia, enunciado: p.enunciado, dificuldade: p.dificuldade, codigo_exemplo: p.codigo_exemplo, data: agora() });
          gravarAlternativas(q, p.alternativas_json, p.indice_correta);
          if (p.enviar) enviarParaAprovacao('questao', q.id_questao, p.id_professor);
          return [{ id_questao: q.id_questao }];
        }
        case 'sp_SalvarDesafio': {
          let d;
          if (p.id_desafio == null) {
            d = { id_desafio: proxDesafio++, id_professor: p.id_professor, status: 'rascunho' };
            desafios.push(d);
          } else {
            d = achar('desafio', p.id_desafio);
            if (!podeEditar(d, p.id_professor)) throw new ServicoError('Só é possível editar rascunhos ou conteúdos rejeitados do próprio professor.');
          }
          Object.assign(d, { id_materia: p.id_materia, titulo: p.titulo, enunciado: p.enunciado, dificuldade: p.dificuldade, codigo_base: p.codigo_base, dica: p.dica, data: agora() });
          if (p.enviar) enviarParaAprovacao('desafio', d.id_desafio, p.id_professor);
          return [{ id_desafio: d.id_desafio }];
        }
        case 'sp_EnviarParaAprovacao': enviarParaAprovacao(p.tipo, p.id, p.id_professor); return [];
        case 'sp_ExcluirConteudoProfessor': {
          const x = achar(p.tipo, p.id);
          if (!x || x.id_professor !== p.id_professor || stDe(x) === 'aprovado') throw new ServicoError(erroDe[p.tipo] + ' não encontrada ou já aprovada.');
          lista[p.tipo].splice(lista[p.tipo].indexOf(x), 1);
          if (p.tipo === 'questao') for (let i = alternativas.length - 1; i >= 0; i--) if (alternativas[i].id_questao === p.id) alternativas.splice(i, 1);
          return [];
        }
        case 'sp_WebListarDesafiosAluno':
          return desafios.filter(aprovado).map(d => ({ id_desafio: d.id_desafio, id_materia: d.id_materia, materia: matDe(d.id_materia), titulo: d.titulo, enunciado: d.enunciado,
            dica: d.dica, dificuldade: d.dificuldade, codigo_base: d.codigo_base, autor: (profDoUsuarioPorProf(d.id_professor) || {}).nome || null,
            concluido: alunoDesafio.has(p.id_aluno + ':' + d.id_desafio) }));
        case 'sp_WebConcluirDesafio': {
          const d = achar('desafio', p.id_desafio);
          if (!d || !aprovado(d)) throw new ServicoError('Desafio indisponível.');
          const e = estadoDe(p.id_aluno); const chave = p.id_aluno + ':' + p.id_desafio;
          const ja = alunoDesafio.has(chave);
          if (!ja) { alunoDesafio.add(chave); e.pontos += CHAVES_DESAFIO[d.dificuldade] || 5; }
          return [{ ja_concluido: ja, chaves_ganhas: ja ? 0 : (CHAVES_DESAFIO[d.dificuldade] || 5), saldo_chaves: saldo(p.id_aluno) }];
        }
        case 'sp_DesempenhoAlunos':
          return usuarios.filter(u => u.papel === 'aluno' && u.status === 'ativo' && u.id_aluno).map(u => {
            const e = estadoDe(u.id_aluno); const doMat = (id) => p.id_materia == null || id === p.id_materia;
            const f = [...e.aulas].filter(id => doMat((achar('aula', id) || {}).id_materia)).length + [...e.questoes].filter(id => doMat((achar('questao', id) || {}).id_materia)).length;
            return { nome: u.nome, email: u.email, chaves: e.pontos, fases: f, acerto: e.questoes.size ? 100 : 0, certs: [...e.certs.keys()].filter(doMat).length, ultima: e.streak ? new Date().toISOString().slice(0, 10) : null };
          }).sort((a, b) => a.nome.localeCompare(b.nome));
        default: throw new Error('Procedure não simulada: ' + proc);
      }
    },
  };
}

module.exports = { criarBancoFalso };
