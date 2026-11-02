# Passo a passo das evoluções

## Fase 1 — Login, cadastro, config de chaves e matérias  ✅ feita

### O que foi criado
| Arquivo | Para quê |
|---|---|
| `api/src/config.js` | Lê o `.env`; recusa iniciar sem `JWT_KEY` forte |
| `api/src/db.js` | Único ponto de acesso ao SQL Server (só procedures). Erro de regra do banco (`RAISERROR`, nº 50000) vira `400` com a mensagem |
| `api/src/auth.js` | Gera/valida o JWT (carrega `id_aluno` / `id_professor`) e protege rotas por papel |
| `api/src/routes/auth.js` | `POST /auth/login`, `POST /auth/cadastro`, `GET /auth/me` |
| `api/src/routes/plataforma.js` | `GET /config/chaves`, `GET /materias` |
| `api/src/app.js` | Monta o Express (CORS, JSON, erros) |
| `web/js/api.js` | Cliente da API no navegador; token só em memória |
| `api/test/*` | testes automáticos + servidor com banco falso |

### O que mudou no `web/index.html`
| Antes | Agora |
|---|---|
| `USUARIOS_DB` com 2 usuários e senha `123` | Removido. Login vem do banco via `POST /auth/login` |
| `Auth.validarCredenciais` (comparava senha no navegador) | Removido |
| `realizarLogin()` síncrono | `async`, chama `Api.login`, mostra o erro que a API devolve (senha errada, conta inativa, admin) |
| `criarContaV15()` gravava em `LQ_USUARIOS` (memória) | `async`, chama `Api.cadastro` e já entra logado |
| Senha mínima de 4 | 8 (igual à API) — também no placeholder do campo |
| Data de nascimento ignorada | Convertida de DD/MM/AAAA e enviada (opcional) |
| `selecionarPerfil` preenchia e-mails de demonstração | Só troca a imagem/botão |
| `fazerLogout()` | Também apaga o token (`Api.sair()`) |
| Recompensa de questão fixa em 5 chaves | Lida de `CONFIG_CHAVES` (tabela `ChavesConfig`). Hoje usa `questaoFacil` porque as questões do JS ainda não têm dificuldade |

### Regras que a API já garante
- Cadastro público cria **só aluno**.
- Admin **não** entra pela web (mensagem manda usar o HytechAdmin).
- Login responde igual para e-mail inexistente e senha errada; conta inativa só é revelada com a senha certa.
- Limite de 10 tentativas/min por IP em login e cadastro.
- Senhas: `bcryptjs` — mesmo formato do BCrypt do WinForms/C#; contas antigas continuam valendo.

### Ainda NÃO ligado (continua em memória no front)
Progresso, chaves ganhas, ranking, streak, tickets, conteúdo e questões das matérias, tudo do professor, desempenho dos alunos, perfil/foto, certificados. Também: **especialidade do professor** não vem no login (a procedure `sp_ObterUsuarioParaLogin` não devolve) — entra na fase do professor.

## Fase 2 — Conteúdo das matérias vindo do banco  ✅ feita

### Banco (`api/sql/02_fase2_conteudo.sql`, rodar depois do HytechDB.sql)
- 4 procedures novas só de leitura: `sp_WebListarMaterias`, `sp_WebListarAulasMateria`, `sp_WebListarQuestoesMateria`, `sp_WebListarAlternativasMateria` (esta **não devolve** a coluna `correta`).
- Importa o que estava fixo no JS: **4 matérias, 16 aulas (3 + 1 extra por matéria), 12 questões, 60 alternativas**, tudo `aprovado`.
- Custo de desbloqueio das matérias: 0 / 19 / 38 / 57 chaves (só aplica se ainda estiver 0, para não sobrescrever o que o admin mudou).
- Cria a conta de sistema **"Equipe HyTech"** (`conteudo@hytech.local`, status `inativo`, sem login), porque `Questao.id_professor` é obrigatório. As aulas ficam com autor `NULL`.
- Todas as questões entram como **Fácil** (valem o que o `ChavesConfig` define). Ajuste depois pelo admin.
- Idempotente: rodar de novo não duplica nada. ⚠️ Foi escrito e revisado, mas **ainda não foi executado num SQL Server real**: rode primeiro numa cópia do banco.

