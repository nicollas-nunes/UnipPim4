// Lê as configurações do ambiente (.env) e valida o que é obrigatório.
require('dotenv').config();

function lista(v, padrao) {
  return (v || padrao).split(',').map(s => s.trim()).filter(Boolean);
}

function carregarConfig(env = process.env) {
  const jwtKey = env.JWT_KEY || '';
  if (jwtKey.length < 32) {
    throw new Error('JWT_KEY ausente ou curta demais (mínimo 32 caracteres). Defina no arquivo .env');
  }
  const producao = env.NODE_ENV === 'production';
  if (producao && /troque|exemplo|123456/i.test(jwtKey)) {
    throw new Error('JWT_KEY ainda é o valor de exemplo. Gere uma chave aleatória (npm run chave) e coloque no .env');
  }
  if (producao && (!env.DB_USER || !env.DB_PASSWORD || /troque/i.test(env.DB_PASSWORD))) {
    throw new Error('Em produção, defina DB_USER e DB_PASSWORD reais no .env (não use a senha de exemplo).');
  }
  // E-mail: com SMTP_HOST envia de verdade; sem ele, "modo console" (o código aparece no terminal).
  const smtp = !!env.SMTP_HOST && env.EMAIL_MODO !== 'console';
  if (producao && !smtp && env.EMAIL_MODO !== 'console') {
    throw new Error('Em produção, configure o e-mail (SMTP_HOST, SMTP_USER, SMTP_PASS) ou defina EMAIL_MODO=console (só para demonstração).');
  }
  if (smtp && (!env.SMTP_USER || !env.SMTP_PASS)) {
    throw new Error('SMTP_HOST foi definido, mas faltam SMTP_USER e SMTP_PASS no .env');
  }
  const portaSmtp = Number(env.SMTP_PORT) || 465;
  return {
    producao,
    codigoMinutos: Number(env.CODIGO_MINUTOS) || 10,    // validade do código enviado por e-mail
    email: {
      modo: smtp ? 'smtp' : 'console',
      host: env.SMTP_HOST, porta: portaSmtp,
      seguro: env.SMTP_SECURE ? env.SMTP_SECURE === 'true' : portaSmtp === 465,
      usuario: env.SMTP_USER, senha: env.SMTP_PASS,
      de: env.EMAIL_DE || (env.SMTP_USER ? `HyTech <${env.SMTP_USER}>` : 'HyTech <nao-responder@hytech.local>'),
    },
    trustProxy: env.TRUST_PROXY ? (/^\d+$/.test(env.TRUST_PROXY) ? Number(env.TRUST_PROXY) : env.TRUST_PROXY === 'true') : false,
    siteDir: env.SERVIR_SITE === 'true' ? (env.SITE_DIR || require('path').join(__dirname, '..', '..', 'web')) : null,
    logRequisicoes: env.LOG_REQUISICOES ? env.LOG_REQUISICOES === 'true' : producao,
    limiteGeralPorMinuto: Number(env.LIMITE_GERAL_POR_MINUTO) || 300, // requisições por IP por minuto (toda a API)
    porta: Number(env.PORT) || 3000,
    limiteConteudoPorHora: Number(env.LIMITE_CONTEUDO_POR_HORA) || 120, // criações/edições do professor por hora
    limiteTicketsPorHora: Number(env.TICKETS_LIMITE_POR_HORA) || 60, // mensagens/tickets por usuário por hora
    limiteAuthPorMinuto: Number(env.AUTH_LIMITE_POR_MINUTO) || 10, // tentativas de login/cadastro por IP
    jwt: {
      key: jwtKey,
      issuer: env.JWT_ISSUER || 'HytechApi',
      audience: env.JWT_AUDIENCE || 'HytechWeb',
      expiraMinutos: Number(env.JWT_EXPIRA_MINUTOS) || 120,
    },
    corsOrigens: lista(env.CORS_ORIGINS, 'http://localhost:5500,http://127.0.0.1:5500'),
    db: {
      server: env.DB_SERVER || 'localhost',
      port: Number(env.DB_PORT) || 1433,
      database: env.DB_NAME || 'HytechDB',
      user: env.DB_USER,
      password: env.DB_PASSWORD,
      options: {
        encrypt: env.DB_ENCRYPT === 'true',
        trustServerCertificate: env.DB_TRUST_SERVER_CERT !== 'false',
      },
      pool: { max: 10, min: 0, idleTimeoutMillis: 30000 },
    },
  };
}

module.exports = { carregarConfig };
