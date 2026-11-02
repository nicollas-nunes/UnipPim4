# Publicar o HyTech (HytechApi em C# + web)

Uma API só (HytechApi) atende o site e o HytechAdmin (Windows Forms), no mesmo banco.

## 1. Banco
1. Faça **backup** do banco atual.
2. Teste os scripts numa **cópia** do banco antes de rodar no real. Ordem: `HytechDB.sql`, `02_fase2_conteudo.sql`, `03_fase4_tickets.sql`, `04_fase5_professor.sql`, `05_verificacao_email.sql` (todos idempotentes). Os scripts 02–05 nunca rodaram em SQL Server real durante o desenvolvimento: se algum der erro, corrija o texto do erro e rode de novo.
3. Crie um **usuário SQL exclusivo da API** (não use `sa`) com permissão **só de EXECUTE** nas procedures (`GRANT EXECUTE ON SCHEMA::dbo TO hytech_api;`). A API só chama procedures.
4. Rode `seed_admin.sql` (com o hash gerado por `dotnet run --project api -- --hash "SuaSenhaForte"`) se ainda não houver admin. **Defina a especialidade de cada professor** no HytechAdmin (igual ao nome da matéria, ex.: "Programação"): ela decide em que matéria ele pode criar conteúdo e quem recebe os tickets.

## 2. Servidor (.NET 8)
No servidor instale o **ASP.NET Core Runtime 8** (ou o SDK). Na sua máquina, gere o pacote:
```
dotnet publish api -c Release -o publicar
```
Copie a pasta `publicar` para o servidor e a pasta `web` ao lado dela (ou use `Plataforma__ServirSite` com `Plataforma__SiteDir` apontando para ela).
Gere uma chave forte: `dotnet publicar/HytechApi.dll --chave`.

Configure por **variáveis de ambiente** (nunca no Git; use `__` no lugar de `:`):
```
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://127.0.0.1:3000
ConnectionStrings__HytechDb=Server=...;Database=HytechDB;User Id=hytech_api;Password=...;Encrypt=True;TrustServerCertificate=False
Jwt__Key=<saída de --chave>
Jwt__ExpiraMinutos=120
Plataforma__ServirSite=true            # a API entrega o site da pasta web/ (um endereço só, sem CORS)
Plataforma__SiteDir=/caminho/para/web  # (relativo à pasta da API ou absoluto)
Plataforma__ConfiarNoProxy=true        # se estiver atrás de Nginx/IIS/Cloudflare
Cors__Origins__0=https://seu-dominio.com.br   # só se o site ficar em outro endereço
Email__Host=smtp.gmail.com  Email__Porta=587  Email__Usuario=...  Email__Senha=...   # ver EMAIL_GMAIL.md
```
Em produção a API **não sobe** se `Jwt__Key` ou a senha do banco forem os valores de exemplo, ou se o e-mail não estiver configurado.

## 3. HTTPS (obrigatório)
A API não faz HTTPS sozinha. Coloque na frente um proxy com certificado (Nginx, Caddy, IIS+ARR ou Cloudflare) apontando para `http://127.0.0.1:3000`, com `Plataforma__ConfiarNoProxy=true`. Sem HTTPS, senha e token trafegam abertos.
Exemplo Caddy: `seu-dominio.com.br { reverse_proxy 127.0.0.1:3000 }`

## 4. Manter no ar
- **Windows:** como serviço — `sc create HytechApi binPath= "C:\\hytech\\publicar\\HytechApi.exe" start= auto` (a API já entende o modo serviço do Windows) ou hospede no IIS (módulo ASP.NET Core Hosting Bundle).
- **Linux:** serviço systemd (`ExecStart=/usr/bin/dotnet /srv/hytech/publicar/HytechApi.dll`, `Restart=always`, variáveis em `Environment=` ou `EnvironmentFile=`).
- **Azure App Service:** publique a pasta e ponha as variáveis em **Configuração → Variáveis de ambiente**.
- O processo trata o desligamento normal (fecha as conexões e sai).
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
- [ ] `dotnet run --project api.testes` passa (70 testes, banco falso).
- [ ] Segredos (chave do JWT, senha do banco e do e-mail) fora do Git; senha do `sa` não é usada pela API; backup agendado do banco.

## 6. O que a revisão de segurança cobre / não cobre
Cobre: JWT com chave forte e expiração, bcrypt, limites de requisição (login 10/min, geral 300/min, escrita por usuário), corpo pequeno, CORS restrito, CSP e demais cabeçalhos de segurança, ids sempre do token, só procedures, erros sem detalhes, texto de usuário escapado no servidor e no site.
Não cobre: HTTPS (proxy), firewall do SQL Server, backups, atualização de pacotes (rode `dotnet list package --vulnerable` de tempos em tempos), 2FA.
