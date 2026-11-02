# HyTech — Relatório de Atualização (consolidado)

**Data:** 16/10/2026
**Compara:** `Hytech_antigo.rar` (versão de 13/10/2026) → `hytech_1.zip` (versão atual)
**Escopo:** ilustrações e ícones de **todo o site**: home, login, cadastro, "Conheça mais", área do aluno e **área do professor**. Todos os emojis usados como ícone foram trocados por imagens, e o projeto ganhou alguns comportamentos novos (confirmação de exclusão, olho da senha no login, seletor de especialidade).

Este documento junta tudo o que mudou do `Hytech_antigo.rar` até o `hytech_1.zip`. A **Parte 1** é o relatório da atualização de 13/10 (área do aluno), mantido como histórico. A **Parte 2** descreve o que mudou depois.

---

## 1. Resumo

| | Antigo (`.rar`) | Atual (`hytech_1.zip`) |
|---|---|---|
| `index.html` | 6.171 linhas | 6.305 linhas |
| Imagens em `assets/` | 38 (≈ 690 KB) | 78 (≈ 2,1 MB) |
| Imagens em `originais/` | 20 | 26 |
| Arquivos no projeto | 60 | 106 |
| Arquivos removidos | — | nenhum |

- **46 arquivos novos** (40 em `assets/` e 6 em `originais/`).
- **4 imagens alteradas de verdade:** as imagens das 4 matérias.
- **52 imagens "modificadas" só em metadados:** os pixels são idênticos aos do antigo. Cada arquivo ganhou um bloco extra de metadados (`caBX` nos PNG e `C2PA` nos WebP, de credenciais de conteúdo), cerca de 5,7 KB a mais por imagem. Não muda nada visualmente. Se quiser arquivos mais leves, esse bloco pode ser removido sem perda.
- **135 trechos do `index.html` alterados** (a maior parte é troca de emoji por imagem).

---

## 2. Estrutura de pastas

```
hytech/
├── index.html
├── README.md
├── assets/
│   ├── badges/
│   │   ├── conquistas/    8 imagens (.webp, 192×192)
│   │   └── niveis/        5 imagens (.webp, 256×256)
│   ├── conheca-mais/      trilha, hub-aulas, perfil (.webp)                         [novo]
│   ├── conta/             aluno, professor, cadastro (.webp, 384×384)               [novo]
│   ├── home/              facil, pontos, certificacoes (.webp)                      [novo]
│   ├── icons/
│   │   ├── atalho/        conteudo.webp, desafio.webp
│   │   ├── desafio/       sem-desafio.webp
│   │   ├── menu/          inicio, modulos, jogar, desafios, ranking, configuracoes, ticket-suporte (.png, 52×52)
│   │   ├── perfil/        chave, fases, fases-concluidas, ranking, ver-perfil (.png)
│   │   ├── senha/         senha-aberta.png, senha-oculta.png                        [novo]
│   │   ├── ticket/        ticket.png, chat.png
│   │   └── trilha/        alerta.png, cadeado.png, cadeado-aberto.png, acerto.png   [3 novos]
│   ├── mascote/           mascote-home.webp, emote_cabeca_05.png, emote_cabeca_10.png [2 novos]
│   ├── materias/          programacao, design, banco-de-dados, redes (.webp, 320×320; imagens refeitas)
│   ├── professor/                                                                   [novo]
│   │   ├── acoes/         editar, enviar, excluir, rascunho (.png)
│   │   ├── desempenho/    alunos-ativos, aprovados, certificados-emitidos (.webp), email.png
│   │   ├── inicio/        aguardando-aprovacao, alunos-ativos, aprovados, conteudos-enviados, enviar-com-questoes, enviar-conteudo (.webp)
│   │   ├── menu/          enviar-conteudo, enviar-questoes, meus-conteudos, desempenho-alunos (.png)
│   │   ├── meus/          questoes.webp, textos-explicativos.webp
│   │   ├── perfil/        alunos-ativos, aprovados, conteudos-enviados (.webp)
│   │   └── ticket/        nenhum-ticket.webp
│   └── social/            instagram-white.png, facebook-white.png
└── originais/             arquivos originais (badges, icons, materias)
    └── professor/meus-conteudos-e-desafios/   enviar, excluir, lapis, questoes, rascunho, textos-explicativos (.png)   [novo]
```

---

# PARTE 1 — Atualização de 13/10/2026 (área do aluno)

