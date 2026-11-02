// Gera uma chave aleatória forte para o JWT_KEY do .env:  npm run chave
console.log(require('crypto').randomBytes(48).toString('base64url'));
