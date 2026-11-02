// Sobe a API com um BANCO FALSO em memória (sem SQL Server) para testar o site.
//   npm run dev:falso      -> API em http://localhost:3000
// Usuários de teste: aluno@hytech.com / professor@hytech.com, senha "senha1234"
const bcrypt = require('bcryptjs');
const { criarApp } = require('../src/app');
const { ServicoError } = require('../src/db');
const { carregarConfig } = require('../src/config');

const cfg = carregarConfig({
  JWT_KEY: 'chave-de-teste-apenas-para-o-banco-falso-123456',
  CORS_ORIGINS: process.env.CORS_ORIGINS || 'http://localhost:5500,http://127.0.0.1:5500',
  PORT: process.env.PORT || '3000',
  AUTH_LIMITE_POR_MINUTO: '1000',
});
const hash = bcrypt.hashSync('senha1234', 8);
const usuarios = [
  { id_usuario: 3, nome: 'Pedro Bala', nickname: '@PedroBala', email: 'aluno@hytech.com', status: 'ativo', papel: 'aluno', id_aluno: 1, id_professor: null, senha_hash: hash },
  { id_usuario: 2, nome: 'Prof. Silva', nickname: '@profsilva', email: 'professor@hytech.com', status: 'ativo', papel: 'professor', id_aluno: null, id_professor: 1, senha_hash: hash },
  { id_usuario: 1, nome: 'Administrador', nickname: '@admin', email: 'admin@hytech.com', status: 'ativo', papel: 'admin', senha_hash: hash },
];
let proxId = 100;

const db = {
  async exec(proc, p = {}) {
    switch (proc) {
      case 'sp_ObterUsuarioParaLogin': return usuarios.filter(u => u.email === String(p.email).trim().toLowerCase());
      case 'sp_RegistrarLogin': return [];
      case 'sp_CadastrarAluno': {
        const email = String(p.email).trim().toLowerCase();
        if (usuarios.some(u => u.email === email)) throw new ServicoError('Este e-mail já está cadastrado.');
        const nick = p.nickname.startsWith('@') ? p.nickname : '@' + p.nickname;
        if (usuarios.some(u => u.nickname === nick)) throw new ServicoError('Este nome de usuário já está em uso.');
        const id = ++proxId;
        usuarios.push({ id_usuario: id, nome: p.nome, nickname: nick, email, status: 'ativo', papel: 'aluno', id_aluno: id, id_professor: null, senha_hash: p.senha_hash });
        return [{ id_usuario: id, id_aluno: id }];
      }
      case 'sp_ObterConfiguracaoPlataforma': return [{ chaves_questao_facil: 7, chaves_questao_media: 10, chaves_questao_dificil: 15, chaves_certificado: 50 }];
      case 'sp_ListarMaterias': return ['Programação', 'Design', 'Banco de Dados', 'Redes'].map((t, i) => ({ id_materia: i + 1, titulo: t }));
      default: throw new Error('Procedure não simulada: ' + proc);
    }
  },
};
criarApp({ cfg, db }).listen(cfg.porta, () => console.log('API (banco FALSO) em http://localhost:' + cfg.porta));
