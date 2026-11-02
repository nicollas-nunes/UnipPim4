# Relatório de testes — HyTech (web: aluno + professor)

Data: 26/10/2026 · Ambiente: servidor de teste com **banco falso em memória** (sem SQL Server) · Navegador: Chromium automatizado (Playwright).

> **Limite deste relatório:** é um teste automático de "o que está quebrado" e de segurança básica. **Não é um teste de usabilidade com pessoas** (isso exige usuários reais; roteiro no fim). Nada foi testado em SQL Server real nem com envio real de e-mail.

## Resultado em uma linha
1 falha real encontrada e **já corrigida**; nenhum erro de JavaScript, imagem quebrada, rolagem lateral ou vazamento de dados nas telas testadas.

## 1. Telas (aluno, professor, públicas)
- Percorridas **17 telas** em 3 larguras (computador 1280 px, tablet 768 px, celular 390 px): home, sobre, dashboard, hub, trilha, ranking, desafios, tickets e perfil do aluno; dashboard, conteúdo, questões, meus conteúdos, desafios, alunos, tickets e perfil do professor; modais de login, cadastro e esqueci a senha.
- ✅ 0 erros de JavaScript no console · ✅ 0 requisições com erro (4xx/5xx) · ✅ 0 imagens quebradas · ✅ 0 telas com rolagem horizontal · ✅ 0 telas vazias · ✅ modais cabem no celular.
- ✅ Varredura do código: nenhum botão chama função inexistente; nenhuma imagem do HTML aponta para arquivo que não existe.
- Logout limpa token e sessão (nada da conta anterior fica).
- 🔧 Corrigido: 2 fotos de perfil sem texto alternativo (`alt`) e ausência de ícone do site (gerava um 404 no console).

## 2. Segurança da API (33 verificações)
Passaram: rotas protegidas exigem login (401) · token falso e token "alg:none" forjado recusados · aluno não acessa área do professor e vice-versa · **um aluno não lê nem escreve no ticket de outro** · injeção de SQL no login recusada · texto com HTML/script é guardado como texto (o site escapa ao exibir) · limites de tamanho (assunto 150, mensagem 4000, corpo da requisição) · JSON inválido dá 400 · sem `x-powered-by`, com Content-Security-Policy e `nosniff` · 404 sem detalhes internos · `/.env`, código da API e `../` não são servidos · login e `/aluno/estado` não devolvem senha/hash · questões enviadas ao aluno **não trazem o gabarito** · **código de e-mail é invalidado após 5 erros** (força bruta bloqueada) e a senha original continua valendo.

## 3. Falha encontrada e corrigida
| Gravidade | Problema | Correção |
|---|---|---|
| Média (robustez) | Mandar um objeto/lista no lugar de e-mail ou senha (ex.: `{"email":{"a":1}}`) fazia a API responder **500**. Em nome/assunto, o texto `[object Object]` podia ser gravado. | Campos de texto só aceitam texto; qualquer outro tipo dá **400**. Teste automático novo cobre 11 rotas × 4 tipos errados. 93 testes passam. |

Observação (não é falha): ticket alheio responde 400 "Você não participa deste ticket." (não vaza conteúdo; o padrão seria 403).

## 4. O que este teste NÃO cobre (riscos que continuam)
- **SQL Server real:** os scripts 02–05 nunca rodaram num banco de verdade.
- **E-mail real (Gmail):** só o modo console foi testado.
- **Limite de tentativas de login:** no servidor de teste o limite está alto; em produção é 10 por minuto por IP (configurável) — vale conferir atrás do proxy do Azure (`TRUST_PROXY`).
- **Token após desativar conta:** continua válido até expirar (até 120 min).
- **Texto da LGPD do cadastro** diz que os dados ficam "localmente neste dispositivo", o que não é verdade.
- Acessibilidade: só o básico foi verificado (textos alternativos, nomes de botões). Leitor de tela e navegação só por teclado não foram testados.
- Teste de carga/desempenho e testes em Safari/Firefox não foram feitos.

## 5. Roteiro de teste de usabilidade com pessoas (para vocês aplicarem)
Chame 5 pessoas (3 alunos, 2 professores), sem explicar o site. Peça tarefas e anote onde travam, quanto tempo levam e o que falam em voz alta:
1. **Aluno:** criar conta (com código do e-mail), entrar, abrir uma matéria e concluir uma aula.
2. **Aluno:** responder uma questão, ver as chaves ganhas, achar o ranking.
3. **Aluno:** terminar uma matéria e baixar o certificado em PDF.
4. **Aluno:** abrir um ticket para o professor e ler a resposta.
5. **Aluno:** trocar a senha e o e-mail no perfil; "esqueci a senha" no login.
6. **Professor:** enviar uma aula e uma questão para aprovação; ver o motivo se for rejeitada.
7. **Professor:** responder um ticket e ver o desempenho dos alunos.
Depois pergunte: "o que foi mais confuso?", "o que você esperava que acontecesse?" e dê nota de 1 a 5 para facilidade. Meça: tarefas concluídas sem ajuda (meta ≥ 80%) e tempo por tarefa. Teste também no celular.
