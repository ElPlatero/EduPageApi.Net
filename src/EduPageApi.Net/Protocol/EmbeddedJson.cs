using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EduPageApi.Protocol;

internal static partial class EmbeddedJson
{
    // Read data only. Never execute JavaScript supplied by the remote page.
    internal static JsonDocument Read(string html, string initializer)
    {
        JsonDocument? result = null;
        try
        {
            foreach (Match script in Scripts().Matches(html))
            {
                var source = script.Groups[1].Value;
                var pattern = @"\." + Regex.Escape(initializer) + @"\s*\(\s*(?=\{)";
                foreach (Match match in Regex.Matches(source, pattern, RegexOptions.CultureInvariant,
                             TimeSpan.FromSeconds(1)))
                {
                    if (result is not null)
                        throw new ProtocolException("The page contains ambiguous initialization data.");

                    var bytes = Encoding.UTF8.GetBytes(source[(match.Index + match.Length)..]);
                    var reader = new Utf8JsonReader(bytes);
                    result = JsonDocument.ParseValue(ref reader);
                    if (!Encoding.UTF8.GetString(bytes.AsSpan((int)reader.BytesConsumed)).TrimStart().StartsWith(')'))
                        throw new ProtocolException("The page initialization format is unsupported.");
                }
            }

            return result ?? throw new ProtocolException("The expected page initialization data is missing.");
        }
        catch (Exception error) when (error is JsonException or RegexMatchTimeoutException)
        {
            result?.Dispose();
            throw new ProtocolException("The page initialization data is invalid.");
        }
        catch
        {
            result?.Dispose();
            throw;
        }
    }

    [GeneratedRegex(@"<script\b[^>]*>([\s\S]*?)</script\s*>", RegexOptions.IgnoreCase, 1000)]
    private static partial Regex Scripts();
}
