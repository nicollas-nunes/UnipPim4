namespace HytechApi.Security;

public interface ISenhaHasher
{
    string Hash(string senha);
    bool Verificar(string senha, string hashArmazenado);
    /// <summary>Hash descartável: usado quando o e-mail não existe, para o login levar o mesmo tempo (evita descobrir e-mails pelo tempo de resposta).</summary>
    string HashFalso { get; }
}

/// <summary>BCrypt, o mesmo algoritmo do HytechAdmin (Usuario.senha_hash). Os dois PRECISAM usar o mesmo.</summary>
public class SenhaHasherBcrypt : ISenhaHasher
{
    private readonly int _custo;

    public SenhaHasherBcrypt(int custo = 11)
    {
        _custo = custo;
        HashFalso = BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString(), custo);
    }

    public string HashFalso { get; }

    public string Hash(string senha) => BCrypt.Net.BCrypt.HashPassword(senha, _custo);

    public bool Verificar(string senha, string hashArmazenado)
    {
        if (string.IsNullOrEmpty(hashArmazenado)) return false;
        try { return BCrypt.Net.BCrypt.Verify(senha, hashArmazenado); }
        catch (Exception) { return false; }
    }
}
