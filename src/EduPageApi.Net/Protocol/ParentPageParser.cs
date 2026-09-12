using System.Text.Json;

namespace EduPageApi.Protocol;

internal sealed record ObservedChild(string Id, string FirstName, string LastName);

internal static class ParentPageParser
{
    internal static IReadOnlyList<ObservedChild> Parse(string html)
    {
        using var document = EmbeddedJson.Read(html, "userhome");
        try
        {
            var root = document.RootElement;
            var students = root.GetProperty("dbi").GetProperty("students");
            var children = new List<ObservedChild>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var id in root.GetProperty("parentStudentids").EnumerateArray())
            {
                var key = JsonFields.Id(id);
                if (!seen.Add(key))
                    throw new ProtocolException("The parent page contains duplicate child identifiers.");
                var student = students.GetProperty(key);
                if (JsonFields.Id(student.GetProperty("id")) != key)
                    throw new ProtocolException("The parent page contains inconsistent child identifiers.");
                children.Add(new(key, JsonFields.Text(student, "firstname"), JsonFields.Text(student, "lastname")));
            }
            return children.AsReadOnly();
        }
        catch (Exception error) when (error is KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new ProtocolException("The parent page has an unsupported data structure.");
        }
    }
}

internal static class JsonFields
{
    internal static string Text(JsonElement element, string name) =>
        element.GetProperty(name).GetString() ?? throw new ProtocolException("A required text field is null.");

    internal static string Id(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String when !string.IsNullOrWhiteSpace(element.GetString()) => element.GetString()!,
        JsonValueKind.Number when element.TryGetInt64(out var value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture),
        _ => throw new ProtocolException("An identifier has an unsupported format.")
    };
}
