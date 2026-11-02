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
| `api/test/*` | 10 testes automáticos + servidor com banco falso |

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

## Próximas fases (uma por vez)
2. **Conteúdo:** importar as 4 matérias (16 tópicos + 15 questões) do JS para `Aula`/`Questao`/`Alternativa`; rotas de leitura; gabarito deixa de ir ao navegador.
3. **Progresso do aluno:** concluir aula, responder questão (`sp_ResponderQuestao`), desbloquear matéria, chaves/streak/perfil, ranking real.
4. **Tickets** (aluno e professor).
5. **Professor:** salvar/enviar/excluir aula, questão e desafio; desempenho dos alunos; perfil.
6. **Certificados** e limpeza do que sobrou de dados fixos.

## Decisões em aberto
- Foto de perfil em base64 dentro do banco funciona, mas pesa; depois podemos guardar arquivo e salvar só a URL.
- Níveis e conquistas (19/38/57/90 chaves) ficam no front; se quiser, viram tabela para o admin ajustar.
