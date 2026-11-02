// Texto escrito por professor é ENTRADA NÃO CONFIÁVEL: nunca vira HTML bruto.
const esc = (s) => String(s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
  .replace(/"/g, '&quot;').replace(/'/g, '&#39;');

// "l1\nl1b\n\npar 2" -> "<p>l1<br>l1b</p><br>\n<p>par 2</p>"  (formato do conteúdo importado na Fase 2)
function textoParaHtml(texto) {
  return String(texto || '').replace(/\r\n/g, '\n').split(/\n{2,}/).map(s => s.trim()).filter(Boolean)
    .map(par => '<p>' + esc(par).replace(/\n/g, '<br>') + '</p>').join('<br>\n');
}

// Volta para texto puro (para o professor editar a própria aula).
function htmlParaTexto(html) {
  return String(html || '').replace(/<\/p><br>\n?<p>/g, '\n\n').replace(/<br>/g, '\n').replace(/<\/p>/g, '').replace(/<p>/g, '')
    .replace(/&lt;/g, '<').replace(/&gt;/g, '>').replace(/&quot;/g, '"').replace(/&#39;/g, "'").replace(/&amp;/g, '&')
    .trim();
}
module.exports = { esc, textoParaHtml, htmlParaTexto };
