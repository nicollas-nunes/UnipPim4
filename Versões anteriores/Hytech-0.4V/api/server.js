const { carregarConfig } = require('./src/config');
const { Db } = require('./src/db');
const { criarApp } = require('./src/app');

const cfg = carregarConfig();
const db = new Db(cfg.db);
const app = criarApp({ cfg, db });

const servidor = app.listen(cfg.porta, () => {
  console.log(`HytechApi no ar na porta ${cfg.porta} (${cfg.producao ? 'produção' : 'desenvolvimento'})` + (cfg.siteDir ? ' — servindo também o site' : ''));
});

async function encerrar(sinal) {
  console.log(`${sinal} recebido: encerrando...`);
  servidor.close(async () => { await db.fechar(); process.exit(0); });
  setTimeout(() => process.exit(1), 10000).unref();   // não fica preso se algo não fechar
}
process.on('SIGINT', () => encerrar('SIGINT'));
process.on('SIGTERM', () => encerrar('SIGTERM'));
process.on('unhandledRejection', (e) => console.error('Promessa rejeitada sem tratamento:', e));
