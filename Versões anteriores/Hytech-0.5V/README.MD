# HyTech — Web (aluno e professor) + API Node.js

```
hytech/
├── api/                 Back-end em JavaScript (Node.js + Express). Fala com o SQL Server SÓ por procedures.
│   ├── server.js        Liga a API
│   ├── src/             config, db (procedures), auth (JWT), routes/
│   ├── sql/             HytechDB.sql (banco), 02_fase2_conteudo.sql (conteúdo), seed_admin.sql (1º admin)
│   ├── test/            Testes automáticos + servidor com banco falso
│   └── scripts/         gerar-hash.js (hash BCrypt para o admin)
├── web/                 Interface (index.html + assets + js/api.js)
├── docs/                MUDANCAS.md (passo a passo das fases), PUBLICACAO.md, roteiros em Word das fases 3, 4 e 5
└── reference-csharp/    API antiga em C# (só referência; pode apagar depois)
```

**Admin NÃO usa a web.** Fica no HytechAdmin (Windows Forms), no mesmo banco.

## Rodar (passo a passo)

1. **Banco:** rode no SQL Server, nesta ordem: `api/sql/HytechDB.sql`, depois `api/sql/02_fase2_conteudo.sql` (conteúdo das matérias) e `api/sql/03_fase4_tickets.sql` (leitura de tickets) e `api/sql/04_fase5_professor.sql` (área do professor). Todos são idempotentes.
2. **Admin inicial:** `cd api && npm install && npm run hash -- "SuaSenhaForte"`, cole o hash em `api/sql/seed_admin.sql` e rode no SQL Server.
3. **Configurar:** `cp .env.example .env` e preencha conexão com o banco e `JWT_KEY` (32+ caracteres).
4. **API:** `npm start` → http://localhost:3000
5. **Web:** sirva a pasta `web/` em http://localhost:5500 (ex.: extensão Live Server do VS Code, ou `npx serve web -l 5500`). Se usar outra porta, coloque-a em `CORS_ORIGINS` no `.env`. **Mais simples:** ponha `SERVIR_SITE=true` no `.env` e abra http://localhost:3000 (a API entrega o site).
6. Se a API não estiver em `localhost:3000`, defina antes do `js/api.js` no index.html:
   `<script>window.HYTECH_API_URL = 'https://sua-api';</script>`

### Sem SQL Server (só para ver o site funcionando)
`cd api && npm install && npm run dev:falso` — sobe a API com banco falso. Contas: `aluno@hytech.com` e `professor@hytech.com`, senha `senha1234`.

### Testes
`cd api && npm test`
