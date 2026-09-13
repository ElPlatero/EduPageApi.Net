using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace EduPageApi.Protocol;

internal static class EnvelopeCodec
{
    private static readonly UTF8Encoding Utf8 = new(false, true);

    internal static IReadOnlyDictionary<string, string> Encode(string formData)
    {
        using var output = new MemoryStream();
        using (var compressor = new DeflateStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            compressor.Write(Utf8.GetBytes(formData));
        }

        var payload = "dz:" + Convert.ToBase64String(output.ToArray());
        return new Dictionary<string, string>
        {
            ["eqap"] = payload,
            // The checksum is required by the observed wire protocol, not used for authentication.
            ["eqacs"] = Convert.ToHexStringLower(SHA1.HashData(Utf8.GetBytes(payload))),
            ["eqaz"] = "1"
        };
    }

    internal static string Decode(string response)
    {
        if (response.StartsWith("eqwd:", StringComparison.Ordinal))
            throw new ProtocolException("The server rejected the request envelope.");

        if (!response.StartsWith("eqz:", StringComparison.Ordinal))
            return response;

        try
        {
            return Utf8.GetString(Convert.FromBase64String(response[4..]));
        }
        catch (Exception error) when (error is FormatException or DecoderFallbackException)
        {
            throw new ProtocolException("The response envelope is invalid.");
        }
    }
}
