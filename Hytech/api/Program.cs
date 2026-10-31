using System.Security.Cryptography;
using HytechApi;
using HytechApi.Security;

// ── Utilitários (rodam e saem; não sobem o servidor) ──
//   dotnet run -- --hash "SuaSenhaForte"   -> imprime o hash BCrypt (para o seed do admin)
//   dotnet run -- --chave                  -> imprime uma chave aleatória para o Jwt:Key
if (args.Length == 2 && args[0] == "--hash")
{
    Console.WriteLine(new SenhaHasherBcrypt().Hash(args[1]));
    return;
}
if (args.Length == 1 && args[0] == "--chave")
{
    Console.WriteLine(Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)));
    return;
}

var app = HytechApp.Construir(args);

app.Lifetime.ApplicationStarted.Register(() =>
    app.Logger.LogInformation("HytechApi no ar ({Ambiente}){Site}", app.Environment.EnvironmentName,
        app.Services.GetRequiredService<Opcoes>().SiteDir is null ? "" : " — servindo também o site"));

app.Run();