### API
| Rota | O que faz |
|---|---|
| `GET /materias` (alterada) | agora traz também `ordem` e `chavesParaDesbloquear` |
| `GET /materias/:id/aulas` | tópicos aprovados, extras por último |
| `GET /materias/:id/questoes` | questões + alternativas **sem gabarito** |
| `POST /aluno/questoes/:id/responder` | corpo `{ idAlternativa }`. Chama `sp_ResponderQuestao`: o banco diz se acertou, quantas chaves ganhou e se emitiu certificado. Só papel `aluno`; o `id_aluno` vem do token |

### `web/index.html`
| Antes | Agora |
|---|---|
| `MATERIAS_DATA` com ~300 linhas de texto **e o gabarito** dentro do JS | Só um esqueleto; `carregarConteudoDaApi()` preenche após o login do aluno |
| `confirmarRespostaTrilha` decidia no navegador | `async`: envia a alternativa e usa a resposta do servidor (`acertou`, `chavesGanhas`) |
| "Vale 3/5 Chaves" e "+5 chaves" fixos | Valores do banco (por aula e por questão) |
| Textos de pergunta, código, exemplo e dica entravam como HTML | Escapados com `_esc()` (segurança). O corpo da aula continua HTML, de propósito |
| Se a API falha | Mensagem "Conteúdo indisponível" em vez de tela quebrada |

Efeito colateral bom: **ver o código-fonte da página não revela mais as respostas certas.**

### Ainda local (de propósito, fica para a Fase 3)
Marcar aula como lida, saldo de chaves, desbloqueio de matéria, streak, ranking, perfil. Hoje, ao dar F5, o progresso some (só o que já foi gravado no banco por `responder` fica). As chaves de aula continuam sendo somadas só no navegador.

## Fase 3 — Progresso do aluno no banco  ✅ feita

Agora, ao dar F5 e entrar de novo (ou entrar de outro computador), o aluno vê as mesmas chaves, tópicos lidos, questões feitas, matérias liberadas, streak, posição e certificados. Não precisou de SQL novo: usa só procedures que já estavam no `HytechDB.sql`.

### API (novas rotas)
| Rota | Procedure | O que faz |
|---|---|---|
| `GET /aluno/estado` | `sp_ObterPerfilAluno` + `sp_ProgressoAluno` | Devolve tudo de uma vez: pontos, saldo de chaves, streak, posição, aulas/questões concluídas, matérias liberadas e certificados |
| `POST /aluno/aulas/:id/concluir` | `sp_ConcluirAula` | "Marcar como lido". O banco credita as chaves (só na 1ª vez) e pode emitir certificado |
| `POST /aluno/materias/:id/desbloquear` | `sp_DesbloquearMateria` | Gasta chaves de verdade. Erros do banco ("Chaves insuficientes.", "Conclua a matéria anterior…") viram 400 |
| `GET /ranking?top=10` | `sp_RankingAlunos` | Ranking real; marca `souEu` |
| `PUT /conta/perfil` | `sp_AtualizarPerfil` | Nome, nickname, e-mail e foto (aluno e professor). Devolve um **token novo** (o token carrega nome/e-mail) |
| `POST /conta/senha` | `sp_ObterUsuarioParaLogin` + `sp_AlterarSenha` | Confere a senha atual e grava o hash da nova (mín. 8) |

Também: `Db.execMulti` (procedures com várias tabelas de resultado) e `nickname` dentro do token.

