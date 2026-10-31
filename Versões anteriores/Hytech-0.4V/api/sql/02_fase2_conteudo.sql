-- ============================================================
-- HYTECH — FASE 2: CONTEÚDO DAS MATÉRIAS (aulas, questões, alternativas)
--
-- Rodar DEPOIS do HytechDB.sql. É idempotente (pode rodar de novo sem duplicar).
-- Faz 3 coisas:
--   1) Cria as procedures que a API web usa para LER o conteúdo (nunca devolvem o gabarito).
--   2) Importa o conteúdo que estava fixo no index.html (4 matérias, 16 tópicos, 12 questões).
--   3) Define o custo para desbloquear cada matéria (0 / 19 / 38 / 57 chaves), só se ainda for 0.
--
-- Observações:
--   - Todo o conteúdo entra com status 'aprovado' (já era o que o aluno via).
--   - Questao.id_professor é obrigatório no banco, então o script cria a conta de sistema
--     "Equipe HyTech" (status 'inativo', sem login possível) como autora das questões.
--   - Todas as questões importadas são 'Fácil' (5 chaves pelo ChavesConfig) para manter o
--     comportamento atual do site. O admin/professor pode mudar a dificuldade depois.
-- ============================================================
USE HytechDB;
GO

-- ------------------------------------------------------------
-- 1. PROCEDURES DE LEITURA PARA A WEB (aluno)
-- ------------------------------------------------------------
CREATE OR ALTER PROCEDURE sp_WebListarMaterias
AS
BEGIN
    SET NOCOUNT ON;
    SELECT id_materia, titulo, ordem, chaves_para_desbloquear
    FROM Materia
    WHERE status_aprovacao = N'aprovado'
    ORDER BY ordem, id_materia;
END
GO

CREATE OR ALTER PROCEDURE sp_WebListarAulasMateria
    @id_materia INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT id_aula, topico, titulo, conteudo, exemplo_codigo, dica_titulo, dica_corpo,
           ordem, is_extra, chaves_recompensa
    FROM Aula
    WHERE id_materia = @id_materia AND status_aprovacao = N'aprovado'
    ORDER BY is_extra, ISNULL(ordem, 9999), id_aula;
END
GO

CREATE OR ALTER PROCEDURE sp_WebListarQuestoesMateria
    @id_materia INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT id_questao, enunciado, codigo_exemplo, dificuldade, chaves_recompensa
    FROM Questao
    WHERE id_materia = @id_materia AND status_aprovacao = N'aprovado'
    ORDER BY id_questao;
END
GO

-- NÃO devolve a coluna 'correta': quem confere a resposta é o banco (sp_ResponderQuestao).
CREATE OR ALTER PROCEDURE sp_WebListarAlternativasMateria
    @id_materia INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT A.id_alternativa, A.id_questao, A.texto
    FROM Alternativa A
    INNER JOIN Questao Q ON Q.id_questao = A.id_questao
    WHERE Q.id_materia = @id_materia AND Q.status_aprovacao = N'aprovado'
    ORDER BY A.id_questao, A.id_alternativa;
END
GO

-- ------------------------------------------------------------
-- 2. CONTA DE SISTEMA (autora das questões importadas; não faz login)
-- ------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM Usuario WHERE email = N'conteudo@hytech.local')
BEGIN
    INSERT INTO Usuario (nome, nickname, email, senha_hash, status)
    VALUES (N'Equipe HyTech', N'@equipehytech', N'conteudo@hytech.local', N'!conta-de-sistema-sem-login', N'inativo');
    INSERT INTO Professor (id_usuario, especialidade, criador_conteudo)
    VALUES (SCOPE_IDENTITY(), NULL, 1);
END
GO

-- ------------------------------------------------------------
-- 3. CONTEÚDO (uma matéria por bloco)
-- ------------------------------------------------------------

