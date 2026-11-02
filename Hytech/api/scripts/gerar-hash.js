// Uso: npm run hash -- "SuaSenhaForte"   -> imprime o hash BCrypt (para o seed do admin)
const bcrypt = require('bcryptjs');
const senha = process.argv[2];
if (!senha) { console.error('Uso: npm run hash -- "SuaSenhaForte"'); process.exit(1); }
console.log(bcrypt.hashSync(senha, 11));