### Site (`web/index.html`)
| Antes | Agora |
|---|---|
| Progresso só em memória (sumia no F5) | `carregarEstadoDoAluno()` reconstrói tudo a partir do banco depois do login |
| Uma conta herdava o progresso da anterior | `resetProg()` no logout e no início de todo login/cadastro |
| "Marcar como lido" somava chaves no navegador | `marcarLidoHub` chama a API; as chaves vêm do servidor |
| Desbloqueio descontava chaves no navegador | `desbloquearProximaMateria` chama a API; mostra a mensagem do banco se falhar |
| Ranking com 5 alunos falsos; streak fixo em 1 | Ranking, posição e streak reais |
| Nível e conquistas usavam o saldo (que diminui ao gastar) | Usam os **pontos acumulados** (só sobem); os contadores de chaves mostram o **saldo** |
| Certificado dava "+15 chaves" fixo | Valor do banco (`ChavesConfig`, padrão 50); comprovante usa a data do servidor |
| Editar perfil/senha/foto só na tela | Salva no banco; senha pede a atual; foto é reduzida no navegador antes de enviar |

### Ainda local (de propósito)
Tickets (Fase 4); conteúdo, questões, desafios e desempenho do professor (Fase 5); desafios do aluno (Fase 5); conquistas e níveis continuam calculados no site.

### Testado / não testado
- ✅ 34 testes automáticos com banco falso; fluxo no navegador (ler tópico → F5 → continua lido; duas contas não se misturam; desbloquear sem chaves mostra o erro do banco; cadastro novo vem zerado; editar perfil).
- ⚠️ Nada disso rodou ainda num **SQL Server real**. As procedures usadas já existiam, mas teste primeiro numa cópia do banco.

## Fase 4 — Tickets de suporte no banco  ✅ feita

Aluno abre ticket por matéria, o professor da matéria responde e encerra, e tudo fica salvo no banco. F5 ou outro computador mostram as mesmas conversas.

### Banco (`api/sql/03_fase4_tickets.sql`, rodar depois do 02)
- 2 procedures de **leitura** novas: `sp_WebListarTickets` e `sp_WebObterTicket` (devolvem os ids que a API precisa; as antigas só devolvem nomes). Nenhuma procedure do WinForms foi alterada.
- Escrita usa as que já existiam: `sp_AbrirTicket`, `sp_ResponderTicket`, `sp_FecharTicket`, `sp_MensagensTicket`. O status (aberto/respondido) muda por trigger.
- ⚠️ Esse SQL nunca rodou num SQL Server real: teste primeiro numa cópia do banco.

### API (`api/src/routes/tickets.js`)
| Rota | Quem | O que faz |
|---|---|---|
| `GET /tickets` | aluno, professor | Lista: aluno vê os que abriu; professor, os que recebeu |
| `GET /tickets/:id` | participante | Cabeçalho + conversa. Quem não participa recebe 400 do banco |
| `POST /tickets` | aluno | `{ idMateria, assunto (até 150), mensagem (até 4000) }`. O solicitante vem do token; o banco escolhe o destinatário |
| `POST /tickets/:id/mensagens` | participante | `{ texto }` |
| `POST /tickets/:id/fechar` | professor destinatário | Encerra (o banco confere) |

Limite de escrita: 60 mensagens/tickets por hora por usuário (`TICKETS_LIMITE_POR_HORA` no `.env`).

### Site
| Antes | Agora |
|---|---|
| Array `tickets` em memória | Cache da API; `abrirTicketsAluno/Prof` carregam do banco |
| Matéria do ticket era um nome fixo no `<select>` | Opções vêm do banco (value = id da matéria) |
| Status calculado no navegador | Vem do banco (trigger) |
| Só hora na mensagem | Hora de hoje ou data + hora, vindas do banco |
| Troca de conta mantinha os tickets | `resetTickets()` (chamado pelo `resetProg()`) |
| — | Atualização automática a cada 30 s com a tela aberta (não atualiza o ticket se você estiver digitando) |
| — | Aluno vê "encaminhado à administração" quando o destinatário é admin |

### Como o ticket escolhe o professor
O banco procura o professor ativo cuja **especialidade** é igual ao nome da matéria (o que tem menos tickets abertos). Se não houver, usa o professor da matéria; se não houver, um **admin**. Ticket que cai no admin só é respondido pelo HytechAdmin (Windows). Por isso, **preencha a especialidade dos professores no HytechAdmin** (ex.: "Programação").

