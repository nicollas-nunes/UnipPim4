using System.Data;
using Microsoft.Data.SqlClient;

namespace HytechApi.Data;

/// <summary>Regra de negócio vinda do banco (RAISERROR das procedures). A API devolve 400 com a mensagem.</summary>
public class ServicoException : Exception
{
    public ServicoException(string mensagem) : base(mensagem) { }
}

/// <summary>Parâmetro de procedure. O nome é guardado sem o "@" (aceita "@x" ou "x").</summary>
public readonly record struct Param(string Nome, object? Valor);

public static class Parametros
{
    public static Param P(string nome, object? valor) => new(nome.TrimStart('@'), valor);
}

/// <summary>
/// Acesso ao banco SOMENTE por stored procedures. É uma interface para que os testes
/// usem um banco falso em memória (sem SQL Server) no lugar do <see cref="SqlDb"/>.
/// </summary>
public interface IDb
{
    /// <summary>Executa a procedure e devolve as linhas da PRIMEIRA tabela de resultado.</summary>
    Task<List<Linha>> ExecAsync(string proc, params Param[] ps);

    /// <summary>Para procedures com VÁRIOS SELECT (ex.: sp_ProgressoAluno): uma lista por tabela de resultado.</summary>
    Task<List<List<Linha>>> ExecMultiAsync(string proc, params Param[] ps);
}

public static class DbExtensoes
{
    public static async Task<List<T>> ConsultarAsync<T>(this IDb db, string proc, Func<Linha, T> mapear, params Param[] ps)
        => (await db.ExecAsync(proc, ps)).Select(mapear).ToList();

    public static Task ExecutarAsync(this IDb db, string proc, params Param[] ps) => db.ExecAsync(proc, ps);

    public static async Task<Linha?> PrimeiroAsync(this IDb db, string proc, params Param[] ps)
        => (await db.ExecAsync(proc, ps)).FirstOrDefault();

    /// <summary>Como <see cref="PrimeiroAsync"/>, mas a procedure TEM que devolver uma linha (senão é erro interno, 500).</summary>
    public static async Task<Linha> UnicoAsync(this IDb db, string proc, params Param[] ps)
        => (await db.ExecAsync(proc, ps)).FirstOrDefault()
           ?? throw new InvalidOperationException($"{proc} não devolveu nenhuma linha.");
}

/// <summary>Implementação real: SQL Server via Microsoft.Data.SqlClient.</summary>
public sealed class SqlDb : IDb
{
    private readonly string _cs;

    public SqlDb(IConfiguration config)
        => _cs = config.GetConnectionString("HytechDb")
                 ?? throw new InvalidOperationException("ConnectionStrings:HytechDb não configurada.");

    public async Task<List<Linha>> ExecAsync(string proc, params Param[] ps)
        => (await ExecMultiAsync(proc, ps)).FirstOrDefault() ?? new List<Linha>();

    public async Task<List<List<Linha>>> ExecMultiAsync(string proc, params Param[] ps)
    {
        try
        {
            await using var con = new SqlConnection(_cs);
            await con.OpenAsync();
            await using var cmd = new SqlCommand(proc, con) { CommandType = CommandType.StoredProcedure };
            foreach (var p in ps) cmd.Parameters.Add(Converter(p));

            await using var r = await cmd.ExecuteReaderAsync();
            var tudo = new List<List<Linha>>();
            do
            {
                var linhas = new List<Linha>();
                while (await r.ReadAsync()) linhas.Add(Ler(r));
                tudo.Add(linhas);
            } while (await r.NextResultAsync());
            return tudo;
        }
        // 50000 = RAISERROR(...) das procedures: mensagem escrita para o usuário ver
        catch (SqlException ex) when (ex.Number == 50000) { throw new ServicoException(ex.Message); }
    }

    private static SqlParameter Converter(Param p)
    {
        var sp = new SqlParameter("@" + p.Nome, DBNull.Value);
        switch (p.Valor)
        {
            case null: break;
            case DateOnly d:
                sp.SqlDbType = SqlDbType.Date;
                sp.Value = d.ToDateTime(TimeOnly.MinValue);
                break;
            case bool b:
                sp.SqlDbType = SqlDbType.Bit;
                sp.Value = b;
                break;
            default:
                sp.Value = p.Valor;
                break;
        }
        return sp;
    }

    private static Linha Ler(SqlDataReader r)
    {
        var d = new Dictionary<string, object?>(r.FieldCount, StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < r.FieldCount; i++)
            d[r.GetName(i)] = r.IsDBNull(i) ? null : r.GetValue(i);
        return new Linha(d);
    }
}
