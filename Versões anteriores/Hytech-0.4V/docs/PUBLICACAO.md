# Publicar o HyTech (web: aluno + professor)

O admin continua só no HytechAdmin (Windows Forms), no mesmo banco.

## 1. Banco
1. Faça **backup** do banco atual.
2. Teste os scripts numa **cópia** do banco antes de rodar no real. Ordem: `HytechDB.sql`, `02_fase2_conteudo.sql`, `03_fase4_tickets.sql`, `04_fase5_professor.sql` (todos idempotentes). Os scripts 02–04 nunca rodaram em SQL Server real durante o desenvolvimento: se algum der erro, corrija o texto do erro e rode de novo.
3. Crie um **usuário SQL exclusivo da API** (não use `sa`) com permissão **só de EXECUTE** nas procedures (`GRANT EXECUTE ON SCHEMA::dbo TO hytech_api;`). A API só chama procedures.
4. Rode `seed_admin.sql` (com o hash gerado por `npm run hash -- "SuaSenhaForte"`) se ainda não houver admin. **Defina a especialidade de cada professor** no HytechAdmin (igual ao nome da matéria, ex.: "Programação"): ela decide em que matéria ele pode criar conteúdo e quem recebe os tickets.

## 2. Servidor (Node 18+)
```
cd api
npm ci --omit=dev
npm run chave        # gera um JWT_KEY forte
```
Crie o `api/.env` (nunca vai para o Git):
```
NODE_ENV=production
PORT=3000
DB_SERVER=...   DB_NAME=HytechDB   DB_USER=hytech_api   DB_PASSWORD=...
DB_ENCRYPT=true            # true se o SQL Server tem certificado válido
DB_TRUST_SERVER_CERT=false # false em produção com certificado válido
JWT_KEY=<saída de npm run chave>
JWT_EXPIRA_MINUTOS=120
SERVIR_SITE=true           # a API entrega o site da pasta web/ (um endereço só, sem CORS)
TRUST_PROXY=1              # se estiver atrás de Nginx/IIS/Cloudflare
CORS_ORIGINS=https://seu-dominio.com.br   # só se o site ficar em outro endereço
```
Com `NODE_ENV=production` a API **não sobe** se `JWT_KEY` ou a senha do banco forem os valores de exemplo.

## 3. HTTPS (obrigatório)
A API não faz HTTPS sozinha. Coloque na frente um proxy com certificado (Nginx, Caddy, IIS+ARR ou Cloudflare) apontando para `http://127.0.0.1:3000`, com `TRUST_PROXY=1`. Sem HTTPS, senha e token trafegam abertos.
Exemplo Caddy: `seu-dominio.com.br { reverse_proxy 127.0.0.1:3000 }`

## 4. Manter no ar
- **Linux:** `pm2 start server.js --name hytech && pm2 save && pm2 startup` (ou um serviço systemd).
- **Windows:** NSSM ou `pm2-windows-service`.
- O processo trata SIGTERM (fecha as conexões e sai).
- Monitoramento: `GET /saude` devolve `{"status":"ok"}`.
- Logs: uma linha por requisição (método, caminho, status, tempo), sem corpo, token ou e-mail. Guarde e gire os logs.

## 5. Checklist final (faça com o SQL Server real)
- [ ] Login de aluno e de professor; admin é recusado na web.
- [ ] Cadastro novo cria aluno zerado.
- [ ] Ler aula → F5 → continua lida; responder questão credita chaves; desbloquear matéria; certificado.
- [ ] Ticket: aluno abre, professor da matéria responde e encerra.
- [ ] Professor cria aula/questão/desafio → "Aguardando aprovação" → **aprovar no HytechAdmin** → aparece para o aluno → aluno conclui o desafio e ganha chaves.
- [ ] Rejeitar no HytechAdmin com motivo → o professor vê o motivo, edita e reenvia.
- [ ] Texto com `<script>` digitado em título/aula/ticket aparece como texto.
- [ ] `npm test` passa (67 testes, banco falso).
- [ ] `.env` fora do Git; senha do `sa` não é usada pela API; backup agendado do banco.

## 6. O que a revisão de segurança cobre / não cobre
Cobre: JWT com chave forte e expiração, bcrypt, limites de requisição (login 10/min, geral 300/min, escrita por usuário), corpo pequeno, CORS restrito, CSP e demais cabeçalhos (helmet), ids sempre do token, só procedures, erros sem detalhes, texto de usuário escapado no servidor e no site.
Não cobre: HTTPS (proxy), firewall do SQL Server, backups, atualização de dependências (rode `npm audit` de tempos em tempos), e-mail de recuperação de senha (não existe), 2FA.
