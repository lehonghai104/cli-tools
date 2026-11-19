using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Totp;

/// <summary>RFC 6238 time-based one-time password generator.</summary>
public static class TotpGenerator
{
    public static string Generate(byte[] key, DateTimeOffset now, int period = 30, int digits = 6, string algorithm = "SHA1")
    {
        long counter = now.ToUnixTimeSeconds() / period;
        Span<byte> counterBytes = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counterBytes, counter);

        byte[] hash = algorithm.ToUpperInvariant() switch
        {
            "SHA1" => HMACSHA1.HashData(key, counterBytes),
            "SHA256" => HMACSHA256.HashData(key, counterBytes),
            "SHA512" => HMACSHA512.HashData(key, counterBytes),
            _ => throw new ArgumentException($"Unsupported algorithm '{algorithm}'. Use SHA1, SHA256 or SHA512."),
        };

        int offset = hash[^1] & 0x0F;
        int binary = ((hash[offset] & 0x7F) << 24)
                   | (hash[offset + 1] << 16)
                   | (hash[offset + 2] << 8)
                   | hash[offset + 3];

        long otp = binary % (long)Math.Pow(10, digits);
        return otp.ToString().PadLeft(digits, '0');
    }

    public static int SecondsRemaining(DateTimeOffset now, int period = 30)
        => period - (int)(now.ToUnixTimeSeconds() % period);
}
