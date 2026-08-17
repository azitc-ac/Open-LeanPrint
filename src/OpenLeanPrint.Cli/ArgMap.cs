using System.Globalization;

namespace OpenLeanPrint.Cli;

/// <summary>Minimal parser for "--key value" options and "--flag" switches.</summary>
internal sealed class ArgMap
{
    private readonly Dictionary<string, string?> _map = new(StringComparer.OrdinalIgnoreCase);

    public static ArgMap Parse(string[] args)
    {
        var m = new ArgMap();
        for (int i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal)) continue;
            string key = args[i][2..];
            if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                m._map[key] = args[i + 1];
                i++;
            }
            else
            {
                m._map[key] = null; // flag
            }
        }
        return m;
    }

    public bool Has(string key) => _map.ContainsKey(key);

    public string Get(string key, string fallback) =>
        _map.TryGetValue(key, out var v) && v is not null ? v : fallback;

    public double GetDouble(string key, double fallback) =>
        _map.TryGetValue(key, out var v) && v is not null &&
        double.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out var d)
            ? d : fallback;
}