Os emojis usados como ícones e ilustrações da área do aluno foram substituídos por imagens PNG e WebP, organizadas na pasta `assets/`. O `index.html` passou a carregar essas imagens, e o projeto precisa da pasta `assets/` ao lado dele para funcionar.

### Home (página pública)
- **Mascote da HyTech** no lugar do emoji de computador (🖥️) do destaque principal.
  - Imagem: `assets/mascote/mascote-home.webp`.
  - Tamanho maior (440×440 px, antes a caixa era 260×200), com brilho lavanda atrás para destacar do fundo roxo e uma animação leve de flutuação, que é desativada para quem prefere menos movimento.
  - No celular (até 768 px) o mascote agora aparece, com 260×260 px, abaixo do texto. Antes a imagem ficava escondida.
- **Redes sociais do rodapé:** os ícones passaram de `banco de imagens/instagramWhite.png` e `facebookWhite.png` para `assets/social/instagram-white.png` e `facebook-white.png`, com texto alternativo.

### Menu lateral (todas as páginas do aluno)
- Os 7 itens trocaram emoji por ícone PNG: Início, Acessar módulo, Jogar agora, Desafios, Ranking, Configurações e Ticket de suporte (`assets/icons/menu/`).
- **Ilustração do topo da sidebar** passou a ser uma imagem em cada página:

| Página | Antes | Agora |
|---|---|---|
| Início | 🖥️ | ícone de início (casinha) |
| Acessar módulo | 🖥️ | imagem da matéria selecionada |
| Jogar agora | 🧭 | imagem da matéria selecionada |
| Desafios | 🎯 | ícone de desafios |
| Ranking | 🏆 | ícone de medalha |
| Configurações | 👤 | ícone de ver perfil (branco) |
| Ticket de suporte | 🎫 | ícone de ticket |

- **Sidebar padronizada:** o menu mudava de posição ao navegar entre as páginas, porque o topo tinha alturas diferentes. Agora a área da ilustração tem altura fixa (hoje 136 px, igual na área do professor — ver Parte 2) em Início, Acessar módulo, Desafios, Ranking, Configurações e Ticket de suporte. Em Acessar módulo a imagem da matéria ficou em 64 px. A página Jogar agora manteve o tamanho próprio (104 px).

### Painel do perfil (menu do avatar)
- Chaves, posição no ranking e fases agora usam ícones (`chave`, `ranking`, `fases`) em vez de 🗝️ 🏆 ▶️.
- Botões "Editar perfil" e "Ver perfil" ficaram sem emoji.

### Início (dashboard)
- Cards de atalho "Conteúdo" e "Desafio" com imagens WebP (`assets/icons/atalho/`), no lugar de 📖 e 🎮.

### Acessar módulo (Hub de aulas)
- Lista de matérias com a imagem de cada matéria (Programação, Design, Banco de Dados e Redes), no lugar de 💻 🎨 🗄️ 🌐.
- Selos "Vale 3 Chaves" e "Vale 5 Chaves" com o ícone de chave em imagem.

### Jogar agora (Trilhas)
- Sidebar com as imagens das matérias.
- Tela "Conclua os Tópicos Primeiro!": o emoji 📚 foi trocado pelo ícone de alerta (`assets/icons/trilha/alerta.png`, 56 px).
- Cards de questão com o ícone de chave em imagem.

### Desafios
- Título "Desafios" sem emoji.
- Abas de filtro (Todas, Programação, Design, Banco de Dados, Redes) sem emoji, com a imagem de cada matéria.
- Tela sem desafios com a ilustração `sem-desafio.webp`, no lugar do 🎯.

### Ranking
- **Níveis:** os 5 níveis (Iniciante, Explorador, Aprendiz, Desenvolvedor e Mestre Python) agora têm imagem própria, no lugar de 🥚 🌱 ⚡ 🔥 🐍.
- **Conquistas:** as 8 conquistas têm imagem própria (Primeiro Passo, Leitor, Coletor, Desafiante, Desbravador, Certificado I, All Star e Pythonista). As ainda bloqueadas aparecem esmaecidas em tons de cinza. Antes elas mostravam um 🔒.
- **Progresso por matéria:** ícones de matéria em imagem.
- **Títulos sem emoji:** "Progresso Geral", "Progresso por Matéria" e "Conquistas".
- O texto do próximo nível ("Explorador (19 chaves)") usa o ícone de chave em imagem.

### Configurações (perfil)
- Resumo do perfil (chaves, posição no ranking e fases concluídas) com ícones em imagem.
- Campos do formulário sem os ícones de emoji.

