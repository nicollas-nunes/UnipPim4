// Acesso ao SQL Server SOMENTE por stored procedures (mesmo padrão do HytechAdmin e da API antiga em C#).
const sql = require('mssql');

/** Regra de negócio vinda do banco (RAISERROR nas procedures). A API devolve 400 com a mensagem. */
class ServicoError extends Error {
  constructor(mensagem) { super(mensagem); this.name = 'ServicoError'; }
}

class Db {
  constructor(configDb) {
    this._config = configDb;
    this._pool = null;
  }

  async _obterPool() {
    if (!this._pool) this._pool = await new sql.ConnectionPool(this._config).connect();
    return this._pool;
  }

  /** Executa a procedure e devolve o resultado bruto (todas as tabelas de resultado). */
  async _executar(proc, params, tipos) {
    try {
      const pool = await this._obterPool();
      const req = pool.request();
      for (const [nome, valor] of Object.entries(params)) {
        const v = valor === undefined ? null : valor;
        if (tipos[nome]) req.input(nome, tipos[nome], v);
        else req.input(nome, v);
      }
      return await req.execute(proc);
    } catch (e) {
      // 50000 = RAISERROR(...) das procedures
      if (e && e.number === 50000) throw new ServicoError(e.message);
      throw e;
    }
  }

  /**
   * Executa uma procedure e devolve as linhas da PRIMEIRA tabela de resultado.
   * @param {string} proc    nome da procedure, ex.: 'sp_ListarMaterias'
   * @param {object} params  { nomeSemArroba: valor }
   * @param {object} tipos   (opcional) { nomeSemArroba: sql.Date } quando o tipo não puder ser inferido
   */
  async exec(proc, params = {}, tipos = {}) {
    const r = await this._executar(proc, params, tipos);
    return r.recordset || [];
  }

  /** Para procedures com VÁRIOS SELECT (ex.: sp_ProgressoAluno): devolve um array de arrays. */
  async execMulti(proc, params = {}, tipos = {}) {
    const r = await this._executar(proc, params, tipos);
    return r.recordsets || [];
  }

  async fechar() { if (this._pool) { await this._pool.close(); this._pool = null; } }
}

module.exports = { Db, ServicoError, sql };
