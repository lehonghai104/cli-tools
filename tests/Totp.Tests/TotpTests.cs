using System.Text;
using Totp;

namespace Totp.Tests;

public class TotpTests
{
    // RFC 6238 Appendix B test vectors (8 digits, 30s period).
    [Theory]
    [InlineData(59, "SHA1", "94287082")]
    [InlineData(59, "SHA256", "46119246")]
    [InlineData(59, "SHA512", "90693936")]
    [InlineData(1111111109, "SHA1", "07081804")]
    [InlineData(1111111109, "SHA256", "68084774")]
    [InlineData(1111111109, "SHA512", "25091201")]
    [InlineData(1234567890, "SHA1", "89005924")]
    [InlineData(1234567890, "SHA256", "91819424")]
    [InlineData(1234567890, "SHA512", "93441116")]
    [InlineData(2000000000, "SHA1", "69279037")]
    [InlineData(20000000000, "SHA256", "77737706")]
    [InlineData(20000000000, "SHA512", "47863826")]
    public void Rfc6238Vectors(long unixTime, string alg, string expected)
    {
        var seed = alg switch
        {
            "SHA1" => "12345678901234567890",
            "SHA256" => "12345678901234567890123456789012",
            _ => "1234567890123456789012345678901234567890123456789012345678901234",
        };
        var code = TotpGenerator.Generate(Encoding.ASCII.GetBytes(seed),
            DateTimeOffset.FromUnixTimeSeconds(unixTime), 30, 8, alg);
        Assert.Equal(expected, code);
    }

    [Fact]
    public void Base32DecodesWithSpacesAndLowercase()
    {
        Assert.Equal(Encoding.ASCII.GetBytes("12345678901234567890"),
            Base32.Decode("gezd gnbv gy3t qojq GEZDGNBVGY3TQOJQ"));
    }

    [Fact]
    public void Base32RejectsInvalidChars() =>
        Assert.Throws<FormatException>(() => Base32.Decode("ABC1"));

    [Fact]
    public void FindMatchesCaseInsensitiveAndUniquePrefix()
    {
        var cfg = new TotpConfig { Apps = [new() { Name = "GitHub" }, new() { Name = "Gitlab" }, new() { Name = "AWS" }] };
        Assert.Equal("GitHub", cfg.Find("github")?.Name);
        Assert.Equal("AWS", cfg.Find("aw")?.Name);
        Assert.Null(cfg.Find("git")); // ambiguous
    }
}