### Ticket de suporte
- Ícones de ticket e de chat em imagem (`assets/icons/ticket/`).
- Botão "Enviar Ticket" sem emoji.
- Mensagem de "nenhum ticket aberto" com o ícone em imagem.

## 4. Mudanças técnicas no `index.html`

**CSS**
- Nova seção `ÍCONES E IMAGENS (pasta assets/)`, com a classe base `.ico` e as classes `.ico-inicio`, `.ico-modulos`, `.ico-jogar`, `.ico-desafios`, `.ico-ranking`, `.ico-config`, `.ico-ticket`, `.ico-chave`, `.ico-medalha`, `.ico-fases`, `.ico-play` e `.ico-user`.
- Classes de tamanho para as imagens: `.mat-ico*`, `.chave-ico`, `.nivel-img`, `.conq-img`, `.es-img`, `.tv-img` e `.acao-card-img img`.
- `.hero-imagem` reescrita para o mascote (brilho, sombra, animação e versão para celular).
- Regras da sidebar padronizada, aplicadas só às páginas do aluno, exceto Jogar agora.

**JavaScript**
- Novas constantes e funções: `MAT_IMG`, `matImg()`, `CHAVE_IMG`, `IMG_NIVEIS`, `IMG_CONQUISTAS`, `_setNivelBadge()`, `_setMascoteMateria()` e `_esc()`.
- Os dados de `NIVEIS` e `CONQUISTAS_DEF` passaram de um campo `icone` (emoji) para um campo `img` (nome do arquivo).
- `renderHub()` e `renderTrilha()` atualizam a imagem da matéria na sidebar.

**Correção**
- O id da aba "Banco de Dados" em Desafios era `dab-aluno-BancoDeDados`. O JavaScript monta o id removendo os espaços do nome da matéria, que resulta em `BancodeDados`. O id foi ajustado para esse valor, e a aba passa a ficar destacada ao ser clicada.

---

# PARTE 2 — Atualizações posteriores (até o `hytech_1.zip`)

## 3. O que mudou, por tela

### Home, login, cadastro e "Conheça mais"
- **Home:** os emojis 🎯 💎 🏅 dos cards "Diversão e Aprendizado" viraram imagens (`assets/home/facil`, `pontos` e `certificacoes`).
- **Login:**
  - A imagem acima de "Faça seu Login" troca entre aluno e professor conforme o botão selecionado (`assets/conta/aluno.webp` e `professor.webp`), no lugar do 👤.
  - Os botões Aluno e Professor ficaram sem emoji (🎓 📋).
  - O campo de senha **ganhou o botão de olho**, que não existia.
- **Cadastro:** o 🚀 do topo virou `assets/conta/cadastro.webp`, e o botão ficou só "Criar Conta".
- **Senha (login e cadastro, 2 campos):** o olho usa `senha-oculta.png` (senha escondida) e `senha-aberta.png` (senha visível), no lugar de 👁️ e 🙈. Os campos ganharam espaço à direita para o botão não cobrir o texto.
- **Conheça mais:** 🗺️ 🎓 👤 viraram `trilha`, `hub-aulas` e `perfil` (`assets/conheca-mais/`).
- **Mensagem "Conta criada!":** o 🎉 virou o ícone de acerto.

### Modais com o mascote
- **Sair da conta?** O 👋 virou o mascote `emote_cabeca_10.png`.
- **Confirmar exclusão (novo):** modal "Deseja mesmo excluir?" com o mascote `emote_cabeca_05.png` e os botões Não/Sim. Ele aparece ao excluir conteúdo, questão ou desafio (antes a exclusão era direta).

### Área do aluno — ajustes de ícones (16/10)
- **Acessar módulo:**
  - Tópico concluído: o ✅ do card e o do botão "Lido!" viraram `professor/desempenho/aprovados.webp`.
  - **Conteúdos Extras do Professor:** 📝 e 📄 viraram `professor/meus/textos-explicativos.webp`, e o 👨‍🏫 do autor virou `conta/professor.webp`.
  - A imagem da matéria na lista do hub ficou maior (36 → 46 px).
