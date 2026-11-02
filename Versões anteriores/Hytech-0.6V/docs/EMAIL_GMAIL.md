# Enviar os códigos por e-mail (Gmail)

Sem configurar nada, a API funciona em **modo console**: o e-mail com o código aparece no terminal onde a API está rodando. Serve para testar. Para enviar de verdade:

## 1. Crie (ou use) uma conta Gmail só para o HyTech
Ex.: `hytech.plataforma@gmail.com`. Evite usar a sua conta pessoal.

## 2. Ative a verificação em duas etapas
Conta Google → **Segurança** → **Verificação em duas etapas** → ativar (celular). Sem isso o Google não deixa criar a senha de app.

## 3. Crie uma "senha de app"
Conta Google → Segurança → **Senhas de app** (ou pesquise "senhas de app" na busca da conta) → nome "HyTech API" → **Criar**. O Google mostra **16 letras**. Copie (aparece só uma vez). **Não** é a senha normal do Gmail.

## 4. Preencha o `.env` da API
```
SMTP_HOST=smtp.gmail.com
SMTP_PORT=465
SMTP_USER=hytech.plataforma@gmail.com
SMTP_PASS=abcdefghijklmnop
EMAIL_DE=HyTech <hytech.plataforma@gmail.com>
```
Reinicie a API. Ao subir sem erro, o modo é "smtp". Faça um cadastro de teste com um e-mail seu: o código chega na caixa de entrada (olhe também o spam na primeira vez).

## Cuidados
- O `.env` **nunca** vai para o Git (já está no `.gitignore`).
- Gmail gratuito limita cerca de 500 e-mails por dia: serve para a banca/demonstração. Para uso real em escala, troque por um serviço de envio (SendGrid, Amazon SES, Azure Communication Services): é só mudar `SMTP_HOST/PORT/USER/PASS`.
- No Azure, coloque essas variáveis em **Configuração → Variáveis de ambiente** do App Service (não no código).
- Se o envio falhar, o site mostra "Não foi possível enviar o e-mail agora" (erro 503) e o motivo real aparece só no log da API.
- Enquanto não houver servidor, mantenha o modo console.
