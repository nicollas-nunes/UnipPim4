# Migração: API Node.js → HytechApi (C# / ASP.NET Core 8)

Objetivo: **uma API só** para o site (aluno/professor) e para o HytechAdmin. O Node foi removido; a `HytechApi` em C# passou a fazer tudo o que o Node fazia, mais as rotas de admin que ela já tinha.

## O que foi removido
- Pasta `api/` em Node (`server.js`, `src/`, `package.json`, `node_modules`, `.env.example`, `scripts/`, `test/*.js`).
- Pasta `reference-csharp/` (a API antiga em C#, que serviu de base e agora está dentro de `api/`).

## Equivalência dos arquivos
| Node | C# (pasta `api/`) |
|---|---|
| `server.js` + `src/app.js` | `Program.cs` + `HytechApp.cs` |
| `src/config.js` (`.env`) | `Opcoes.cs` + `appsettings.json` |
| `src/db.js` | `Data/Db.cs` (`IDb`, `SqlDb`) e `Data/Linha.cs` |
| `src/auth.js` | `Security/TokenService.cs` + políticas em `HytechApp.cs` + `Infra/Middlewares.cs` |
| `src/email.js` | `Services/Email.cs` |
| `src/verificacao.js` | `Services/Verificacao.cs` |
| `src/texto.js` | `Services/Texto.cs` e `Infra/Corpo.cs` |
| `routes/auth.js` | `Endpoints/AuthEndpoints.cs` |
| `routes/conta.js` | `Endpoints/ContaEndpoints.cs` |
| `routes/plataforma.js` | `Endpoints/PlataformaEndpoints.cs` |
| `routes/aluno.js` | `Endpoints/AlunoEndpoints.cs` |
| `routes/professor.js` | `Endpoints/ProfessorEndpoints.cs` |
| `routes/tickets.js` | `Endpoints/TicketsEndpoints.cs` |
| (já existia em C#) | `Endpoints/AdminEndpoints.cs` — rotas do HytechAdmin |
| `test/*.test.js` + `banco-falso.js` | `api.testes/` (70 testes + `BancoFalso.cs`) |

**O banco não mudou**: todas as procedures que as rotas chamam já existiam nos scripts `sql/`. Nenhum script SQL novo foi criado. **O site também não mudou** (só comentários); o contrato das rotas e dos JSONs é o mesmo, então o `index.html` funciona sem alterações.

## O que foi acrescentado (e por quê)
1. **Interface `IDb` (banco falso)** — o acesso ao banco virou uma interface. Em produção é o `SqlDb` (procedures); nos testes é o `BancoFalso` em memória. Sem isso não dá para testar sem SQL Server.
2. **`Linha`** — leitura tolerante das colunas (NULL vira vazio), usada por todas as rotas e pelo falso.
3. **`Infra/Corpo.cs`** — lê o JSON da requisição de forma tolerante (campo com tipo errado vira "vazio" e cai na validação → 400, nunca 500), igual ao `str()` do Node. Também decide "verdadeiro" como o JavaScript (`enviar`).
4. **Políticas de acesso** (`login`, `aluno`, `professor`, `aluno-ou-professor`, `admin`) com respostas **401/403 em JSON** `{ "erro": "..." }` (o site depende desse campo).
5. **Limites de requisições** (o `express-rate-limit`): geral por IP (300/min), login/cadastro/conta por IP (10/min), escrita do professor e tickets por usuário/hora — todos configuráveis.
6. **Cabeçalhos de segurança** (o `helmet`): CSP, nosniff, X-Frame-Options, etc.; remoção do cabeçalho `Server`.
7. **Limite de corpo** (100 KB; 1,5 MB só na foto de perfil) → 413.
8. **Servir o site pela API** (`Plataforma:ServirSite`), proxy reverso (`ConfiarNoProxy`), log de requisições.
9. **Validação de configuração em produção** — recusa chave/senha de exemplo e e-mail não configurado.
10. **E-mail SMTP** pelo `SmtpClient` do .NET (sem pacote extra). Modo console igual ao do Node.
11. **Utilitários** `dotnet run -- --hash "senha"` e `dotnet run -- --chave`.
12. **`servidor-falso`** — `dotnet run --project api.testes -- servidor-falso` (era `npm run dev:falso`), com as rotas `/__dev/*` que só existem nele.
13. **`Hytech.sln`**, `launchSettings.json` (porta 3000, perfil que serve o site), `.gitignore` para C#.

## O que mudou de comportamento (leia antes de publicar)
- **Configuração:** `.env` → `appsettings.json` / user-secrets / variáveis de ambiente (`Jwt__Key`, `ConnectionStrings__HytechDb`, `Email__Host`...). A conexão com o banco agora é uma *connection string* única.
- **E-mail pela porta 465 não é suportado** (o `SmtpClient` só faz STARTTLS). Use **587**; a API avisa se você configurar 465.
- **Login:** `/auth/login` recusa admin (como o Node); `/auth/login-admin` continua para o desktop.
- **Token:** o papel vai no claim `role` (antes `papel`) e agora tem `nickname`. O site não lê o token, só o repassa; o HytechAdmin não é afetado.
- **Rotas por papel:** `/conta/*`, `/tickets/*` etc. agora recusam token de admin (403). O admin usa só `/admin/*`.
- **`/materias`** passou a vir de `sp_WebListarMaterias` (com `ordem` e `chavesParaDesbloquear`, que o site usa); `/admin/materias` continua com `sp_ListarMaterias`.
- **Datas** saem sem o "Z" (hora do servidor, como o `datetime` do banco). O Node marcava como UTC; se o servidor e o usuário estiverem no mesmo fuso, as horas agora ficam corretas.
- Rota/método inexistente devolve 404 em JSON (o Node também).

## Como foi verificado
- ✅ Compilou com .NET SDK 8 e **70 testes automáticos passaram** (os cenários das Fases 1–7 do Node, portados, mais testes de permissão do admin e de configuração).
- ✅ O `web/js/api.js` real foi exercitado no navegador contra a API em C# (login, estado, matérias, aulas, ranking, tickets) sem erro.
- ⚠️ O ambiente onde isto foi feito **não tinha acesso ao NuGet**. Por isso os 3 pacotes externos (`Microsoft.Data.SqlClient`, `BCrypt.Net-Next`, `Microsoft.AspNetCore.Authentication.JwtBearer`) foram substituídos por versões de teste só durante a verificação; o seu `.csproj` usa os pacotes oficiais. O código usa apenas a API desses pacotes que o `HytechApi` antigo já usava (mais `ValidAlgorithms`, da mesma biblioteca do JWT). **Rode `dotnet build` e `dotnet run --project api.testes` na sua máquina** para confirmar.
- ⚠️ **Nunca rodou contra SQL Server real nem com envio real de e-mail** (igual à situação do Node). Siga o checklist do `PUBLICACAO.md`.
- ℹ️ O token de quem desativa a conta continua valendo até expirar (120 min), como antes.
