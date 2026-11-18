using System.Text.Json;
using System.Text.RegularExpressions;

namespace NfcDesk.Core.Protocol;

public readonly record struct ParsedTap(string Tag, long Ts, string TsText, string Sig);

public static class TapParser
{
    public const int MaxBodyBytes = 1024;

    private static readonly Regex TagRegex = new(@"\A[a-z0-9-]{1,64}\z", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex TsRegex = new(@"\A(0|[1-9][0-9]{0,18})\z", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static bool TryParse(ReadOnlySpan<byte> utf8Body, out ParsedTap tap)
    {
        tap = default;
        if (utf8Body.Length == 0 || utf8Body.Length > MaxBodyBytes) return false;
        try
        {
            return TryParseCore(utf8Body, out tap);
        }
        catch (JsonException) { tap = default; return false; }
        catch (InvalidOperationException) { tap = default; return false; }
        catch (ArgumentException) { tap = default; return false; }
    }

    private static bool TryParseCore(ReadOnlySpan<byte> body, out ParsedTap tap)
    {
        tap = default;
        var reader = new Utf8JsonReader(body, new JsonReaderOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 8,
        });

        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject) return false;

        string? tag = null, tsText = null, sig = null;
        long ts = 0;

        while (true)
        {
            if (!reader.Read()) return false;
            if (reader.TokenType == JsonTokenType.EndObject) break;
            if (reader.TokenType != JsonTokenType.PropertyName) return false;

            string name = reader.GetString()!;
            if (!reader.Read()) return false;

            switch (name)
            {
                case "tag":
                    if (tag is not null || reader.TokenType != JsonTokenType.String) return false;
                    tag = reader.GetString()!;
                    if (!TagRegex.IsMatch(tag)) return false;
                    break;
                case "ts":
                    if (tsText is not null) return false;
                    string raw;
                    if (reader.TokenType == JsonTokenType.Number)
                        raw = System.Text.Encoding.UTF8.GetString(reader.ValueSpan);
                    else if (reader.TokenType == JsonTokenType.String)
                        raw = reader.GetString()!;
                    else return false;
                    if (!TsRegex.IsMatch(raw) || !long.TryParse(raw, System.Globalization.NumberStyles.None,
                            System.Globalization.CultureInfo.InvariantCulture, out ts)) return false;
                    tsText = raw;
                    break;
                case "sig":
                    if (sig is not null || reader.TokenType != JsonTokenType.String) return false;
                    sig = reader.GetString()!;
                    break;
                default:
                    return false;
            }
        }

        // Anything after the closing brace other than whitespace is invalid.
        if (reader.Read()) return false;
        if (tag is null || tsText is null || sig is null) return false;

        tap = new ParsedTap(tag, ts, tsText, sig);
        return true;
    }
}
