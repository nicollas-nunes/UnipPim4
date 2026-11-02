using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace HytechApi.Security;

public record UsuarioLogin(int Id, string Nome, string Nickname, string Email, string Status, string Papel,
                           int? IdAluno, int? IdProfessor, int? IdAdmin, string Hash);

public class TokenService
{
    private readonly IConfiguration _cfg;
    public TokenService(IConfiguration cfg) => _cfg = cfg;

    public (string token, DateTime expira) Criar(UsuarioLogin u)
    {
        var jwt = _cfg.GetSection("Jwt");
        var expira = DateTime.UtcNow.AddMinutes(int.TryParse(jwt["ExpiraMinutos"], out var m) ? m : 120);

        var claims = new List<Claim>
        {
            new("sub", u.Id.ToString()),
            new("name", u.Nome),
            new("email", u.Email),
            new("role", u.Papel)                                  // aluno | professor | admin
        };
        if (u.IdAluno is int a)     claims.Add(new Claim("id_aluno", a.ToString()));
        if (u.IdProfessor is int p) claims.Add(new Claim("id_professor", p.ToString()));
        if (u.IdAdmin is int ad)    claims.Add(new Claim("id_admin", ad.ToString()));

        var chave = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt["Key"]!));
        var token = new JwtSecurityToken(
            issuer: jwt["Issuer"], audience: jwt["Audience"], claims: claims,
            expires: expira, signingCredentials: new SigningCredentials(chave, SecurityAlgorithms.HmacSha256));

        return (new JwtSecurityTokenHandler().WriteToken(token), expira);
    }
}
