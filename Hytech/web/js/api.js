// ============================================================
// HYTECH — Cliente da API (front → back-end Node)
// Todo acesso a dados do site passa por aqui. O token fica só em memória.
// Para mudar o endereço da API: defina window.HYTECH_API_URL antes de carregar este arquivo.
// ============================================================
(function () {
  // Endereço da API: 1) window.HYTECH_API_URL, se definido; 2) em desenvolvimento (arquivo ou localhost em outra porta
  // que não a 3000) usa http://localhost:3000; 3) em produção usa a mesma origem do site.
  function _base() {
    if (window.HYTECH_API_URL) return window.HYTECH_API_URL;
    var l = window.location;
    var dev = l.protocol === 'file:' || ((l.hostname === 'localhost' || l.hostname === '127.0.0.1') && l.port !== '3000' && l.port !== '');
    return dev ? 'http://localhost:3000' : '';
  }
  var BASE = _base().replace(/\/$/, '');
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
    setToken: function (t) { _token = t; },      // o servidor devolve token novo quando nome/e-mail mudam

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
