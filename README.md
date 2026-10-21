<div align="center">

# 🚀 Projeto HYTECH

### Microlearning Gamificado

![Segmento](https://img.shields.io/badge/segmento-EdTech-7c3aed)
![Foco](https://img.shields.io/badge/foco-Microlearning%20Gamificado-2563eb)
![PIM 3](https://img.shields.io/badge/PIM%203-conclu%C3%ADdo-success)
![PIM 4](https://img.shields.io/badge/PIM%204-em%20andamento-orange)
![LGPD](https://img.shields.io/badge/LGPD-conforme-blue)

</div>

> *"A HYTECH surge para transformar a educação técnica: uma edtech focada em microlearning gamificado que transforma conteúdos densos de programação em uma experiência interativa, fluida e motivadora."*

**Desenvolvimento de plataforma web de avaliação e apoio à aprendizagem para uma empresa fictícia de tecnologia educacional.**

### Hytech

Tecnologia Educacional com foco em Gamificação e Ensino de Tecnologia. A empresa atua no nicho de microlearning, oferecendo conteúdos densos de tecnologia fragmentados em desafios interativos em um sistema gamificado.

<div align="center">

🎯 [Desafio](#desafio) | 💡 [Solução](#solução) | 🏢 [Empresa](#1-caracterização-da-empresa-e-visão-de-negócio) | 📋 [Requisitos](#2-matriz-de-requisitos) | 🔄 [Backlog](#3-backlog-unificado-pim-3--pim-4) | ✅ [DoR e DoD](#4-definition-of-ready-dor) | 👥 [Equipe](#equipe)

</div>

---

## Sumário

- [Desafio](#desafio)
- [Solução](#solução)
1. [Caracterização da Empresa e Visão de Negócio](#1-caracterização-da-empresa-e-visão-de-negócio)
2. [Matriz de Requisitos](#2-matriz-de-requisitos)
   - [Requisitos Funcionais (RF)](#requisitos-funcionais-rf)
   - [Requisitos Não Funcionais (RNF)](#requisitos-não-funcionais-rnf)
3. [Backlog Unificado (PIM 3 + PIM 4)](#3-backlog-unificado-pim-3--pim-4)
   - [Progresso](#progresso)
   - [PIM 3 — Concluído](#pim-3--concluído)
   - [PIM 4 — Pendente](#pim-4--pendente)
4. [Definition of Ready (DoR)](#4-definition-of-ready-dor)
5. [Definition of Done (DoD)](#5-definition-of-done-dod)
- [Equipe](#equipe)

---

## Desafio

O desafio é criar uma plataforma web de avaliação e apoio à aprendizagem para uma empresa fictícia de tecnologia educacional, integrando conceitos de engenharia de software ágil, modelagem de dados, programação orientada a objetos, desenvolvimento web responsivo, UX/UI design e análise de dados, com foco na tomada de decisão baseada em informações de desempenho dos usuários.

[⬆ Voltar ao sumário](#sumário)

---

## Solução

O app irá auxiliar o conhecimento do usuário na área de TI através de atividades específicas e entrega de resultados de progresso em pontos, podendo assim se aperfeiçoar pessoalmente.

[⬆ Voltar ao sumário](#sumário)

---

## 1. Caracterização da Empresa e Visão de Negócio

| Item | Descrição |
|------|-----------|
| 🏢 **Nome da Empresa** | HYTECH |
| 🎯 **Segmento** | EdTech / Tecnologia Educacional focada em Gamificação e Microlearning |
| 👥 **Público-Alvo** | Jovens e adultos (15 a 30 anos), estudantes de TI e pessoas em transição de carreira ("Duolingo da Programação") |

### Módulos da plataforma

| Módulo | Descrição |
|--------|-----------|
| 🕹️ **Trilha dos Desafios** | Mapa de progressão gamificado por fases e níveis interativos. |
| 📚 **Hub Aulas** | Suporte teórico rápido com vídeos, textos e auxílio de Inteligência Artificial. |
| 🏆 **Central do Jogador** | Dashboard de métricas (streaks, pontos), perfil, suporte e gestão LGPD. |

[⬆ Voltar ao sumário](#sumário)

---

## 2. Matriz de Requisitos

### Requisitos Funcionais (RF)

| Código | Descrição Resumida | Ator Principal | Função / Escopo |
|--------|--------------------|----------------|-----------------|
| **RF001** | Gerenciamento de Usuários | Administrador | Criar, editar, ativar, inativar contas e definir níveis de acesso (RBAC). |
| **RF002** | Monitoramento da Plataforma | Administrador | Visualizar métricas, gerenciar tickets de suporte, logs e regras de gamificação. |
| **RF003** | Gerenciamento de Conteúdo | Professor / Gestor | Criar e organizar textos, questões, desafios, simulados e trilhas de aprendizagem. |
| **RF004** | Submissão e Acompanhamento | Professor / Gestor | Submeter conteúdos para aprovação, acompanhar desempenho de turmas e responder alunos. |
| **RF005** | Acesso a Conteúdos e Exercícios | Aluno | Visualizar aulas, navegar pelas trilhas de estudo, resolver questões e simulados. |
| **RF006** | Acompanhamento de Progresso | Aluno | Consultar pontuação, ver ranking global, emitir certificados e gerenciar perfil. |
| **RF007** | Autenticação e Cadastro | Todos os Usuários | Realizar login, auto-registro, redefinição de senha e aceite dos termos de privacidade (LGPD). |

### Requisitos Não Funcionais (RNF)

| Código | Categoria | Descrição Resumida | Critério de Aceite / Padrão |
|--------|-----------|--------------------|------------------------------|
| **RNF001** | Responsividade | Adaptação de layout da interface | O sistema deve funcionar perfeitamente em telas de desktops, tablets e smartphones. |
| **RNF002** | Segurança | Proteção de credenciais e sessões | Utilização de hashing seguro para senhas (ex: BCrypt/Argon2) e tokens de acesso (JWT). |
| **RNF003** | Controle de Acesso | Restrição por perfil de usuário | Restringir recursos e rotas da API estritamente de acordo com o nível de acesso (RBAC). |
| **RNF004** | Escalabilidade | Suporte a cargas de acessos | A infraestrutura deve suportar o crescimento gradual de usuários sem queda de desempenho. |
| **RNF005** | Usabilidade | Facilidade de navegação | Interface intuitiva com feedback visual instantâneo para ações e erros do usuário. |
| **RNF006** | Compatibilidade | Suporte a navegadores | Compatibilidade total com os principais navegadores modernos (Chrome, Edge, Firefox, Safari). |
| **RNF007** | Integridade dos Dados | Persistência e consistência | Garantir que dados de progresso, cadastros e notas não sejam corrompidos ou perdidos. |
| **RNF008** | Legislação (LGPD) | Privacidade e consentimento | Coleta de aceite explícito dos termos de uso e política de tratamento de dados pessoais. |
| **RNF009** | Desempenho | Tempo de resposta do sistema | O tempo de resposta das requisições principais da API deve ser de no máximo 2 segundos. |

[⬆ Voltar ao sumário](#sumário)

---

## 3. Backlog Unificado (PIM 3 + PIM 4)

### Progresso

| Escopo | Concluídas | Pendentes | Total |
|--------|:----------:|:---------:|:-----:|
| PIM 3 | 13 | 0 | 13 |
| PIM 4 | 0 | 10 | 10 |
| **Geral** | **13** | **10** | **23** |

### PIM 3 — Concluído

| ID | Tarefa | Prioridade | Status | Escopo |
|----|--------|:----------:|:------:|:------:|
| T01 | Definir negócio e público-alvo (Empresa Fictícia HYTECH) | Alta | ✅ Concluído | PIM 3 |
| T02 | Identificar stakeholders (Alunos, Professores, Admins) | Alta | ✅ Concluído | PIM 3 |
| T03 | Levantar requisitos funcionais e não funcionais | Alta | ✅ Concluído | PIM 3 |
| T04 | Organizar e priorizar o backlog inicial | Alta | ✅ Concluído | PIM 3 |
| T05 | Planejar iterações (Sprints do PIM 3) | Média | ✅ Concluído | PIM 3 |
| T06 | Desenvolver Diagramas (Caso de Uso, Classe e Sequência) | Alta | ✅ Concluído | PIM 3 |
| T07 | Modelar banco de dados (Conceitual e Lógico) | Alta | ✅ Concluído | PIM 3 |
| T08 | Desenvolver Protótipos UX/UI (Wireframes HYTECH) | Alta | ✅ Concluído | PIM 3 |
| T09 | Garantir Responsividade na Interface | Alta | ✅ Concluído | PIM 3 |
| T10 | Implementar Frontend Estático (HTML, CSS, JS base) | Média | ✅ Concluído | PIM 3 |
| T11 | Avaliar Usabilidade e ajustar navegação | Baixa | ✅ Concluído | PIM 3 |
| T12 | Organização da comunicação do grupo | Baixa | ✅ Concluído | PIM 3 |
| T13 | Avaliação final do PIM 3 | Média | ✅ Concluído | PIM 3 |

### PIM 4 — Pendente

| ID | Tarefa | Prioridade | Status | Escopo |
|----|--------|:----------:|:------:|:------:|
| T14 | Desenvolvimento Backend (API REST / Servidor) | Alta | ⏳ Pendente | PIM 4 |
| T15 | Implementação de Banco de Dados Relacional/NoSQL (Físico) | Alta | ⏳ Pendente | PIM 4 |
| T16 | Integração Frontend com Backend (Fetch / AJAX) | Alta | ⏳ Pendente | PIM 4 |
| T17 | Implementação de Autenticação Real (JWT / Sessões / LGPD) | Alta | ⏳ Pendente | PIM 4 |
| T18 | CRUD completo do Painel de Professor e Envio de Desafios | Alta | ⏳ Pendente | PIM 4 |
| T19 | Motor de Regras de Gamificação e Persistência do Ranking | Média | ⏳ Pendente | PIM 4 |
| T20 | Backend do Sistema de Tickets (Atendimento em tempo real/persistido) | Média | ⏳ Pendente | PIM 4 |
| T21 | Testes de Software (Unitários, Integração e Sistema) | Alta | ⏳ Pendente | PIM 4 |
| T22 | Documentação Técnica e Manual de Arquitetura do PIM 4 | Alta | ⏳ Pendente | PIM 4 |
| T23 | Validação Final e Apresentação do PIM 4 | Média | ⏳ Pendente | PIM 4 |

[⬆ Voltar ao sumário](#sumário)

---

## 4. Definition of Ready (DoR)

Uma tarefa/história só entra na Sprint quando atende a **todos** os critérios abaixo:

- [ ] A tarefa tem **título e descrição claros**, escritos de forma que qualquer integrante da equipe entenda.
- [ ] Está **vinculada a um ou mais requisitos** (RF/RNF) da [Matriz de Requisitos](#2-matriz-de-requisitos).
- [ ] Os **critérios de aceite** estão definidos e foram validados pelo Product Owner.
- [ ] A **prioridade** (Alta, Média ou Baixa) está definida no [Backlog](#3-backlog-unificado-pim-3--pim-4).
- [ ] As **dependências** de outras tarefas (ex.: T14 antes de T16) estão identificadas e resolvidas ou planejadas.
- [ ] A tarefa foi **estimada** pela equipe de desenvolvimento e cabe em uma única Sprint.
- [ ] Há **responsável** definido para a execução.
- [ ] Protótipos, diagramas ou modelagens necessários (UX/UI, banco de dados, casos de uso) estão **disponíveis**.
- [ ] Os impactos em **segurança, RBAC e LGPD** foram considerados quando aplicável.

[⬆ Voltar ao sumário](#sumário)

---

## 5. Definition of Done (DoD)

Uma tarefa/história só é considerada **concluída** quando atende a **todos** os critérios abaixo:

- [ ] O código foi **implementado** e atende aos critérios de aceite definidos.
- [ ] O código passou por **code review** de, no mínimo, outro integrante e foi integrado à branch principal via Pull Request.
- [ ] Os **testes** (unitários, de integração e/ou de sistema) foram escritos e estão **passando**.
- [ ] Os requisitos não funcionais aplicáveis foram verificados:
  - [ ] **Responsividade** em desktop, tablet e smartphone (RNF001).
  - [ ] **Compatibilidade** com Chrome, Edge, Firefox e Safari (RNF006).
  - [ ] **Desempenho**: requisições principais da API respondem em até 2 segundos (RNF009).
  - [ ] **Segurança e RBAC**: senhas com hash (BCrypt/Argon2), tokens JWT e rotas restritas por perfil (RNF002 e RNF003).
  - [ ] **LGPD**: aceite explícito dos termos e tratamento adequado de dados pessoais (RNF008).
- [ ] **Integridade dos dados** garantida: nenhum dado de progresso, cadastro ou nota é perdido ou corrompido (RNF007).
- [ ] Não há **bugs críticos** ou impeditivos em aberto relacionados à tarefa.
- [ ] A **documentação** foi atualizada (README, documentação técnica e/ou manual de arquitetura).
- [ ] O **Product Owner** validou a entrega e a tarefa foi marcada como ✅ Concluído no backlog.
- [ ] A funcionalidade foi **demonstrada** na Review da Sprint.

[⬆ Voltar ao sumário](#sumário)

---

## Equipe

| Nome                | RA      | SCRUM          | Git |
|---------------------|---------|----------------|-----|
| Alice Serezani      | h607jd1 | Scrum Master   | [GitHub](https://github.com/aliceserezani) |
| Fernanda Carvalheira| h191gh0 | Product Owner  | [GitHub](https://github.com/NandaCarvalheira) |
| Kauã de Moreira     | r6885b1 | Dev            | [GitHub](https://github.com/KauaMMoreiraBr) |
| Nicolas Diogo       | r860817 | Dev            | [GitHub](https://github.com/nicollas-nunes) |
| Iago Monfredine     | r8511b0 | Dev            | [GitHub](https://github.com/IagoMonfredine) |

[⬆ Voltar ao sumário](#sumário)
