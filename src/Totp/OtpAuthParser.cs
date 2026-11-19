namespace Totp;

/// <summary>An account decoded from a QR code, before it gets a unique name in the config.</summary>
public sealed record ParsedAccount(string? Issuer, string? Account, string Secret, int Digits, int Period, string Algorithm)
{
    public string DisplayName =>
        !string.IsNullOrWhiteSpace(Issuer) ? Issuer! :
        !string.IsNullOrWhiteSpace(Account) ? Account! : "Unnamed";
}

/// <summary>
/// Parses the text inside authenticator QR codes:
///  - otpauth://totp/Issuer:account?secret=...&amp;issuer=...&amp;digits=...&amp;period=...&amp;algorithm=...
///  - otpauth-migration://offline?data=...  (Google Authenticator "Transfer accounts" export)
/// </summary>
public static class OtpAuthParser
{
    public static List<ParsedAccount> Parse(string text)
    {
        text = text.Trim();
        if (text.StartsWith("otpauth-migration://", StringComparison.OrdinalIgnoreCase))
            return ParseMigration(text);
        if (text.StartsWith("otpauth://", StringComparison.OrdinalIgnoreCase))
            return [ParseOtpAuth(text)];

        throw new FormatException("QR code is not an authenticator code (expected otpauth:// or otpauth-migration://).");
    }

    private static ParsedAccount ParseOtpAuth(string text)
    {
        var uri = new Uri(text);
        if (!uri.Host.Equals("totp", StringComparison.OrdinalIgnoreCase))
            throw new FormatException($"Only TOTP is supported (QR code is '{uri.Host}').");

        var query = ParseQuery(uri.Query);

        // Label is "Issuer:account" or just "account".
        var label = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'));
        string? labelIssuer = null, account = label;
        var colon = label.IndexOf(':');
        if (colon >= 0)
        {
            labelIssuer = label[..colon].Trim();
            account = label[(colon + 1)..].Trim();
        }

        var secret = query.GetValueOrDefault("secret")
            ?? throw new FormatException("QR code has no 'secret' parameter.");
        secret = secret.Replace(" ", "").TrimEnd('=').ToUpperInvariant();
        Base32.Decode(secret); // validate

        var issuer = query.GetValueOrDefault("issuer") is { Length: > 0 } i ? i : labelIssuer;
        int digits = int.TryParse(query.GetValueOrDefault("digits"), out var d) ? d : 6;
        int period = int.TryParse(query.GetValueOrDefault("period"), out var p) ? p : 30;
        var algorithm = (query.GetValueOrDefault("algorithm") ?? "SHA1").ToUpperInvariant();

        return new ParsedAccount(issuer, string.IsNullOrWhiteSpace(account) ? null : account, secret, digits, period, algorithm);
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            var key = Uri.UnescapeDataString(eq < 0 ? part : part[..eq]);
            var value = eq < 0 ? "" : Uri.UnescapeDataString(part[(eq + 1)..]);
            result.TryAdd(key, value);
        }
        return result;
    }

    // --- Google Authenticator export (protobuf MigrationPayload) ---------------------------

    private static List<ParsedAccount> ParseMigration(string text)
    {
        var data = ParseQuery(new Uri(text).Query).GetValueOrDefault("data")
            ?? throw new FormatException("Migration QR code has no 'data' parameter.");

        // Base64 may arrive with '+' turned into ' ' by some tools.
        var payload = Convert.FromBase64String(data.Replace(' ', '+'));

        var accounts = new List<ParsedAccount>();
        var skipped = 0;
        foreach (var (field, value) in ReadFields(payload))
        {
            if (field != 1 || value is not byte[] otpParams) continue; // repeated OtpParameters otp_parameters = 1

            byte[]? secret = null;
            string? name = null, issuer = null;
            long algorithm = 1, digits = 1, type = 2;
            foreach (var (f, v) in ReadFields(otpParams))
            {
                switch (f)
                {
                    case 1: secret = (byte[])v; break;
                    case 2: name = System.Text.Encoding.UTF8.GetString((byte[])v); break;
                    case 3: issuer = System.Text.Encoding.UTF8.GetString((byte[])v); break;
                    case 4: algorithm = (long)v; break;
                    case 5: digits = (long)v; break;
                    case 6: type = (long)v; break;
                }
            }

            // type: 1 = HOTP, 2 = TOTP (0 = unspecified, treated as TOTP)
            if (secret is null || type == 1) { skipped++; continue; }

            // Name is often "Issuer:account"; strip the issuer prefix if duplicated.
            if (name is not null && !string.IsNullOrEmpty(issuer) && name.StartsWith(issuer + ":", StringComparison.Ordinal))
                name = name[(issuer.Length + 1)..].Trim();

            accounts.Add(new ParsedAccount(
                string.IsNullOrWhiteSpace(issuer) ? null : issuer,
                string.IsNullOrWhiteSpace(name) ? null : name,
                Base32.Encode(secret),
                digits == 2 ? 8 : 6,
                30,
                algorithm switch { 2 => "SHA256", 3 => "SHA512", 4 => "MD5", _ => "SHA1" }));
        }

        if (accounts.Count == 0)
            throw new FormatException(skipped > 0
                ? "Migration QR code contains only HOTP accounts, which are not supported."
                : "Migration QR code contains no accounts.");
        return accounts;
    }

    /// <summary>Minimal protobuf reader: yields (fieldNumber, long | byte[]) for varint and length-delimited fields.</summary>
    private static IEnumerable<(int Field, object Value)> ReadFields(byte[] buffer)
    {
        int pos = 0;
        while (pos < buffer.Length)
        {
            var tag = ReadVarint(buffer, ref pos);
            int field = (int)(tag >> 3), wireType = (int)(tag & 7);
            switch (wireType)
            {
                case 0:
                    yield return (field, (long)ReadVarint(buffer, ref pos));
                    break;
                case 2:
                    var len = (int)ReadVarint(buffer, ref pos);
                    if (len < 0 || pos + len > buffer.Length) throw new FormatException("Corrupt migration data.");
                    yield return (field, buffer[pos..(pos + len)]);
                    pos += len;
                    break;
                case 1: pos += 8; break;
                case 5: pos += 4; break;
                default: throw new FormatException("Corrupt migration data.");
            }
        }
    }

    private static ulong ReadVarint(byte[] buffer, ref int pos)
    {
        ulong result = 0;
        for (int shift = 0; shift < 64; shift += 7)
        {
            if (pos >= buffer.Length) throw new FormatException("Corrupt migration data.");
            byte b = buffer[pos++];
            result |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) return result;
        }
        throw new FormatException("Corrupt migration data.");
    }
}
