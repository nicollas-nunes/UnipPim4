const { carregarConfig } = require('./src/config');
const { Db } = require('./src/db');
const { criarApp } = require('./src/app');

const cfg = carregarConfig();
const db = new Db(cfg.db);
const app = criarApp({ cfg, db });

app.listen(cfg.porta, () => console.log(`HytechApi no ar: http://localhost:${cfg.porta}`));
process.on('SIGINT', async () => { await db.fechar(); process.exit(0); });