-- ===== 1. Programação =====
DECLARE @mat INT = (SELECT TOP 1 id_materia FROM Materia WHERE titulo = N'Programação');
DECLARE @q INT;
DECLARE @prof INT = (SELECT P.id_professor FROM Professor P INNER JOIN Usuario U ON U.id_usuario = P.id_usuario WHERE U.email = N'conteudo@hytech.local');
IF @mat IS NULL RAISERROR(N'Matéria Programação não encontrada. Rode o HytechDB.sql antes.', 16, 1);
ELSE
BEGIN
    UPDATE Materia SET chaves_para_desbloquear = 0 WHERE id_materia = @mat AND chaves_para_desbloquear = 0;
    IF NOT EXISTS (SELECT 1 FROM Aula WHERE id_materia = @mat AND titulo = N'Variáveis em Python' AND ISNULL(topico, N'') = N'Tópico 1')
        INSERT INTO Aula (id_materia, id_professor, topico, titulo, conteudo, ordem, is_extra, chaves_recompensa, exemplo_codigo, dica_titulo, dica_corpo, status_aprovacao)
        VALUES (@mat, NULL, N'Tópico 1', N'Variáveis em Python', N'<p>Aprender programação começa com entender como armazenar e organizar informações. Variáveis são como caixas onde você guarda dados — um número, um texto, qualquer valor que o programa precisará usar depois.</p><br>
          <p>Em Python, criar uma variável é simples: você escolhe um nome e atribui um valor. A linguagem decide sozinha qual tipo de dado está sendo guardado (tipagem dinâmica).</p><br>
          <p>Classificar essas informações corretamente é o que permite criar programas organizados e eficientes.</p>', 1, 0, 3, N'nome = "HYTECH"
idade = 1
ativo = True', N'Nomeie bem suas variáveis!', N'Use nomes descritivos em vez de letras genéricas como "x". Um nome como "quantidadeAlunos" é muito mais fácil de entender do que "qa".', N'aprovado');
    IF NOT EXISTS (SELECT 1 FROM Aula WHERE id_materia = @mat AND titulo = N'Tipos de Dados' AND ISNULL(topico, N'') = N'Tópico 2')
        INSERT INTO Aula (id_materia, id_professor, topico, titulo, conteudo, ordem, is_extra, chaves_recompensa, exemplo_codigo, dica_titulo, dica_corpo, status_aprovacao)
        VALUES (@mat, NULL, N'Tópico 2', N'Tipos de Dados', N'<p>Python possui vários tipos de dados nativos: <strong>int</strong> (números inteiros), <strong>float</strong> (decimais), <strong>str</strong> (texto), <strong>bool</strong> (verdadeiro/falso) e <strong>list</strong> (listas).</p><br>
          <p>Saber qual tipo usar em cada situação é fundamental para escrever código eficiente e evitar erros inesperados no programa.</p><br>
          <p>Python identifica o tipo automaticamente, mas você pode verificar usando a função <code>type()</code>.</p>', 2, 0, 3, N'numero = 42           # int
preco = 9.99          # float
nome = "Python"       # str
ativo = True          # bool
notas = [8, 9, 7.5]  # list', N'Use type() para inspecionar!', N'Quando tiver dúvida sobre o tipo de uma variável, use print(type(variavel)) para ver exatamente o que Python está guardando.', N'aprovado');
    IF NOT EXISTS (SELECT 1 FROM Aula WHERE id_materia = @mat AND titulo = N'Operadores' AND ISNULL(topico, N'') = N'Tópico 3')
        INSERT INTO Aula (id_materia, id_professor, topico, titulo, conteudo, ordem, is_extra, chaves_recompensa, exemplo_codigo, dica_titulo, dica_corpo, status_aprovacao)
        VALUES (@mat, NULL, N'Tópico 3', N'Operadores', N'<p>Operadores permitem realizar cálculos e comparações com variáveis. Os aritméticos são: <strong>+</strong> adição, <strong>-</strong> subtração, <strong>*</strong> multiplicação, <strong>/</strong> divisão e <strong>**</strong> potência.</p><br>
          <p>Os operadores de comparação (<strong>==</strong>, <strong>!=</strong>, <strong>&gt;</strong>, <strong>&lt;</strong>) retornam True ou False e são fundamentais nas estruturas de decisão.</p>', 3, 0, 3, N'a = 10
b = 3
print(a + b)   # 13
print(a ** b)  # 1000
print(a > b)   # True', N'Cuidado com / e //!', N'Em Python 3, a divisão "/" sempre retorna float. Use "//" para divisão inteira. Ex: 10/3 = 3.333 e 10//3 = 3.', N'aprovado');
    IF NOT EXISTS (SELECT 1 FROM Aula WHERE id_materia = @mat AND titulo = N'Input e Output' AND ISNULL(topico, N'') = N'Tópico Extra')
        INSERT INTO Aula (id_materia, id_professor, topico, titulo, conteudo, ordem, is_extra, chaves_recompensa, exemplo_codigo, dica_titulo, dica_corpo, status_aprovacao)
        VALUES (@mat, NULL, N'Tópico Extra', N'Input e Output', N'<p>Todo programa precisa se comunicar com o usuário. Em Python, usamos <strong>input()</strong> para ler dados digitados e <strong>print()</strong> para exibir resultados.</p><br>
          <p>O <code>input()</code> sempre retorna uma string, então se precisar de número, converta com <code>int()</code> ou <code>float()</code>.</p>', 4, 1, 5, N'nome = input("Qual é o seu nome? ")
idade = int(input("Qual a sua idade? "))
print(f"Olá, {nome}! Você tem {idade} anos.")', N'f-strings são poderosas!', N'Use f"texto {variavel}" para incluir variáveis diretamente no texto. É mais legível do que concatenar strings com "+".', N'aprovado');
    IF NOT EXISTS (SELECT 1 FROM Questao WHERE id_materia = @mat AND enunciado = N'O que é uma variável em programação?')
    BEGIN
        INSERT INTO Questao (id_materia, id_professor, enunciado, dificuldade, status_aprovacao, codigo_exemplo)
        VALUES (@mat, @prof, N'O que é uma variável em programação?', N'Fácil', N'aprovado', N'# Exemplo:
nome = "Maria"
idade = 25
print(nome, idade)');
        SET @q = SCOPE_IDENTITY();
        INSERT INTO Alternativa (id_questao, texto, correta) VALUES
            (@q, N'A) Um tipo de operador matemático', 0),
            (@q, N'B) Um espaço na memória para armazenar um valor', 1),
            (@q, N'C) Uma função que exibe texto na tela', 0),
            (@q, N'D) Um comentário no código', 0),
            (@q, N'E) Um arquivo de configuração do Python', 0);
    END
    IF NOT EXISTS (SELECT 1 FROM Questao WHERE id_materia = @mat AND enunciado = N'Qual é o tipo de dado do valor 3.14 em Python?')
    BEGIN
        INSERT INTO Questao (id_materia, id_professor, enunciado, dificuldade, status_aprovacao, codigo_exemplo)
        VALUES (@mat, @prof, N'Qual é o tipo de dado do valor 3.14 em Python?', N'Fácil', N'aprovado', N'x = 3.14
print(type(x))');
        SET @q = SCOPE_IDENTITY();
        INSERT INTO Alternativa (id_questao, texto, correta) VALUES
            (@q, N'A) int', 0),
            (@q, N'B) str', 0),
            (@q, N'C) float', 1),
            (@q, N'D) bool', 0),
            (@q, N'E) number', 0);
    END
    IF NOT EXISTS (SELECT 1 FROM Questao WHERE id_materia = @mat AND enunciado = N'Qual operador é usado para potência em Python?')
    BEGIN
        INSERT INTO Questao (id_materia, id_professor, enunciado, dificuldade, status_aprovacao, codigo_exemplo)
        VALUES (@mat, @prof, N'Qual operador é usado para potência em Python?', N'Fácil', N'aprovado', N'resultado = 2 ** 10
print(resultado)  # ?');
        SET @q = SCOPE_IDENTITY();
        INSERT INTO Alternativa (id_questao, texto, correta) VALUES
            (@q, N'A) ^', 0),
            (@q, N'B) ^^', 0),
            (@q, N'C) pow()', 0),
            (@q, N'D) **', 1),
            (@q, N'E) *^', 0);
    END
END
GO

-- ===== 2. Design =====
DECLARE @mat INT = (SELECT TOP 1 id_materia FROM Materia WHERE titulo = N'Design');
DECLARE @q INT;
DECLARE @prof INT = (SELECT P.id_professor FROM Professor P INNER JOIN Usuario U ON U.id_usuario = P.id_usuario WHERE U.email = N'conteudo@hytech.local');
IF @mat IS NULL RAISERROR(N'Matéria Design não encontrada. Rode o HytechDB.sql antes.', 16, 1);
ELSE
BEGIN
    UPDATE Materia SET chaves_para_desbloquear = 19 WHERE id_materia = @mat AND chaves_para_desbloquear = 0;
    IF NOT EXISTS (SELECT 1 FROM Aula WHERE id_materia = @mat AND titulo = N'Fundamentos do Design' AND ISNULL(topico, N'') = N'Tópico 1')
        INSERT INTO Aula (id_materia, id_professor, topico, titulo, conteudo, ordem, is_extra, chaves_recompensa, exemplo_codigo, dica_titulo, dica_corpo, status_aprovacao)
        VALUES (@mat, NULL, N'Tópico 1', N'Fundamentos do Design', N'<p>Design é a arte de resolver problemas de forma visual e funcional. Os princípios fundamentais do design gráfico e digital guiam a criação de peças que comunicam com clareza e impacto.</p><br>
          <p>Os quatro pilares do design são: <strong>Contraste</strong> (destacar elementos importantes), <strong>Repetição</strong> (criar consistência visual), <strong>Alinhamento</strong> (organizar o layout) e <strong>Proximidade</strong> (agrupar itens relacionados). Esses princípios formam o acrônimo <strong>CRAP</strong>.</p><br>
          <p>Aplicar esses princípios é o primeiro passo para criar layouts profissionais e harmoniosos.</p>', 1, 0, 3, N'PRINCÍPIOS DO DESIGN (CRAP)

C — Contraste: elementos diferentes devem
    ser claramente distintos
R — Repetição: repita estilos para coesão
A — Alinhamento: nada deve ser posto
    aleatoriamente na página
P — Proximidade: itens relacionados devem
    estar agrupados visualmente', N'Menos é mais!', N'Bons designers sabem quando parar. Evite sobrecarregar o layout com elementos em excesso. O espaço em branco (whitespace) também é um elemento do design.', N'aprovado');
    IF NOT EXISTS (SELECT 1 FROM Aula WHERE id_materia = @mat AND titulo = N'Teoria das Cores' AND ISNULL(topico, N'') = N'Tópico 2')
        INSERT INTO Aula (id_materia, id_professor, topico, titulo, conteudo, ordem, is_extra, chaves_recompensa, exemplo_codigo, dica_titulo, dica_corpo, status_aprovacao)
        VALUES (@mat, NULL, N'Tópico 2', N'Teoria das Cores', N'<p>A cor é uma das ferramentas mais poderosas do design. Ela transmite emoções, cria hierarquia e orienta o olhar do usuário. Entender o círculo cromático é essencial para criar combinações harmoniosas.</p><br>
          <p>Principais harmonias cromáticas: <strong>Monocromática</strong> (variações de um tom), <strong>Complementar</strong> (cores opostas no círculo), <strong>Análoga</strong> (cores vizinhas) e <strong>Triádica</strong> (três cores equidistantes).</p><br>
          <p>Cada cor carrega significados culturais: azul transmite confiança, vermelho urgência, verde natureza e equilíbrio.</p>', 2, 0, 3, N'HARMONIAS CROMÁTICAS

Monocromática:  #2D0A6E → #5B2D8E → #9B6FD0
                (mesmo matiz, luminosidades diferentes)

Complementar:   Azul  ←→  Laranja
                Verde ←→  Vermelho

Análoga:        Azul + Azul-Verde + Verde
                (cores próximas no círculo)', N'Regra 60-30-10!', N'Use a cor dominante em 60% do layout, a secundária em 30% e a cor de destaque (accent) em apenas 10%. Essa proporção cria equilíbrio visual natural.', N'aprovado');
    IF NOT EXISTS (SELECT 1 FROM Aula WHERE id_materia = @mat AND titulo = N'Tipografia' AND ISNULL(topico, N'') = N'Tópico 3')
        INSERT INTO Aula (id_materia, id_professor, topico, titulo, conteudo, ordem, is_extra, chaves_recompensa, exemplo_codigo, dica_titulo, dica_corpo, status_aprovacao)
        VALUES (@mat, NULL, N'Tópico 3', N'Tipografia', N'<p>Tipografia é o estudo e uso das fontes para comunicação visual. A escolha certa da fonte pode transmitir personalidade, credibilidade e hierarquia de informação.</p><br>
          <p>As famílias tipográficas principais são: <strong>Serif</strong> (com serifa — clássicas, como Times New Roman), <strong>Sans-serif</strong> (sem serifa — modernas, como Arial), <strong>Display</strong> (decorativas, para títulos) e <strong>Monospace</strong> (código, como Courier).</p><br>
          <p>A hierarquia tipográfica organiza a leitura: títulos maiores, subtítulos médios e corpo de texto menor.</p>', 3, 0, 3, N'HIERARQUIA TIPOGRÁFICA

Título (H1):    36–48px  |  Font-weight: 800
Subtítulo (H2): 24–32px  |  Font-weight: 700
Título (H3):    18–22px  |  Font-weight: 600
Corpo do texto: 14–16px  |  Font-weight: 400
Legenda:        11–13px  |  Font-weight: 400

Dica: máximo 2-3 famílias tipográficas por projeto', N'Legibilidade acima de tudo!', N'Uma fonte bonita que não se lê bem é um design ruim. Priorize sempre a legibilidade, especialmente em textos longos. Contraste entre fundo e texto deve ser alto.', N'aprovado');
    IF NOT EXISTS (SELECT 1 FROM Aula WHERE id_materia = @mat AND titulo = N'UI/UX e Prototipação' AND ISNULL(topico, N'') = N'Tópico Extra')
        INSERT INTO Aula (id_materia, id_professor, topico, titulo, conteudo, ordem, is_extra, chaves_recompensa, exemplo_codigo, dica_titulo, dica_corpo, status_aprovacao)
        VALUES (@mat, NULL, N'Tópico Extra', N'UI/UX e Prototipação', N'<p><strong>UI (User Interface)</strong> refere-se à aparência visual de um produto digital — botões, ícones, cores, tipografia. <strong>UX (User Experience)</strong> foca na experiência do usuário ao interagir com o produto — fluxos, usabilidade e acessibilidade.</p><br>
          <p>A prototipação é o processo de criar representações visuais do produto antes do desenvolvimento. Ferramentas como <strong>Figma</strong>, Adobe XD e Sketch permitem criar wireframes e protótipos interativos.</p><br>
          <p>O processo de design segue etapas: pesquisa → wireframe → protótipo → teste → refinamento.</p>', 4, 1, 5, N'FLUXO DE DESIGN UI/UX

1. Pesquisa de usuário (entrevistas, personas)
2. Arquitetura da informação (sitemap)
3. Wireframe (esboço estrutural, sem cores)
4. Protótipo de alta fidelidade (Figma)
5. Teste de usabilidade (com usuários reais)
6. Handoff para desenvolvimento
7. Iteração com base no feedback', N'Teste com usuários reais!', N'Não presuma que o seu design é intuitivo. Faça testes de usabilidade com pelo menos 5 usuários reais. Problemas que parecem óbvios ao designer são invisíveis a quem usa pela primeira vez.', N'aprovado');
    IF NOT EXISTS (SELECT 1 FROM Questao WHERE id_materia = @mat AND enunciado = N'Qual é o acrônimo que representa os quatro princípios fundamentais do design?')
    BEGIN
        INSERT INTO Questao (id_materia, id_professor, enunciado, dificuldade, status_aprovacao, codigo_exemplo)
        VALUES (@mat, @prof, N'Qual é o acrônimo que representa os quatro princípios fundamentais do design?', N'Fácil', N'aprovado', N'Contraste, Repetição, Alinhamento, Proximidade

Esses princípios guiam a organização visual
de qualquer layout profissional.');
        SET @q = SCOPE_IDENTITY();
        INSERT INTO Alternativa (id_questao, texto, correta) VALUES
            (@q, N'A) DRAW', 0),
            (@q, N'B) CRAP', 1),
            (@q, N'C) GRID', 0),
            (@q, N'D) FLOW', 0),
            (@q, N'E) FORM', 0);
    END
    IF NOT EXISTS (SELECT 1 FROM Questao WHERE id_materia = @mat AND enunciado = N'Qual harmonia cromática utiliza cores opostas no círculo cromático?')
    BEGIN
        INSERT INTO Questao (id_materia, id_professor, enunciado, dificuldade, status_aprovacao, codigo_exemplo)
        VALUES (@mat, @prof, N'Qual harmonia cromática utiliza cores opostas no círculo cromático?', N'Fácil', N'aprovado', N'Exemplos de pares:
- Azul  ←→  Laranja
- Verde ←→  Vermelho
- Roxo  ←→  Amarelo');
        SET @q = SCOPE_IDENTITY();
        INSERT INTO Alternativa (id_questao, texto, correta) VALUES
            (@q, N'A) Harmonia monocromática', 0),
            (@q, N'B) Harmonia análoga', 0),
            (@q, N'C) Harmonia complementar', 1),
            (@q, N'D) Harmonia triádica', 0),
            (@q, N'E) Harmonia quadrada', 0);
    END
    IF NOT EXISTS (SELECT 1 FROM Questao WHERE id_materia = @mat AND enunciado = N'Na hierarquia tipográfica, qual é a finalidade de usar tamanhos e pesos diferentes para títulos e corpo de texto?')
    BEGIN
        INSERT INTO Questao (id_materia, id_professor, enunciado, dificuldade, status_aprovacao, codigo_exemplo)
        VALUES (@mat, @prof, N'Na hierarquia tipográfica, qual é a finalidade de usar tamanhos e pesos diferentes para títulos e corpo de texto?', N'Fácil', N'aprovado', N'Título (H1):   48px — peso 800
Subtítulo (H2): 28px — peso 700
Corpo:         16px — peso 400');
        SET @q = SCOPE_IDENTITY();
        INSERT INTO Alternativa (id_questao, texto, correta) VALUES
            (@q, N'A) Apenas para fins estéticos, sem impacto funcional', 0),
            (@q, N'B) Orientar o olhar do leitor e organizar a leitura por importância', 1),
            (@q, N'C) Economizar espaço no layout', 0),
            (@q, N'D) Garantir compatibilidade com todos os navegadores', 0),
            (@q, N'E) Definir a paleta de cores do projeto', 0);
    END
END
GO

-- ===== 3. Banco de Dados =====
DECLARE @mat INT = (SELECT TOP 1 id_materia FROM Materia WHERE titulo = N'Banco de Dados');
DECLARE @q INT;
DECLARE @prof INT = (SELECT P.id_professor FROM Professor P INNER JOIN Usuario U ON U.id_usuario = P.id_usuario WHERE U.email = N'conteudo@hytech.local');
IF @mat IS NULL RAISERROR(N'Matéria Banco de Dados não encontrada. Rode o HytechDB.sql antes.', 16, 1);
ELSE
BEGIN
    UPDATE Materia SET chaves_para_desbloquear = 38 WHERE id_materia = @mat AND chaves_para_desbloquear = 0;
    IF NOT EXISTS (SELECT 1 FROM Aula WHERE id_materia = @mat AND titulo = N'Introdução a Banco de Dados' AND ISNULL(topico, N'') = N'Tópico 1')
        INSERT INTO Aula (id_materia, id_professor, topico, titulo, conteudo, ordem, is_extra, chaves_recompensa, exemplo_codigo, dica_titulo, dica_corpo, status_aprovacao)
        VALUES (@mat, NULL, N'Tópico 1', N'Introdução a Banco de Dados', N'<p>Um banco de dados é um sistema organizado para armazenar, gerenciar e recuperar informações de forma eficiente. Existem dois grandes modelos: <strong>Relacionais (SQL)</strong> e <strong>Não-relacionais (NoSQL)</strong>.</p><br>
          <p>Os bancos de dados relacionais organizam dados em <strong>tabelas</strong> compostas por linhas (registros) e colunas (atributos). Cada tabela representa uma entidade do mundo real, como "Alunos", "Produtos" ou "Pedidos".</p><br>
          <p>SGBDs (Sistemas Gerenciadores de Banco de Dados) populares: <strong>MySQL</strong>, <strong>PostgreSQL</strong>, <strong>SQLite</strong> e <strong>SQL Server</strong>.</p>', 1, 0, 3, N'TABELA: Alunos
┌────┬──────────┬───────┬────────┐
│ id │  nome    │ idade │ turma  │
├────┼──────────┼───────┼────────┤
│  1 │ Maria    │  16   │  A     │
│  2 │ João     │  17   │  B     │
│  3 │ Ana      │  16   │  A     │
└────┴──────────┴───────┴────────┘

Cada linha = 1 registro
Cada coluna = 1 atributo', N'Chave Primária (PK)!', N'A chave primária identifica cada registro de forma única. A coluna "id" geralmente é a PK — nunca se repete e nunca pode ser nula.', N'aprovado');
    IF NOT EXISTS (SELECT 1 FROM Aula WHERE id_materia = @mat AND titulo = N'Comandos SQL Básicos' AND ISNULL(topico, N'') = N'Tópico 2')
        INSERT INTO Aula (id_materia, id_professor, topico, titulo, conteudo, ordem, is_extra, chaves_recompensa, exemplo_codigo, dica_titulo, dica_corpo, status_aprovacao)
        VALUES (@mat, NULL, N'Tópico 2', N'Comandos SQL Básicos', N'<p>SQL (Structured Query Language) é a linguagem padrão para interagir com bancos de dados relacionais. Os comandos principais são divididos em categorias:</p><br>
          <p><strong>DML (Manipulação de Dados):</strong> <code>SELECT</code> (consultar), <code>INSERT</code> (inserir), <code>UPDATE</code> (atualizar) e <code>DELETE</code> (excluir).</p><br>
          <p><strong>DDL (Definição de Dados):</strong> <code>CREATE TABLE</code> (criar tabela), <code>ALTER TABLE</code> (modificar) e <code>DROP TABLE</code> (excluir tabela).</p>', 2, 0, 3, N'-- Consultar todos os alunos
SELECT * FROM Alunos;

-- Inserir novo aluno
INSERT INTO Alunos (nome, idade, turma)
VALUES (''Pedro'', 17, ''C'');

-- Atualizar um registro
UPDATE Alunos SET turma = ''B''
WHERE id = 3;

-- Excluir um registro
DELETE FROM Alunos WHERE id = 3;', N'Cuidado com DELETE sem WHERE!', N'Um DELETE sem cláusula WHERE apaga TODOS os registros da tabela. Sempre filtre com WHERE antes de executar operações de exclusão ou atualização.', N'aprovado');
    IF NOT EXISTS (SELECT 1 FROM Aula WHERE id_materia = @mat AND titulo = N'Filtros e Junções (JOIN)' AND ISNULL(topico, N'') = N'Tópico 3')
        INSERT INTO Aula (id_materia, id_professor, topico, titulo, conteudo, ordem, is_extra, chaves_recompensa, exemplo_codigo, dica_titulo, dica_corpo, status_aprovacao)
        VALUES (@mat, NULL, N'Tópico 3', N'Filtros e Junções (JOIN)', N'<p>A cláusula <strong>WHERE</strong> filtra registros com condições. Pode combinar condições com <strong>AND</strong>, <strong>OR</strong> e <strong>NOT</strong>. A cláusula <strong>ORDER BY</strong> ordena resultados.</p><br>
          <p>O <strong>JOIN</strong> permite combinar dados de duas ou mais tabelas relacionadas. O tipo mais comum é o <strong>INNER JOIN</strong>, que retorna apenas registros com correspondência nas duas tabelas.</p>', 3, 0, 3, N'-- Filtrar com WHERE
SELECT nome, idade FROM Alunos
WHERE turma = ''A'' AND idade >= 16
ORDER BY nome ASC;

-- JOIN: combinar Alunos com Notas
SELECT A.nome, N.disciplina, N.valor
FROM Alunos A
INNER JOIN Notas N ON A.id = N.aluno_id
WHERE N.valor >= 7;', N'INNER vs LEFT JOIN!', N'INNER JOIN retorna apenas correspondências. LEFT JOIN retorna todos os registros da tabela da esquerda, mesmo sem correspondência na da direita (preenchendo com NULL).', N'aprovado');
    IF NOT EXISTS (SELECT 1 FROM Aula WHERE id_materia = @mat AND titulo = N'Modelagem e Normalização' AND ISNULL(topico, N'') = N'Tópico Extra')
        INSERT INTO Aula (id_materia, id_professor, topico, titulo, conteudo, ordem, is_extra, chaves_recompensa, exemplo_codigo, dica_titulo, dica_corpo, status_aprovacao)
        VALUES (@mat, NULL, N'Tópico Extra', N'Modelagem e Normalização', N'<p>Modelagem de dados é o processo de projetar a estrutura do banco antes de criá-lo. O <strong>Diagrama ER (Entidade-Relacionamento)</strong> representa entidades, seus atributos e os relacionamentos entre elas.</p><br>
          <p><strong>Normalização</strong> é o processo de organizar as tabelas para eliminar redundâncias e anomalias. As três primeiras formas normais (1FN, 2FN, 3FN) são suficientes para a maioria dos projetos.</p><br>
          <p>A <strong>Chave Estrangeira (FK)</strong> é o mecanismo que cria relacionamentos entre tabelas, referenciando a chave primária de outra tabela.</p>', 4, 1, 5, N'-- Criando tabelas relacionadas
CREATE TABLE Turmas (
  id    INT PRIMARY KEY,
  nome  VARCHAR(10) NOT NULL
);

CREATE TABLE Alunos (
  id       INT PRIMARY KEY,
  nome     VARCHAR(100) NOT NULL,
  turma_id INT,
  FOREIGN KEY (turma_id) REFERENCES Turmas(id)
);', N'1FN, 2FN, 3FN simplificado!', N'1FN: sem grupos repetidos. 2FN: todo campo depende da PK inteira. 3FN: nenhum campo depende de outro campo não-chave. Seguindo essas regras, seu banco ficará íntegro.', N'aprovado');
    IF NOT EXISTS (SELECT 1 FROM Questao WHERE id_materia = @mat AND enunciado = N'Em um banco de dados relacional, o que é a Chave Primária (Primary Key)?')
    BEGIN
        INSERT INTO Questao (id_materia, id_professor, enunciado, dificuldade, status_aprovacao, codigo_exemplo)
        VALUES (@mat, @prof, N'Em um banco de dados relacional, o que é a Chave Primária (Primary Key)?', N'Fácil', N'aprovado', N'TABELA: Produtos
┌────┬─────────────┬────────┐
│ id │    nome     │ preco  │
├────┼─────────────┼────────┤
│  1 │ Notebook    │ 2500   │
│  2 │ Monitor     │  900   │
└────┴─────────────┴────────┘

A coluna "id" é a Chave Primária');
        SET @q = SCOPE_IDENTITY();
        INSERT INTO Alternativa (id_questao, texto, correta) VALUES
            (@q, N'A) Um campo que pode se repetir em vários registros', 0),
            (@q, N'B) Um identificador único que nunca se repete na tabela', 1),
            (@q, N'C) A primeira coluna obrigatória de toda tabela', 0),
            (@q, N'D) Um campo numérico que armazena senhas criptografadas', 0),
            (@q, N'E) O nome dado à tabela principal do banco', 0);
    END
    IF NOT EXISTS (SELECT 1 FROM Questao WHERE id_materia = @mat AND enunciado = N'Qual comando SQL é utilizado para buscar dados em uma tabela?')
    BEGIN
        INSERT INTO Questao (id_materia, id_professor, enunciado, dificuldade, status_aprovacao, codigo_exemplo)
        VALUES (@mat, @prof, N'Qual comando SQL é utilizado para buscar dados em uma tabela?', N'Fácil', N'aprovado', N'-- Qual comando preenche a lacuna?
_________ nome, idade FROM Alunos
WHERE turma = ''A'';');
        SET @q = SCOPE_IDENTITY();
        INSERT INTO Alternativa (id_questao, texto, correta) VALUES
            (@q, N'A) GET', 0),
            (@q, N'B) FETCH', 0),
            (@q, N'C) FIND', 0),
            (@q, N'D) SELECT', 1),
            (@q, N'E) READ', 0);
    END
    IF NOT EXISTS (SELECT 1 FROM Questao WHERE id_materia = @mat AND enunciado = N'O que o comando INNER JOIN faz em uma consulta SQL?')
    BEGIN
        INSERT INTO Questao (id_materia, id_professor, enunciado, dificuldade, status_aprovacao, codigo_exemplo)
        VALUES (@mat, @prof, N'O que o comando INNER JOIN faz em uma consulta SQL?', N'Fácil', N'aprovado', N'SELECT A.nome, N.valor
FROM Alunos A
INNER JOIN Notas N
ON A.id = N.aluno_id;');
        SET @q = SCOPE_IDENTITY();
        INSERT INTO Alternativa (id_questao, texto, correta) VALUES
            (@q, N'A) Cria uma nova tabela combinando as duas existentes', 0),
            (@q, N'B) Exclui registros duplicados de uma tabela', 0),
            (@q, N'C) Retorna registros que têm correspondência nas duas tabelas', 1),
            (@q, N'D) Ordena os resultados em ordem alfabética', 0),
            (@q, N'E) Insere dados de uma tabela em outra', 0);
    END
END
GO

-- ===== 4. Redes =====
DECLARE @mat INT = (SELECT TOP 1 id_materia FROM Materia WHERE titulo = N'Redes');
DECLARE @q INT;
DECLARE @prof INT = (SELECT P.id_professor FROM Professor P INNER JOIN Usuario U ON U.id_usuario = P.id_usuario WHERE U.email = N'conteudo@hytech.local');
IF @mat IS NULL RAISERROR(N'Matéria Redes não encontrada. Rode o HytechDB.sql antes.', 16, 1);
ELSE
BEGIN
    UPDATE Materia SET chaves_para_desbloquear = 57 WHERE id_materia = @mat AND chaves_para_desbloquear = 0;
    IF NOT EXISTS (SELECT 1 FROM Aula WHERE id_materia = @mat AND titulo = N'Fundamentos de Redes' AND ISNULL(topico, N'') = N'Tópico 1')
        INSERT INTO Aula (id_materia, id_professor, topico, titulo, conteudo, ordem, is_extra, chaves_recompensa, exemplo_codigo, dica_titulo, dica_corpo, status_aprovacao)
        VALUES (@mat, NULL, N'Tópico 1', N'Fundamentos de Redes', N'<p>Uma rede de computadores é um conjunto de dispositivos interconectados que compartilham recursos e informações. As redes podem ser classificadas por abrangência: <strong>LAN</strong> (Local Area Network — rede local), <strong>MAN</strong> (Metropolitan Area Network) e <strong>WAN</strong> (Wide Area Network — como a Internet).</p><br>
          <p>Os principais componentes de uma rede incluem: <strong>roteadores</strong> (direcionam pacotes entre redes), <strong>switches</strong> (conectam dispositivos numa mesma rede), <strong>modems</strong> (convertem sinais) e <strong>access points</strong> (Wi-Fi).</p><br>
          <p>A comunicação em rede segue modelos de referência: o <strong>modelo OSI</strong> (7 camadas) e o <strong>modelo TCP/IP</strong> (4 camadas).</p>', 1, 0, 3, N'CLASSIFICAÇÃO POR ABRANGÊNCIA

LAN  — Local Area Network
       Ex: rede da escola, empresa
       Alcance: até alguns km

MAN  — Metropolitan Area Network
       Ex: rede de uma cidade
       Alcance: dezenas de km

WAN  — Wide Area Network
       Ex: Internet
       Alcance: global', N'LAN vs WLAN!', N'LAN usa cabos (ethernet), enquanto WLAN (Wireless LAN) usa ondas de rádio (Wi-Fi). Ambas são redes locais — a diferença está no meio de transmissão.', N'aprovado');
    IF NOT EXISTS (SELECT 1 FROM Aula WHERE id_materia = @mat AND titulo = N'Modelo OSI e TCP/IP' AND ISNULL(topico, N'') = N'Tópico 2')
        INSERT INTO Aula (id_materia, id_professor, topico, titulo, conteudo, ordem, is_extra, chaves_recompensa, exemplo_codigo, dica_titulo, dica_corpo, status_aprovacao)
        VALUES (@mat, NULL, N'Tópico 2', N'Modelo OSI e TCP/IP', N'<p>O <strong>Modelo OSI</strong> divide a comunicação em rede em 7 camadas: Física, Enlace, Rede, Transporte, Sessão, Apresentação e Aplicação. É um modelo conceitual de referência.</p><br>
          <p>O <strong>modelo TCP/IP</strong> é o modelo prático usado na Internet, com 4 camadas: Acesso à Rede, Internet, Transporte e Aplicação. Protocolos como <strong>IP</strong>, <strong>TCP</strong>, <strong>UDP</strong>, <strong>HTTP</strong> e <strong>DNS</strong> operam nesse modelo.</p><br>
          <p><strong>TCP</strong> (orientado a conexão, confiável) vs <strong>UDP</strong> (sem conexão, mais rápido — usado em streaming e jogos).</p>', 2, 0, 3, N'MODELO TCP/IP — 4 CAMADAS

4. Aplicação   → HTTP, HTTPS, FTP, DNS, SMTP
               → O que o usuário vê

3. Transporte  → TCP, UDP
               → Entrega dos dados

2. Internet    → IP, ICMP
               → Endereçamento e roteamento

1. Acesso      → Ethernet, Wi-Fi
   à Rede      → Transmissão física', N'TCP vs UDP: quando usar?', N'Use TCP quando a integridade dos dados é crítica (e-mail, transferência de arquivos, web). Use UDP quando a velocidade importa mais que a perfeição (videochamadas, jogos online, DNS).', N'aprovado');
    IF NOT EXISTS (SELECT 1 FROM Aula WHERE id_materia = @mat AND titulo = N'Endereçamento IP e Sub-redes' AND ISNULL(topico, N'') = N'Tópico 3')
        INSERT INTO Aula (id_materia, id_professor, topico, titulo, conteudo, ordem, is_extra, chaves_recompensa, exemplo_codigo, dica_titulo, dica_corpo, status_aprovacao)
        VALUES (@mat, NULL, N'Tópico 3', N'Endereçamento IP e Sub-redes', N'<p>O endereço <strong>IP (Internet Protocol)</strong> identifica cada dispositivo em uma rede. O <strong>IPv4</strong> usa 32 bits representados em 4 octetos decimais (ex: 192.168.1.10). O <strong>IPv6</strong> usa 128 bits para suportar muito mais dispositivos.</p><br>
          <p>A <strong>máscara de sub-rede</strong> (subnet mask) define qual parte do IP é a rede e qual é o host. Ex: 255.255.255.0 (ou /24) indica que os 3 primeiros octetos identificam a rede.</p><br>
          <p>O <strong>DHCP</strong> atribui IPs automaticamente. O <strong>DNS</strong> converte nomes de domínio em endereços IP.</p>', 3, 0, 3, N'ENDEREÇAMENTO IPv4

IP do dispositivo:  192.168.1 . 50
Máscara (/24):      255.255.255.0

Parte de REDE:      192.168.1  (fixo)
Parte de HOST:                  50  (varia)

Range de hosts:    192.168.1.1  a  .254
Gateway padrão:    192.168.1.1  (roteador)
Broadcast:         192.168.1.255', N'IPs privados vs públicos!', N'IPs privados (192.168.x.x, 10.x.x.x, 172.16-31.x.x) são usados em redes internas e não roteados na Internet. IPs públicos são únicos globalmente e atribuídos pelo provedor (ISP).', N'aprovado');
    IF NOT EXISTS (SELECT 1 FROM Aula WHERE id_materia = @mat AND titulo = N'Segurança em Redes' AND ISNULL(topico, N'') = N'Tópico Extra')
        INSERT INTO Aula (id_materia, id_professor, topico, titulo, conteudo, ordem, is_extra, chaves_recompensa, exemplo_codigo, dica_titulo, dica_corpo, status_aprovacao)
        VALUES (@mat, NULL, N'Tópico Extra', N'Segurança em Redes', N'<p>Segurança em redes envolve proteger dados e sistemas contra acessos não autorizados, ataques e falhas. Os principais conceitos são: <strong>confidencialidade</strong>, <strong>integridade</strong> e <strong>disponibilidade</strong> (tríade CIA).</p><br>
          <p>Ferramentas e técnicas fundamentais: <strong>Firewall</strong> (filtra tráfego por regras), <strong>VPN</strong> (criptografa conexões remotas), <strong>HTTPS/TLS</strong> (criptografia na camada de aplicação) e <strong>IDS/IPS</strong> (detecção e prevenção de intrusões).</p><br>
          <p>Ataques comuns: <strong>DDoS</strong> (sobrecarga de servidores), <strong>Man-in-the-Middle</strong> (interceptação), <strong>Phishing</strong> (engenharia social) e <strong>Port Scanning</strong> (varredura de portas).</p>', 4, 1, 5, N'TRÍADE CIA DE SEGURANÇA

C — Confidencialidade
    Apenas pessoas autorizadas acessam
    os dados. Ex: criptografia, senhas

I — Integridade
    Dados não são alterados sem autorização
    Ex: hashing, assinatura digital

A — Disponibilidade
    Sistemas ficam acessíveis quando
    necessário. Ex: redundância, backups', N'HTTPS é obrigatório!', N'Nunca trafegue dados sensíveis em HTTP puro. O HTTPS usa TLS para criptografar a comunicação. Um cadeado verde no navegador indica conexão segura.', N'aprovado');
    IF NOT EXISTS (SELECT 1 FROM Questao WHERE id_materia = @mat AND enunciado = N'O que diferencia uma rede LAN de uma rede WAN?')
    BEGIN
        INSERT INTO Questao (id_materia, id_professor, enunciado, dificuldade, status_aprovacao, codigo_exemplo)
        VALUES (@mat, @prof, N'O que diferencia uma rede LAN de uma rede WAN?', N'Fácil', N'aprovado', N'LAN — Local Area Network
WAN — Wide Area Network

Exemplos:
LAN: rede da escola ou empresa
WAN: a própria Internet');
        SET @q = SCOPE_IDENTITY();
        INSERT INTO Alternativa (id_questao, texto, correta) VALUES
            (@q, N'A) LAN usa apenas cabo e WAN usa apenas Wi-Fi', 0),
            (@q, N'B) LAN tem abrangência local enquanto WAN cobre grandes distâncias', 1),
            (@q, N'C) LAN é mais rápida que WAN em qualquer situação', 0),
            (@q, N'D) WAN é uma rede privada e LAN é pública', 0),
            (@q, N'E) Não há diferença, são apenas nomes distintos para a mesma coisa', 0);
    END
    IF NOT EXISTS (SELECT 1 FROM Questao WHERE id_materia = @mat AND enunciado = N'Qual é a função do protocolo DNS em uma rede de computadores?')
    BEGIN
        INSERT INTO Questao (id_materia, id_professor, enunciado, dificuldade, status_aprovacao, codigo_exemplo)
        VALUES (@mat, @prof, N'Qual é a função do protocolo DNS em uma rede de computadores?', N'Fácil', N'aprovado', N'Você digita no navegador:
  www.hytech.com.br

O DNS resolve para:
  192.0.2.45');
        SET @q = SCOPE_IDENTITY();
        INSERT INTO Alternativa (id_questao, texto, correta) VALUES
            (@q, N'A) Criptografar o tráfego entre cliente e servidor', 0),
            (@q, N'B) Atribuir endereços IP automaticamente aos dispositivos', 0),
            (@q, N'C) Converter nomes de domínio em endereços IP', 1),
            (@q, N'D) Filtrar pacotes de rede por regras de segurança', 0),
            (@q, N'E) Gerenciar a largura de banda da rede local', 0);
    END
    IF NOT EXISTS (SELECT 1 FROM Questao WHERE id_materia = @mat AND enunciado = N'Em relação aos protocolos TCP e UDP, qual afirmação está correta?')
    BEGIN
        INSERT INTO Questao (id_materia, id_professor, enunciado, dificuldade, status_aprovacao, codigo_exemplo)
        VALUES (@mat, @prof, N'Em relação aos protocolos TCP e UDP, qual afirmação está correta?', N'Fácil', N'aprovado', N'TCP → confiável, orientado à conexão
      Ex: HTTP, e-mail, FTP

UDP → rápido, sem garantia de entrega
      Ex: streaming, jogos online');
        SET @q = SCOPE_IDENTITY();
        INSERT INTO Alternativa (id_questao, texto, correta) VALUES
            (@q, N'A) UDP é mais confiável pois confirma entrega de todos os pacotes', 0),
            (@q, N'B) TCP é usado em streaming de vídeo por ser mais rápido', 0),
            (@q, N'C) TCP garante entrega e ordem dos dados; UDP prioriza velocidade', 1),
            (@q, N'D) Ambos operam na camada de Rede do modelo TCP/IP', 0),
            (@q, N'E) UDP e TCP são o mesmo protocolo com nomes diferentes', 0);
    END
END
GO

PRINT N'Fase 2: conteúdo importado.';
GO