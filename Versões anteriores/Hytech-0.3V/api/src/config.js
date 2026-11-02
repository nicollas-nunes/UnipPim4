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
  return {
    porta: Number(env.PORT) || 3000,
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