### Testado / não testado
- ✅ 50 testes automáticos (16 novos); fluxo no navegador: abrir ticket, F5, professor responde e encerra, aluno vê a resposta, outra conta não vê tickets alheios, HTML digitado aparece como texto.
- ⚠️ Nada rodou ainda num SQL Server real.

## Fase 5 — Área do professor no banco  ✅ feita

O professor cria aulas, questões e desafios, salva rascunho, envia para aprovação, edita e exclui; vê o painel e o desempenho dos alunos. Tudo fica no banco (F5 ou outro computador mostram o mesmo).

### ⚠️ O que mudou no comportamento (leia)
1. **"Enviar" não publica mais na hora.** Vai para a fila de aprovação; o **administrador aprova no HytechAdmin (Windows)**. Só depois o conteúdo aparece para os alunos. O botão agora se chama "Enviar para aprovação".
2. **Aula de professor é conteúdo EXTRA e paga 0 chaves.** Sem isso ela viraria obrigatória para o certificado de todos os alunos. O admin pode ajustar no WinForms.
3. **O professor só cria conteúdo da própria especialidade** (a matéria fica travada no formulário). A especialidade é definida só pelo admin; o botão de editar especialidade saiu do perfil.
4. **Questão aprovada passa a valer para o certificado** dos alunos que ainda não o têm (regra do banco).
5. **Desafio do aluno:** o aluno lê o enunciado e clica em "Marcar como concluído" (sem correção automática) e ganha as chaves uma única vez (5/10/15). Se preferir não creditar por autodeclaração, é só trocar o botão por um aviso.
6. **"Desempenho dos alunos"** mostra todos os alunos ativos da plataforma, filtrados pela matéria do professor (fases, acerto e certificados). Inclui o e-mail do aluno.

### Banco (`api/sql/04_fase5_professor.sql`, rodar depois do 03)
`sp_WebObterPerfilProfessor`, `sp_WebListarMeusConteudos` (4 tabelas), `sp_WebSalvarAula` (grava dica e marca extra/0 chaves), `sp_WebListarAulasMateria` (agora com o autor), `sp_WebListarDesafiosAluno`, `sp_WebConcluirDesafio`. Escrita de questão/desafio/envio/exclusão/desempenho usa as procedures que já existiam. Nenhuma procedure do WinForms foi alterada. ⚠️ Nunca rodou em SQL Server real.

### API (`api/src/routes/professor.js` + 2 rotas em `aluno.js`)
| Rota | Quem | O que faz |
|---|---|---|
| `GET /professor/perfil` | professor | Nome, especialidade, matéria da especialidade e contadores do painel |
| `GET /professor/meus` | professor | Aulas, questões (com alternativas e gabarito, só do autor) e desafios, com status e motivo da rejeição |
| `POST/PUT /professor/aulas` · `/questoes` · `/desafios` | professor | Salva rascunho (`enviar:false`) ou envia (`enviar:true`). PUT só em rascunho/rejeitado |
| `POST /professor/{aulas,questoes,desafios}/:id/enviar` | dono | Rascunho/rejeitado → aguardando aprovação |
| `DELETE /professor/{aulas,questoes,desafios}/:id` | dono | Só o que não foi aprovado |
| `GET /professor/desempenho` | professor | `?materia=todas` ou `?materia=<id>` (padrão: a da especialidade) |
| `GET /aluno/desafios` · `POST /aluno/desafios/:id/concluir` | aluno | Desafios aprovados / concluir (credita 1 vez) |

Segurança: id do professor vem do token; texto da aula é escapado no servidor (`src/texto.js`, nunca HTML bruto); limites de tamanho; limite de 120 escritas/hora por professor (`LIMITE_CONTEUDO_POR_HORA`).

