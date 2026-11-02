using HytechApi;
using HytechApi.Data;
using HytechApi.Services;
using HytechApi.Testes;

// dotnet run --project api.testes                      -> roda todos os testes
// dotnet run --project api.testes -- servidor-falso    -> sobe a API com BANCO FALSO em http://localhost:3000 (sem SQL Server)
if (args.Contains("servidor-falso")) { await ServidorFalso.Rodar(args.Where(a => a != "servidor-falso").ToArray()); return 0; }

Console.WriteLine("HytechApi — testes automáticos (banco falso, sem SQL Server)");
await TestesBasicos.Todos();
await TestesAluno.Todos();
await TestesTicketsProfessor.Todos();
await TestesSeguranca.Todos();
return T.Resumo();
