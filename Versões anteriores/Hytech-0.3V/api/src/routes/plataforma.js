const express = require('express');

// "Banco de Dados" -> "banco-de-dados" (= assets/materias/banco-de-dados.webp)
function slug(texto) {
  return String(texto || '').normalize('NFD').replace(/[̀-ͯ]/g, '')
    .toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '');
}

module.exports = function rotasPlataforma({ db, auth }) {
  const r = express.Router();
  // Valores de gamificação definidos pelo admin (ChavesConfig). O web LÊ daqui.
  r.get('/config/chaves', auth.exigirLogin, async (_req, res) => {
    const c = (await db.exec('sp_ObterConfiguracaoPlataforma'))[0];
    if (!c) return res.status(404).json({ erro: 'Configuração não encontrada.' });
    res.json({
      questaoFacil: c.chaves_questao_facil,
      questaoMedia: c.chaves_questao_media,
      questaoDificil: c.chaves_questao_dificil,
      certificado: c.chaves_certificado,
    });
  });

  // Matérias do banco, com slug para o mapeamento de imagens do web.
  r.get('/materias', auth.exigirLogin, async (_req, res) => {
    const lista = await db.exec('sp_ListarMaterias');
    res.json(lista.map(m => ({ id: m.id_materia, titulo: m.titulo, slug: slug(m.titulo) })));
  });

  return r;
};
module.exports.slug = slug;
