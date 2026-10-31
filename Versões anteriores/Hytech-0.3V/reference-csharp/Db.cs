using System.Data;
using Microsoft.Data.SqlClient;

namespace HytechApi.Data;

/// <summary>Regra de negócio vinda do banco (RAISERROR das procedures). A API devolve 400 com a mensagem.</summary>
public class ServicoException : Exception
{
    public ServicoException(string mensagem) : base(mensagem) { }
}

/// <summary>Acesso ao SQL Server somente via stored procedures (mesmo padrão do HytechAdmin).</summary>
public sealed class Db
{
    private readonly string _cs;

    public Db(IConfiguration config)
        => _cs = config.GetConnectionString("HytechDb")
                 ?? throw new InvalidOperationException("ConnectionStrings:HytechDb não configurada.");

    public static SqlParameter P(string nome, object? valor) => new(nome, valor ?? DBNull.Value);

    public async Task<List<T>> ConsultarAsync<T>(string proc, Func<SqlDataReader, T> mapear, params SqlParameter[] ps)
    {
        try
        {
            await using var con = new SqlConnection(_cs);
            await con.OpenAsync();
            await using var cmd = Criar(con, proc, ps);
            await using var r = await cmd.ExecuteReaderAsync();
            var lista = new List<T>();
            while (await r.ReadAsync()) lista.Add(mapear(r));
            return lista;
        }
        catch (SqlException ex) when (ex.Number == 50000) { throw new ServicoException(ex.Message); }
    }

    public async Task ExecutarAsync(string proc, params SqlParameter[] ps)
    {
        try
        {
            await using var con = new SqlConnection(_cs);
            await con.OpenAsync();
            await using var cmd = Criar(con, proc, ps);
            await cmd.ExecuteNonQueryAsync();
        }
        catch (SqlException ex) when (ex.Number == 50000) { throw new ServicoException(ex.Message); }
    }

    private static SqlCommand Criar(SqlConnection con, string proc, SqlParameter[] ps)
    {
        var cmd = new SqlCommand(proc, con) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddRange(ps);
        return cmd;
    }
}

public static class LeitorExtensoes
{
    public static string Txt(this SqlDataReader r, string col)
    {
        var i = r.GetOrdinal(col);
        return r.IsDBNull(i) ? string.Empty : Convert.ToString(r.GetValue(i)) ?? string.Empty;
    }

    public static int Int(this SqlDataReader r, string col)
    {
        var i = r.GetOrdinal(col);
        return r.IsDBNull(i) ? 0 : Convert.ToInt32(r.GetValue(i));
    }

    public static int? IntN(this SqlDataReader r, string col)
    {
        var i = r.GetOrdinal(col);
        return r.IsDBNull(i) ? null : Convert.ToInt32(r.GetValue(i));
    }
}
