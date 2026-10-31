// ============================================================
// HYTECH — Certificado de conclusão em PDF (gerado no navegador com jsPDF, sem enviar nada a servidor)
// Uso:  Certificado.baixar({ aluno, materia, dataConclusao })   |   Certificado.imprimir({...})
// Cores por matéria = as cores das medalhas (assets/materias/*.webp).
// ============================================================
(function () {
  var PALETAS = {
    'Programação':    { slug: 'programacao',    icone: 'assets/materias/programacao.webp',    estrela: 'assets/certificado/estrela-programacao.webp',    main: '#22C7E8', dark: '#0B6E8A', soft: '#E6F9FD', d1: '#0B2A4A', d2: '#0E5878' },
    'Design':         { slug: 'design',         icone: 'assets/materias/design.webp',         estrela: 'assets/certificado/estrela-design.webp',         main: '#D946EF', dark: '#86198F', soft: '#FCEBFE', d1: '#3B0764', d2: '#701A75' },
    'Banco de Dados': { slug: 'banco-de-dados', icone: 'assets/materias/banco-de-dados.webp', estrela: 'assets/certificado/estrela-banco-de-dados.webp', main: '#3B82F6', dark: '#1E3A8A', soft: '#E8F0FF', d1: '#0F1E5A', d2: '#1E40AF' },
    'Redes':          { slug: 'redes',          icone: 'assets/materias/redes.webp',          estrela: 'assets/certificado/estrela-redes.webp',          main: '#10B981', dark: '#065F46', soft: '#E5FAF2', d1: '#052E2B', d2: '#065F46' }
  };
  var PADRAO = { slug: 'geral', icone: 'assets/certificado/estrela.webp', estrela: 'assets/certificado/estrela.webp', main: '#7B61FF', dark: '#4C1D95', soft: '#F3E8FF', d1: '#2D0A6E', d2: '#4A1990' };
  function paleta(materia) { return PALETAS[materia] || PADRAO; }

  function rgb(hex) { var n = parseInt(hex.slice(1), 16); return [n >> 16 & 255, n >> 8 & 255, n & 255]; }
  // jsPDF (fontes padrão) só entende Latin-1: tira o que não couber, mantendo acentos comuns
  function latin1(s) {
    return String(s == null ? '' : s).replace(/[^\x20-\x7E\xA0-\xFF]/g, function (c) {
      return c.normalize('NFD').replace(/[^\x20-\x7E]/g, '') || '?';
    });
  }
  function slug(s) { return latin1(s).normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '') || 'aluno'; }

  var _libPromessa = null;
  function carregarLib() {
    if (window.jspdf && window.jspdf.jsPDF) return Promise.resolve(window.jspdf.jsPDF);
    if (_libPromessa) return _libPromessa;
    _libPromessa = new Promise(function (ok, erro) {
      var s = document.createElement('script');
      s.src = 'js/vendor/jspdf.umd.min.js';
      s.onload = function () { ok(window.jspdf.jsPDF); };
      s.onerror = function () { _libPromessa = null; erro(new Error('Não foi possível carregar o gerador de PDF.')); };
      document.head.appendChild(s);
    });
    return _libPromessa;
  }

  var _cacheImg = {};
  function imagemPng(src) {                    // webp/png -> PNG (com transparência) para o PDF
    if (_cacheImg[src]) return Promise.resolve(_cacheImg[src]);
    return new Promise(function (ok, erro) {
      var im = new Image();
      im.onload = function () {
        var c = document.createElement('canvas'); c.width = im.naturalWidth; c.height = im.naturalHeight;
        c.getContext('2d').drawImage(im, 0, 0);
        _cacheImg[src] = { data: c.toDataURL('image/png'), w: c.width, h: c.height };
        ok(_cacheImg[src]);
      };
      im.onerror = function () { erro(new Error('Imagem do certificado não encontrada.')); };
      im.src = src;
    });
  }

  function hexagono(doc, cx, cy, r, estilo) {  // hexágono "de ponta" (mesma silhueta das medalhas)
    var pts = [];
    for (var i = 0; i < 6; i++) { var a = Math.PI / 180 * (60 * i - 90); pts.push([cx + r * Math.cos(a), cy + r * Math.sin(a)]); }
    var rel = [];
    for (var j = 1; j < 6; j++) rel.push([pts[j][0] - pts[j - 1][0], pts[j][1] - pts[j - 1][1]]);
    doc.lines(rel, pts[0][0], pts[0][1], [1, 1], estilo, true);
  }

  function larg_(doc, txt, espaco) { return doc.getTextWidth(txt) + espaco * (txt.length - 1); }   // charSpace é em mm
  function textoEspacado(doc, txt, x, y, espaco) {    // centraliza texto com espaço entre letras
    var larg = larg_(doc, txt, espaco);
    doc.setCharSpace(espaco);
    doc.text(txt, x - larg / 2, y);
    doc.setCharSpace(0);
    return larg;
  }

  async function montar(d) {
    var JsPDF = await carregarLib();
    var P = paleta(d.materia);
    var imgs = await Promise.all([imagemPng(P.estrela), imagemPng(P.icone)]);
    var doc = new JsPDF({ orientation: 'landscape', unit: 'mm', format: 'a4' });
    var W = 297, H = 210, cx = W / 2;
    var cor = function (hex, tipo) { var c = rgb(hex); (tipo === 'fill' ? doc.setFillColor : tipo === 'draw' ? doc.setDrawColor : doc.setTextColor).call(doc, c[0], c[1], c[2]); };

    var aluno = latin1(d.aluno || 'Aluno'), materia = latin1(d.materia || '');
    var conclusao = new Date(d.dataConclusao || Date.now());
    var dataLonga = conclusao.toLocaleDateString('pt-BR', { day: '2-digit', month: 'long', year: 'numeric' });
    var agora = new Date();
    var emissao = agora.toLocaleDateString('pt-BR') + ' às ' + agora.toLocaleTimeString('pt-BR');

    // fundo + molduras
    cor(P.soft, 'fill'); doc.rect(0, 0, W, H, 'F');
    cor('#FFFFFF', 'fill'); doc.rect(9, 9, W - 18, H - 18, 'F');
    cor(P.d1, 'draw'); doc.setLineWidth(1.6); doc.rect(5, 5, W - 10, H - 10, 'S');
    cor(P.main, 'draw'); doc.setLineWidth(0.5); doc.rect(9, 9, W - 18, H - 18, 'S');
    doc.setLineWidth(0.2); doc.rect(11.5, 11.5, W - 23, H - 23, 'S');
    // cantos: hexágonos
    [[9, 9], [W - 9, 9], [9, H - 9], [W - 9, H - 9]].forEach(function (p) {
      cor(P.d1, 'fill'); cor(P.main, 'draw'); doc.setLineWidth(0.6); hexagono(doc, p[0], p[1], 6.2, 'FD');
      cor(P.main, 'fill'); hexagono(doc, p[0], p[1], 2.6, 'F');
    });

    // cabeçalho: marca e matéria
    doc.setFont('helvetica', 'bold'); doc.setFontSize(13); cor(P.d1, 'text');
    doc.setCharSpace(2.5); doc.text('HYTECH', 22, 22); doc.setCharSpace(0);
    doc.setFontSize(8.5); cor(P.dark, 'text');
    var mt = materia.toUpperCase(); doc.setCharSpace(1.5); doc.text(mt, W - 22 - larg_(doc, mt, 1.5), 22); doc.setCharSpace(0);

    // estrela + título
    var E = imgs[0], lado = 34;
    doc.addImage(E.data, 'PNG', cx - lado / 2, 14, lado, lado);
    doc.setFont('helvetica', 'bold'); doc.setFontSize(36); cor(P.d1, 'text');
    textoEspacado(doc, 'CERTIFICADO', cx, 62, 3);
    doc.setFontSize(11.5); cor(P.dark, 'text');
    var lSub = textoEspacado(doc, 'DE CONCLUSÃO', cx, 70, 3);
    cor(P.main, 'draw'); doc.setLineWidth(0.5);
    var x1 = cx - lSub / 2 - 6, x2 = cx + lSub / 2 + 6;
    doc.line(x1 - 38, 68.8, x1, 68.8); doc.line(x2, 68.8, x2 + 38, 68.8);
    cor(P.main, 'fill'); hexagono(doc, x1 - 42, 68.8, 1.8, 'F'); hexagono(doc, x2 + 42, 68.8, 1.8, 'F');

    // corpo
    doc.setFont('helvetica', 'normal'); doc.setFontSize(12.5); cor('#555566', 'text');
    doc.text('Este certificado é concedido a', cx, 86, { align: 'center' });
    var tam = 34; doc.setFont('times', 'bolditalic');
    do { doc.setFontSize(tam); tam -= 1; } while (doc.getTextWidth(aluno) > 215 && tam > 14);
    cor(P.d1, 'text'); doc.text(aluno, cx, 104, { align: 'center' });
    cor(P.main, 'draw'); doc.setLineWidth(0.6); doc.line(cx - 85, 109, cx + 85, 109);
    doc.setFont('helvetica', 'normal'); doc.setFontSize(12.5); cor('#555566', 'text');
    doc.text('por concluir com êxito o módulo', cx, 119, { align: 'center' });

    // pílula da matéria com a medalha
    doc.setFont('helvetica', 'bold'); doc.setFontSize(18);
    var largTxt = doc.getTextWidth(materia), largPil = largTxt + 38, xPil = cx - largPil / 2;
    cor(P.d1, 'fill'); cor(P.main, 'draw'); doc.setLineWidth(0.8);
    doc.roundedRect(xPil, 124, largPil, 15, 7.5, 7.5, 'FD');
    doc.addImage(imgs[1].data, 'PNG', xPil - 3, 121.5, 20, 20);
    cor('#FFFFFF', 'text'); doc.text(materia, xPil + 21, 134, {});

    doc.setFont('helvetica', 'normal'); doc.setFontSize(12.5);
    var rot = 'Concluído em ', lRot = doc.getTextWidth(rot);
    doc.setFont('helvetica', 'bold'); var lData = doc.getTextWidth(dataLonga);
    var x0 = cx - (lRot + lData) / 2;
    doc.setFont('helvetica', 'normal'); cor('#555566', 'text'); doc.text(rot, x0, 154);
    doc.setFont('helvetica', 'bold'); cor(P.d1, 'text'); doc.text(dataLonga, x0 + lRot, 154);

    // rodapé em três colunas
    var yb = 180;
    doc.setFont('helvetica', 'bold'); doc.setFontSize(7); cor('#888899', 'text');
    doc.setCharSpace(1.2);
    doc.text('STATUS', 30, yb - 7); doc.text('EMISSÃO', W - 30, yb - 7, { align: 'right' });
    doc.setCharSpace(0);
    // selo de "ok" desenhado em vetor (a fonte padrão não tem ✔)
    cor(P.main, 'fill'); doc.circle(33, yb + 0.2, 3, 'F');
    cor('#FFFFFF', 'draw'); doc.setLineWidth(0.7); doc.lines([[1.1, 1.2], [1.9, -2.2]], 31.5, yb + 0.4, [1, 1], 'S');
    doc.setFont('helvetica', 'bold'); doc.setFontSize(11); cor(P.d1, 'text'); doc.text('Concluído com Êxito', 38, yb + 1.4);
    doc.setFont('helvetica', 'normal'); doc.setFontSize(9); cor('#555566', 'text'); doc.text('Certificado emitido', 38, yb + 6);
    doc.setFont('helvetica', 'bold'); doc.setFontSize(11); cor(P.d1, 'text'); doc.text(emissao, W - 30, yb + 1.4, { align: 'right' });
    // assinatura da plataforma
    doc.setFont('times', 'italic'); doc.setFontSize(20); cor(P.dark, 'text'); doc.text('HyTech', cx, yb - 1, { align: 'center' });
    cor(P.d1, 'draw'); doc.setLineWidth(0.4); doc.line(cx - 32, yb + 1.5, cx + 32, yb + 1.5);
    doc.setFont('helvetica', 'normal'); doc.setFontSize(8); cor('#555566', 'text');
    doc.text('Plataforma: HYTECH - Sistema de Aprendizagem', cx, yb + 6, { align: 'center' });

    return { doc: doc, nome: 'certificado-hytech-' + P.slug + '-' + slug(aluno) + '.pdf' };
  }

  window.Certificado = {
    paleta: paleta,
    baixar: async function (d) { var r = await montar(d); r.doc.save(r.nome); },
    imprimir: async function (d) {
      var janela = window.open('', '_blank');            // abrir já no clique evita bloqueio de pop-up
      try {
        var r = await montar(d); r.doc.autoPrint();
        var url = r.doc.output('bloburl');
        if (janela) janela.location.href = url; else window.location.href = url;
      } catch (e) { if (janela) janela.close(); throw e; }
    }
  };
})();
