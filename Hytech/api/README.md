# HytechApi — API única da plataforma HyTech (ASP.NET Core 8)

Camada entre o web (`index.html`), o app HytechAdmin (WinForms) e o SQL Server. Fala com o banco **somente por
stored procedures**. Navegador/desktop → API (JWT) → banco.

## Rodar
Veja o passo a passo no `README.md` da pasta acima. Resumo: banco → `seed_admin.sql` → `Jwt:Key` nos user-secrets → `dotnet run`.

Utilitários (rodam e saem):
- `dotnet run -- --hash "SuaSenhaForte"` → hash BCrypt (para o `seed_admin.sql`)
- `dotnet run -- --chave` → chave aleatória para o `Jwt:Key`

## Configuração (`appsettings.json`, user-secrets ou variáveis de ambiente — use `__` no lugar de `:`)

| Chave | Para quê | Padrão |
|---|---|---|
| `ConnectionStrings:HytechDb` | conexão com o SQL Server | `Server=localhost;Database=HytechDB;Trusted_Connection=True;...` |
| `Jwt:Key` | chave do JWT, 32+ caracteres (**nunca no Git**) | — (obrigatória) |
| `Jwt:ExpiraMinutos` / `Issuer` / `Audience` | validade e identificação do token | 120 / HytechApi / HytechWeb |
| `Cors:Origins` | endereços de onde o site pode chamar a API | `localhost:5500`, `127.0.0.1:5500`, `localhost:3000` |
| `Email:Host`, `Porta`, `Ssl`, `Usuario`, `Senha`, `De` | envio de e-mail (SMTP). Sem `Host` = **modo console** (código aparece no terminal) | console |
| `Email:Modo` | `console` força o modo console mesmo com SMTP (só demonstração) | — |
| `Plataforma:ServirSite` / `SiteDir` | a API entrega a pasta `web/` (um endereço só, sem CORS) | `false` / `../web` |
| `Plataforma:CodigoMinutos` | validade do código por e-mail | 10 |
| `Plataforma:ConfiarNoProxy` | `true` se estiver atrás de Nginx/IIS/Cloudflare (lê o IP real) | `false` |
| `Plataforma:LimiteGeralPorMinuto` | requisições por IP por minuto (toda a API) | 300 |
| `Plataforma:LimiteAuthPorMinuto` | tentativas de login/cadastro/conta por IP por minuto | 10 |
| `Plataforma:LimiteConteudoPorHora` | criações/edições do professor por hora | 120 |
| `Plataforma:LimiteTicketsPorHora` | tickets/mensagens por usuário por hora | 60 |
| `Plataforma:LogRequisicoes` | uma linha por requisição (método, caminho, status, tempo) | só em produção |

Em **produção** (`ASPNETCORE_ENVIRONMENT=Production`, o padrão fora do `dotnet run`) a API **não sobe** se a chave do JWT ou a senha do banco forem os valores de exemplo, ou se o e-mail não estiver configurado.

## Rotas

Erros de regra de negócio do banco voltam como `400 { "erro": "..." }`. Sem token: `401`. Papel errado: `403`. Limite estourado: `429`.

### Autenticação — `/auth` (públicas, limite de tentativas por IP)
| Rota | O que faz |
|---|---|
| `POST /auth/login` | login do site (aluno/professor). Admin é recusado (usa o desktop) |
| `POST /auth/login-admin` | login do app desktop: só admin (aluno recebe a mesma resposta de senha errada) |
| `POST /auth/cadastro` → `/cadastro/confirmar`, `/cadastro/reenviar` | cadastro **só de aluno**, em 2 passos: valida e manda código por e-mail; a conta só é criada ao confirmar |
| `POST /auth/esqueci` → `/esqueci/redefinir` | recuperar senha por código (resposta igual para e-mail existente ou não) |
| `GET /auth/me` | quem sou eu (token) |

