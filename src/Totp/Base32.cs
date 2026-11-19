namespace Totp;

/// <summary>RFC 4648 Base32 decoder (tolerates spaces, dashes, lowercase and missing padding).</summary>
public static class Base32
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static byte[] Decode(string input)
    {
        var clean = new string(input
            .Where(c => c is not (' ' or '-' or '='))
            .Select(char.ToUpperInvariant)
            .ToArray());

        if (clean.Length == 0)
            throw new FormatException("Secret is empty.");

        var output = new List<byte>(clean.Length * 5 / 8);
        int buffer = 0, bitsLeft = 0;

        foreach (char c in clean)
        {
            int val = Alphabet.IndexOf(c);
            if (val < 0)
                throw new FormatException($"Invalid Base32 character '{c}' in secret.");

            buffer = (buffer << 5) | val;
            bitsLeft += 5;
            if (bitsLeft >= 8)
            {
                bitsLeft -= 8;
                output.Add((byte)((buffer >> bitsLeft) & 0xFF));
            }
        }

        return output.ToArray();
    }

    public static string Encode(ReadOnlySpan<byte> data)
    {
        var sb = new System.Text.StringBuilder((data.Length * 8 + 4) / 5);
        int buffer = 0, bitsLeft = 0;
        foreach (byte b in data)
        {
            buffer = (buffer << 8) | b;
            bitsLeft += 8;
            while (bitsLeft >= 5)
            {
                bitsLeft -= 5;
                sb.Append(Alphabet[(buffer >> bitsLeft) & 0x1F]);
            }
        }
        if (bitsLeft > 0)
            sb.Append(Alphabet[(buffer << (5 - bitsLeft)) & 0x1F]);
        return sb.ToString(); // unpadded, as authenticator apps expect
    }

    public static bool IsValid(string input)
    {
        try { Decode(input); return true; }
        catch (FormatException) { return false; }
    }
}
