using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using HytechApi.Data;
using Microsoft.IdentityModel.Tokens;

namespace HytechApi.Security;

public record UsuarioLogin(int Id, string Nome, string Nickname, string Email, string Status, string Papel,
                           int? IdAluno, int? IdProfessor, int? IdAdmin, string Hash, string? Foto)
{
    /// <summary>Monta a partir da linha de sp_ObterUsuarioParaLogin.</summary>
    public static UsuarioLogin De(Linha r) => new(
        r.Int("id_usuario"), r.Txt("nome"), r.Txt("nickname"), r.Txt("email"),
        r.Txt("status"), r.Txt("papel"),
        r.IntN("id_aluno"), r.IntN("id_professor"), r.IntN("id_admin"),
        r.Txt("senha_hash"), r.TxtN("foto_url"));
}

/// <summary>Quem está logado, lido do TOKEN. As rotas usam isto — nunca ids vindos do corpo da requisição.</summary>
public record UsuarioToken(int Id, string Nome, string Nickname, string Email, string Papel, int? IdAluno, int? IdProfessor)
{
    public static UsuarioToken De(ClaimsPrincipal u)
    {
        int? Num(string tipo) => int.TryParse(u.FindFirstValue(tipo), out var n) ? n : null;
        return new UsuarioToken(
            Num("sub") ?? 0, u.FindFirstValue("name") ?? "", u.FindFirstValue("nickname") ?? "",
            u.FindFirstValue("email") ?? "", u.FindFirstValue("role") ?? "",
            Num("id_aluno"), Num("id_professor"));
    }
}

public class TokenService
{
    private readonly Opcoes _opc;
    public TokenService(Opcoes opc) => _opc = opc;

    public (string token, DateTime expira) Criar(UsuarioLogin u)
    {
        var expira = DateTime.UtcNow.AddMinutes(_opc.Jwt.ExpiraMinutos);

        var claims = new List<Claim>
        {
            new("sub", u.Id.ToString()),
            new("name", u.Nome),
            new("nickname", u.Nickname),
            new("email", u.Email),
            new("role", u.Papel)                                  // aluno | professor | admin
        };
        if (u.IdAluno is int a)     claims.Add(new Claim("id_aluno", a.ToString()));
        if (u.IdProfessor is int p) claims.Add(new Claim("id_professor", p.ToString()));
        if (u.IdAdmin is int ad)    claims.Add(new Claim("id_admin", ad.ToString()));

        var chave = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_opc.Jwt.Key));
        var token = new JwtSecurityToken(
            issuer: _opc.Jwt.Issuer, audience: _opc.Jwt.Audience, claims: claims,
            expires: expira, signingCredentials: new SigningCredentials(chave, SecurityAlgorithms.HmacSha256));

        return (new JwtSecurityTokenHandler().WriteToken(token), expira);
    }
}