### Plataforma (qualquer usuário logado)
`GET /config/chaves` · `GET /materias` · `GET /materias/{id}/aulas` · `GET /materias/{id}/questoes` (nunca traz o gabarito) · `GET /ranking?top=10`

### Aluno — `/aluno` (papel aluno; o id vem do token)
`GET /aluno/estado` · `POST /aluno/questoes/{id}/responder` · `POST /aluno/aulas/{id}/concluir` · `POST /aluno/materias/{id}/desbloquear` · `GET /aluno/desafios` · `POST /aluno/desafios/{id}/concluir`

### Professor — `/professor` (papel professor)
`GET /professor/perfil` · `GET /professor/meus` · `POST /professor/aulas|questoes|desafios` · `PUT /professor/aulas|questoes|desafios/{id}` · `POST /professor/{tipo}/{id}/enviar` · `DELETE /professor/{tipo}/{id}` · `GET /professor/desempenho?materia=todas|{id}`

### Conta — `/conta` (aluno e professor)
`PUT /conta/perfil` · `POST /conta/senha` (+`/confirmar`) · `POST /conta/email` (+`/confirmar`) · `POST /conta/reenviar` · `POST /conta/desativar`

### Tickets — `/tickets` (aluno e professor)
`GET /tickets` · `GET /tickets/{id}` · `POST /tickets` (aluno) · `POST /tickets/{id}/mensagens` · `POST /tickets/{id}/fechar` (professor)

### Admin — `/admin` (papel admin; usadas pelo HytechAdmin-WinForms)
O id do administrador vem **do token** (`sub`), nunca do corpo.

| Rota | Procedure |
|---|---|
| `GET/POST /admin/usuarios`, `PUT/DELETE /admin/usuarios/{id}`, `PATCH /admin/usuarios/{id}/status` | `sp_ListarUsuarios`, `sp_AdminSalvarUsuario`, `sp_ExcluirUsuario`, `sp_AlterarStatusUsuario` |
| `GET /admin/materias` | `sp_ListarMaterias` |
| `GET /admin/conteudos`, `GET/DELETE /admin/conteudos/{tipo}/{id}`, `POST .../decisao` | `sp_AdminListarConteudos`, `sp_AdminObterConteudo`, `sp_AdminExcluirConteudo`, `sp_AdminDecidirConteudo` |
| `GET /admin/tickets?escopo=meus\|todos`, `GET/POST /admin/tickets/{id}/mensagens`, `POST /admin/tickets/{id}/encerrar` | `sp_AdminListarTickets`, `sp_MensagensTicket`, `sp_ResponderTicket`, `sp_FecharTicket` |
| `GET/PUT /admin/config/chaves` | `sp_ObterConfiguracaoPlataforma`, `sp_AtualizarConfiguracaoPlataforma` |
| `GET /admin/metricas`, `GET /admin/logs?top=100` | `sp_MetricasPlataforma`, `sp_ListarLogs` |

`tipo` (admin) é `aula`, `questao` ou `desafio`. O hash BCrypt das senhas é gerado **na API**.

### Outras
`GET /saude` → `{"status":"ok"}` (monitoramento).

## Decisões de segurança
- Cadastro público cria apenas aluno; professor e admin só pelo painel do admin.
- Login responde igual para e-mail inexistente e senha errada (e sempre confere um hash).
- Códigos de e-mail: guardados só como HMAC, 5 tentativas, uso único, expiram, limite de pedidos.
- Ids (aluno, professor, admin, solicitante do ticket) vêm **sempre do token**.
- Corpo JSON tolerante: tipo errado vira 400, nunca 500; limite de 100 KB (1,5 MB só na foto de perfil).
- CORS restrito, CSP e demais cabeçalhos de segurança, erro 500 sem detalhes (o detalhe vai só para o log).
- A API só chama procedures; use um usuário SQL com `EXECUTE` apenas (veja `docs/PUBLICACAO.md`).
