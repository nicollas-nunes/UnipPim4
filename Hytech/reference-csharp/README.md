# HytechApi — API da plataforma (ASP.NET Core 8)

Camada entre o web (`index.html`) e o SQL Server. Fala com o banco **somente por stored procedures**,
no mesmo padrão do HytechAdmin. Navegador → API (JWT) → banco.

## Rodar (desenvolvimento)

1. Rode `HytechDB.sql` no SQL Server.
2. Gere o hash da senha do admin e crie o admin (ver `sql/seed_admin.sql`):
   ```
   dotnet run -- --hash "SuaSenhaForte"
   ```
3. Defina a chave do JWT (mínimo 32 caracteres, aleatória; **não** vai no Git):
   ```
   dotnet user-secrets set "Jwt:Key" "troque-por-um-texto-aleatorio-bem-longo-123456"
   ```
4. Ajuste `ConnectionStrings:HytechDb` e `Cors:Origins` (endereço de onde o `index.html` é servido) no `appsettings.json`.
5. `dotnet run` e teste em `GET /`.

Em produção use variáveis de ambiente (`Jwt__Key`, `ConnectionStrings__HytechDb`) e HTTPS.

## Endpoints

| Método | Rota | Acesso | Procedure |
|---|---|---|---|
| POST | `/auth/login` | público | `sp_ObterUsuarioParaLogin`, `sp_RegistrarLogin` |
| POST | `/auth/cadastro` | público (**só aluno**) | `sp_CadastrarAluno` |
| GET | `/auth/me` | token | — |
| GET | `/config/chaves` | token | `sp_ObterConfiguracaoPlataforma` |
| GET | `/materias` | token | `sp_ListarMaterias` |

Erros de regra de negócio do banco voltam como `400 { "erro": "..." }`. Login inválido: `401`; conta inativa: `403`.

## Decisões de segurança

- **Cadastro público cria apenas aluno.** Professor e admin só pelo painel do admin.
- Login responde igual para e-mail inexistente e senha errada, e sempre confere um hash (sem vazar quais e-mails existem).
- Limite de 10 tentativas por minuto por IP em login e cadastro.
- CORS restrito às origens do `appsettings.json`.
- Senha mínima de 8 caracteres no cadastro (ajuste em `AuthEndpoints.cs` se o web usar outra regra).

## Usando no web (exemplo)

```js
const API = 'https://sua-api.exemplo.com';

async function login(email, senha) {
  const r = await fetch(API + '/auth/login', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email, senha })
  });
  const dados = await r.json();
  if (!r.ok) throw new Error(dados.erro);
  Auth.login({ ...dados.usuario, role: dados.usuario.papel });   // sessão em memória, como hoje
  window._token = dados.token;                                   // enviar nas próximas chamadas
}

async function chamar(rota) {
  const r = await fetch(API + rota, { headers: { Authorization: 'Bearer ' + window._token } });
  if (!r.ok) throw new Error((await r.json()).erro);
  return r.json();
}
// const chaves = await chamar('/config/chaves');   // substitui os 5 / 15 fixos no JS
```

## Próximos passos

Aulas, questões, desafios, ranking, perfil e tickets do aluno/professor. As procedures já existem
(`sp_ListarMateriasAluno`, `sp_ResponderQuestao`, `sp_ConcluirAula`, `sp_RankingAlunos`, `sp_AbrirTicket`...);
cada rota nova segue o padrão de `AuthEndpoints.cs`: ler o `id_aluno`/`id_professor` do token e chamar a procedure.
