# HyTech — Web (aluno e professor) + HytechApi (C# / ASP.NET Core)

```
hytech/
├── api/                 HytechApi: back-end em C# (ASP.NET Core 8). Fala com o SQL Server SÓ por procedures.
│   ├── Program.cs       Entrada (e utilitários: --hash, --chave)
│   ├── HytechApp.cs     Monta o servidor: JWT, CORS, limites, cabeçalhos de segurança, rotas
│   ├── Opcoes.cs        Lê e valida a configuração (appsettings / user-secrets / variáveis de ambiente)
│   ├── Endpoints/       Auth, Conta, Plataforma, Aluno, Professor, Tickets e Admin (rotas do HytechAdmin)
│   ├── Data/            IDb + SqlDb (procedures) e Linha (leitura das colunas)
│   ├── Security/        BCrypt e JWT
│   ├── Services/        Verificacao (código por e-mail), Email (console/SMTP), Texto
│   ├── Infra/           Corpo JSON tolerante, tratamento de erros, 401/403 em JSON, limites
│   └── sql/             HytechDB.sql (banco), 02..05 (conteúdo, tickets, professor, e-mail), seed_admin.sql
├── api.testes/          70 testes automáticos + "servidor falso" (API com banco em memória, sem SQL Server)
├── web/                 Interface (index.html + assets + js/api.js)
├── docs/                MUDANCAS.md, MIGRACAO_NODE_PARA_CSHARP.md, PUBLICACAO.md, EMAIL_GMAIL.md, roteiros e relatórios
└── Hytech.sln
```

**A mesma API atende o site (aluno/professor) e o HytechAdmin (Windows Forms).** Uma API só, um banco só.
O Node.js foi removido: não é mais preciso instalar Node nem `npm`.

## Pré-requisito
[.NET SDK 8](https://dotnet.microsoft.com/download/dotnet/8.0) (`dotnet --version` deve mostrar 8.x).

## Rodar (passo a passo)

1. **Banco:** rode no SQL Server, nesta ordem: `api/sql/HytechDB.sql`, `02_fase2_conteudo.sql`, `03_fase4_tickets.sql`, `04_fase5_professor.sql` e `05_verificacao_email.sql` (todos idempotentes).
2. **Admin inicial:** `dotnet run --project api -- --hash "SuaSenhaForte"`, cole o hash em `api/sql/seed_admin.sql` e rode no SQL Server.
3. **Configurar:**
   ```
   dotnet run --project api -- --chave                                  # gera uma chave aleatória: copie o texto
   dotnet user-secrets set "Jwt:Key" "<cole a chave aqui>" --project api   # guarda fora do Git
   ```
   e ajuste `ConnectionStrings:HytechDb` em `api/appsettings.json` (ou, melhor, `dotnet user-secrets set "ConnectionStrings:HytechDb" "..." --project api`).
4. **API:** `dotnet run --project api` → http://localhost:3000
5. **Site — o jeito mais simples:** rode com `Plataforma__ServirSite=true` (ou escolha o perfil **"HytechApi (servindo o site)"** no Visual Studio) e abra http://localhost:3000: a API entrega o site.
   *Alternativa:* sirva a pasta `web/` em http://localhost:5500 (Live Server do VS Code); a porta já está liberada no CORS (`Cors:Origins`).
6. Se a API não estiver em `localhost:3000`, defina antes do `js/api.js` no index.html:
   `<script>window.HYTECH_API_URL = 'https://sua-api';</script>`

### Sem SQL Server (só para ver o site funcionando)
```
dotnet run --project api.testes -- servidor-falso
```
Sobe a API com banco falso em http://localhost:3000. Contas: `aluno@hytech.com` e `professor@hytech.com`, senha `senha1234`.
Para o site também sair dessa porta: `Plataforma__ServirSite=true` (Linux/macOS) ou `$env:Plataforma__ServirSite="true"` (PowerShell) antes do comando.

### Testes
```
dotnet run --project api.testes
```

Detalhes das rotas e da configuração: `api/README.md`. O que mudou do Node para o C#: `docs/MIGRACAO_NODE_PARA_CSHARP.md`.
