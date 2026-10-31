using System.Globalization;

namespace HytechApi.Data;

/// <summary>Uma linha devolvida por uma procedure. Colunas ausentes ou NULL viram valor "vazio" (0, "", false, null).</summary>
public sealed class Linha
{
    private readonly Dictionary<string, object?> _v;

    public Linha(IDictionary<string, object?> valores)
        => _v = new Dictionary<string, object?>(valores, StringComparer.OrdinalIgnoreCase);

    public object? this[string coluna] => _v.TryGetValue(coluna, out var v) ? v : null;

    public bool Tem(string coluna) => _v.ContainsKey(coluna);

    /// <summary>Texto; NULL vira "".</summary>
    public string Txt(string coluna) => TxtN(coluna) ?? string.Empty;

    /// <summary>Texto; NULL continua null.</summary>
    public string? TxtN(string coluna)
        => this[coluna] switch
        {
            null => null,
            DateTime d => d.ToString("o", CultureInfo.InvariantCulture),
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            var o => o.ToString()
        };

    public int Int(string coluna) => IntN(coluna) ?? 0;

    public int? IntN(string coluna)
        => this[coluna] is { } v ? Convert.ToInt32(v, CultureInfo.InvariantCulture) : null;

    public long Long(string coluna)
        => this[coluna] is { } v ? Convert.ToInt64(v, CultureInfo.InvariantCulture) : 0L;

    /// <summary>BIT do SQL (true/false) ou número (0/1).</summary>
    public bool Bool(string coluna)
        => this[coluna] switch
        {
            null => false,
            bool b => b,
            string s => s == "1" || s.Equals("true", StringComparison.OrdinalIgnoreCase),
            var o => Convert.ToDecimal(o, CultureInfo.InvariantCulture) != 0m
        };

    public DateTime? DataN(string coluna)
        => this[coluna] switch
        {
            null => null,
            DateTime d => d,
            DateTimeOffset o => o.UtcDateTime,
            DateOnly d => d.ToDateTime(TimeOnly.MinValue),
            var o => DateTime.TryParse(o.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var p) ? p : null
        };
}