- **Trilhas (Certificado de Conclusão):**
  - A medalha 🏅 (grande e a marca d'água do fundo) virou `professor/desempenho/certificados-emitidos.webp`.
  - "Ir para a matéria": o ▶️ virou `icons/perfil/fases.png`.
  - "Baixar Comprovante TXT": o 📄 virou `certificados-emitidos.webp`.
- **Chaves, cadeados e acerto:**
  - 🗝️/🔑 viraram `icons/perfil/chave.png` (selo "+15 Chaves", mensagens "+5 chaves" etc.).
  - 🔒/🔓 viraram os novos `cadeado.png` e `cadeado-aberto.png` (cards "Bloqueada", "Você precisa de X chaves", botão de desbloquear, progresso por matéria).
  - O 🎉 de "Correto! +5 chaves conquistadas!" e de "Você concluiu todas as matérias!" virou o novo `acerto.png`. Em "Nível máximo!" e "Mestre!" o emoji foi removido.
  - Nos 3 ícones novos, o traço é preto e redondo, no mesmo estilo dos demais. Nos fundos escuros (certificado e mensagens rápidas) eles ficam brancos.
- **Ticket de suporte (aluno e professor):** nas mensagens, o 🎓 virou `conta/aluno.webp` e o 👨‍🏫 virou `conta/professor.webp`. Na lista de tickets recebidos do professor, o 👤 virou `conta/aluno.webp`.
- **Matérias (dados internos):** o campo `icone` de cada matéria deixou de ter emoji (💻 🎨 🗄️ 🌐), porque a tela agora usa as imagens das matérias.
- **Imagens das matérias refeitas:** `programacao`, `design`, `banco-de-dados` e `redes` (`assets/materias/`) continuam com 320×320 px, mas o hexágono agora ocupa quase todo o quadro (recorte mais justo, antes havia muita margem). Elas aparecem maiores nos mesmos lugares.

### Área do professor (toda nova)
- **Menu lateral (todas as páginas do professor):**
  - Os itens trocaram emoji por ícone: Início, Enviar conteúdo, Enviar questões, Meus conteúdos, Desempenho alunos, Desafios, Meu perfil e Tickets de suporte.
  - **"Painel" foi renomeado para "Início"** (menu, título da seção e caminho de navegação).
  - O selo "👨‍🏫 Professor" ficou só "Professor".
  - O topo do menu, que tinha um emoji diferente em cada página (👤 🎯 👨‍🏫 📝 ❓ 📂 📊 🎫), agora mostra `conta/professor.webp` em todas.
- **Sidebar padronizada:** a área da ilustração tem a mesma altura (136 px) em **todas** as páginas, de aluno e de professor, para o menu não "pular" ao navegar. Isso substitui a regra de 96 px, que valia só para o aluno.
- **Início (dashboard):**
  - "Bem-vindo, Prof. …!" mostra a imagem do professor no lugar do 👨‍🏫.
  - Os cards de métricas (conteúdos enviados, aprovados, aguardando aprovação, alunos ativos) e os cards de ação (enviar conteúdo e enviar com questões) usam imagens em `assets/professor/inicio/`.
- **Enviar conteúdo / Enviar questões:** botões "Salvar rascunho" e "Enviar" com os ícones de ação (`assets/professor/acoes/`).
- **Meus conteúdos:**
  - As abas "Textos Explicativos" e "Questões" e os cards de cada item usam `textos-explicativos.webp` e `questoes.webp`, no lugar de 📄 ❓ 📝.
  - Os botões Editar, Enviar e Excluir usam os ícones de ação, no lugar de ✏️ 📤 🗑.
  - O estado vazio ("Nenhum conteúdo ainda") tem imagem.
- **Desempenho dos alunos:** métricas com imagem (`assets/professor/desempenho/`). Na tabela, 🗝️ virou o ícone de chave, 🏅 virou `certificados-emitidos` e 📧 virou `email.png`.
- **Desafios:**
  - Título sem emoji.
  - Abas e opções de matéria com a imagem de cada matéria (as opções do formulário ficaram só com o nome, porque `<option>` não aceita imagem).
  - Botões Editar, Enviar e Excluir com ícones. O de excluir fica só com o ícone e a dica "Excluir".
  - O selo de matéria no card ficou só com o texto.
  - Tela sem desafios com `sem-desafio.webp`.
- **Meu perfil:**
  - Resumo (conteúdos enviados, aprovados, alunos ativos) com imagens de `assets/professor/perfil/`.
  - Botões "Editar" e "Alterar" com o ícone `editar.png`.
  - Os emojis dos campos (😊 ✉️ 👤 🎓 🔒) foram removidos.
  - A foto de perfil do professor agora tem o campo de arquivo escondido corretamente (`#input-foto-prof`).
- **Especialidade:**
  - Ao editar, o `<select>` com emojis foi trocado por **4 botões com a imagem de cada matéria**.
  - A especialidade aparece com a imagem da matéria no perfil. No selo do topo, aparece só o texto, sem emoji.
- **Tickets do professor:** estado vazio com `nenhum-ticket.webp` (no lugar do 📭) e ícone de chat no lugar do 💬.

---

## 4. Mudanças técnicas no `index.html`

**CSS**
- Novas classes de ícone do professor: `.ico-p-conteudo`, `.ico-p-questoes`, `.ico-p-meus` e `.ico-p-desempenho`. Elas usam a mesma classe base `.ico` do menu do aluno. O menu do professor reaproveita `.ico-inicio`, `.ico-desafios`, `.ico-user` e `.ico-ticket`.
- Classes de tamanho para imagens: `.ico-img` (com `.ico-lg`, `.ico-sm` e `.inv`), `.btn-ico` (com `.btn-ico-solo`), `.aba-ico`, `.cert-ico`, `.email-ico`, `.esp-ico`, `.es-img-prof`, `.mascote-prof`, `.bv-prof-ico`, `.mascote-saida`, `.modal-img` e `.cadastro-img`.
- Novas regras: `.btn-olho` (botão de olho da senha), `.esp-opcoes` e `.esp-opcao` (seletor de especialidade), `.sidebar-mascote-emoji` (altura fixa de 136 px) e `.desafio-acoes`. Nos botões roxos, o ícone fica branco por inversão de cor (`.btn-roxo .btn-ico`).
- `.topico-card.concluido h3::after` e `.certificado-box::before` passaram de emoji para imagem de fundo.
- A regra da sidebar de 96 px (só aluno) foi removida.

**JavaScript**
- `mostrarToast()` agora converte automaticamente os emojis 🗝️ 🔑 🔒 🔓 🎉 no início da mensagem em ícones. A mensagem "✅ Conteúdo publicado!" / "✅ Questão publicada!" usa o ícone de aprovado.
- `toggleSenhaCad()` passou a trocar a imagem do olho em vez do texto.
- Novas funções: `pedirExclusao()`, `cancelarExclusao()`, `confirmarExclusao()` (modal de exclusão), `selecionarEspProf()`, `_setEspDisplayProf()` (especialidade com imagem) e `_setBoasVindasProf()` (boas-vindas com imagem).
- O login troca a imagem de perfil (aluno/professor) ao mudar o botão selecionado.
- Os mapas de ícones por matéria (`icones`, `_MAT_ICONE` e `icone` em `MATERIAS_DATA`) foram esvaziados, já que as telas usam as imagens das matérias.
- As exclusões de conteúdo, questão e desafio agora passam por `pedirExclusao()` antes de chamar `excluirConteudo()`, `excluirQuestao()` e `excluirDesafio()`.
- Em `atualizarInterface()`, o título `prof-boas-vindas` deixou de ser preenchido como texto. Quem preenche agora é `_setBoasVindasProf()`. A entrada `prof-boas-vindas-x` que sobrou na lista de campos não aponta para nenhum elemento (neutraliza a linha antiga) e pode ser apagada numa limpeza futura.

---

## 5. Verificação
- Todas as 78 imagens referenciadas no código existem em `assets/`, e nenhum arquivo da pasta ficou sem uso.
- O JavaScript do `index.html` passa na verificação de sintaxe.
- Nenhum arquivo do projeto antigo foi removido.

## 6. O que ainda usa emoji (para próximas rodadas)
- **Confirmações e avisos:** ✅ (21 usos, em mensagens rápidas, "Concluída" nos cards de questão e senha), ⚠️ (19, nas mensagens de validação do cadastro), ❌ e 💡.
- **Perfil do aluno:** ✏️ (botões Editar e Alterar) e 📷 (foto).
- **Status dos desafios:** 🟢 🟡 🔴 (dificuldade) e ⏳.
- **Conteúdo extra do professor:** 📅 🗂 📚 (datas, tópicos e matérias nos cards).
- **Outros:** 🔥 (desafios), 🏆 (card "Certificado" e mensagem de matéria concluída), 🗑 e 📤 em mensagens, 🖥️ (Conheça mais), 🔍, 🎯, 💾 e ✕ (fechar modais).
- Os 🗝️ 🔑 🔒 🔓 🎉 que ainda aparecem no código estão só em mensagens rápidas e são convertidos em ícone na hora de exibir.