### Site
Saíram todos os dados de mentira do professor (`LQ_STORE`, listas, turma e desafios de exemplo) e a injeção local de questões/conteúdos. Entrou: carregamento por API, edição real (preenche o formulário), selo "Rejeitado" com motivo, modal do desafio para o aluno, `resetProf()` na troca de conta, e título de aula escapado no hub (mostra "por <autor>").

### Testado / não testado
- ✅ 60 testes automáticos (10 novos) e fluxo no navegador: criar/enviar/editar/excluir, aprovar (simulado), aluno vê a aula/questão/desafio, conclui o desafio e ganha chaves, rejeição com motivo, F5, troca de conta sem vazar dados, HTML digitado aparece como texto, gabarito não vai ao aluno.
- ⚠️ Nada rodou ainda num **SQL Server real**. A aprovação foi simulada no banco falso (`/__dev/aprovar`, só existe lá).

## Fase 6 — Limpeza, segurança e publicação  ✅ feita

- **Segurança da API:** cabeçalhos `helmet` (CSP, nosniff, sem `x-powered-by`), limite geral de 300 requisições/min por IP, corpo de 100 KB (só a foto de perfil aceita 1,5 MB), CORS só para as origens do `.env`, erros 500 sem detalhes, log de requisições sem corpo/token/e-mail.
- **`.env` em produção é validado:** com `NODE_ENV=production` a API **recusa subir** se o `JWT_KEY` ou a senha do banco forem os valores de exemplo. `npm run chave` gera uma chave forte.
- **Um endereço só:** com `SERVIR_SITE=true` a própria API entrega o site (sem CORS e sem precisar de Live Server). `GET /saude` para monitoramento. O site usa a mesma origem da API automaticamente fora do desenvolvimento.
- **Limpeza do site:** removido `LQ_USUARIOS` (lista de usuários local), e-mails de exemplo e `alert()`.
- **Encerramento limpo** (SIGTERM) e `unhandledRejection` registrado.
- Guia completo: `docs/PUBLICACAO.md`.
- ✅ 67 testes automáticos; site conferido no navegador servido pela própria API com a CSP ligada.
- ⚠️ Ainda não testado: SQL Server real e HTTPS real (feito no proxy/IIS do servidor).

## Extra — Certificado em PDF e tela de parabéns  ✅ feito

- **PDF profissional** (A4 paisagem, texto selecionável) gerado no próprio navegador por `web/js/certificado.js` + jsPDF (`web/js/vendor/`, carregado só quando o aluno pede). Nada é enviado a servidor. Traz as informações do TXT (aluno, módulo, data de conclusão, status "Concluído com Êxito", data/hora de emissão, plataforma HYTECH) com a estrela embaixo do título "CERTIFICADO DE CONCLUSÃO".
- **Cores por matéria**, iguais às medalhas: Programação (ciano), Design (magenta), Banco de Dados (azul royal), Redes (verde). A estrela também tem uma versão colorida para cada matéria (`web/assets/certificado/estrela-*.webp`; a original `estrela.webp` ficou como base).
- **Tela de parabéns** (quando o aluno fecha a matéria): mascote comemorando (`assets/mascote/pose-corpo-1.png`) com estrelinhas roxas piscando, estrela e medalha da matéria, chips de status/data/chaves, botões **Baixar certificado (PDF)** e **Imprimir**, e o botão de desbloquear a próxima matéria. O TXT virou um link pequeno. A medalha apagada do fundo saiu.
- O texto "— Python" que aparecia em todas as matérias foi removido do módulo.
- ⚠️ O PDF foi conferido em imagem (as 4 matérias) e o download testado no navegador; ainda não foi aberto em impressora real. Nomes com caracteres fora do alfabeto latino (ex.: japonês) aparecem sem esses caracteres no PDF.

## Decisões em aberto
- Foto de perfil em base64 dentro do banco funciona, mas pesa; depois podemos guardar arquivo e salvar só a URL.
- Níveis e conquistas (19/38/57/90 chaves) ficam no front; se quiser, viram tabela para o admin ajustar.
