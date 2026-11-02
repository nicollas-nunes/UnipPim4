// ============================================================
// HYTECH — Cliente da API (front → back-end Node)
// Todo acesso a dados do site passa por aqui. O token fica só em memória.
// Para mudar o endereço da API: defina window.HYTECH_API_URL antes de carregar este arquivo.
// ============================================================
(function () {
  var BASE = (window.HYTECH_API_URL || 'http://localhost:3000').replace(/\/$/, '');
  var _token = null;

  async function chamar(metodo, rota, corpo) {
    var headers = { 'Content-Type': 'application/json' };
    if (_token) headers.Authorization = 'Bearer ' + _token;
    var resp;
    try {
      resp = await fetch(BASE + rota, {
        method: metodo, headers: headers,
        body: corpo === undefined ? undefined : JSON.stringify(corpo)
      });
    } catch (e) {
      var err = new Error('Não foi possível conectar ao servidor. Tente novamente em instantes.');
      err.rede = true;
      throw err;
    }
    var dados = null;
    try { dados = await resp.json(); } catch (e) { /* resposta sem corpo */ }
    if (!resp.ok) {
      var e2 = new Error((dados && dados.erro) || 'Erro inesperado (' + resp.status + ').');
      e2.status = resp.status;
      if (resp.status === 401 && _token) { _token = null; document.dispatchEvent(new CustomEvent('hytech:sessao-expirada')); }
      throw e2;
    }
    return dados;
  }

  window.Api = {
    base: BASE,
    temToken: function () { return !!_token; },
    sair: function () { _token = null; },

    // ── Autenticação ──
    login: async function (email, senha) {
      var d = await chamar('POST', '/auth/login', { email: email, senha: senha });
      _token = d.token;
      return d.usuario;
    },
    cadastro: function (dados) { return chamar('POST', '/auth/cadastro', dados); },
    me: function () { return chamar('GET', '/auth/me'); },

    // ── Plataforma ──
    config: function () { return chamar('GET', '/config/chaves'); },
    materias: function () { return chamar('GET', '/materias'); },

    // ── Genérico (para as próximas fases) ──
    get: function (rota) { return chamar('GET', rota); },
    post: function (rota, corpo) { return chamar('POST', rota, corpo || {}); },
    put: function (rota, corpo) { return chamar('PUT', rota, corpo || {}); },
    del: function (rota) { return chamar('DELETE', rota); }
  };
})();
