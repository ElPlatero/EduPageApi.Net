using System.Globalization;
using System.Text.Json;

namespace EduPageApi.Protocol;

// Internal observations: do not freeze these protocol fields into the public fluent contract.
internal sealed record ObservedGrade(
    string Id, string StudentId, string Subject, string Assessment,
    string Value, DateTime RecordedAt, string AssessmentType, decimal AssessmentParameter);

internal static class GradesPageParser
{
    internal static IReadOnlyList<ObservedGrade> Parse(string html, string expectedStudentId)
    {
        using var document = EmbeddedJson.Read(html, "znamkyStudentViewer");
        try
        {
            var root = document.RootElement;
            if (JsonFields.Id(root.GetProperty("studentid")) != expectedStudentId)
                throw new ProtocolException("The grades page belongs to a different child.");

            var result = new List<ObservedGrade>();
            foreach (var grade in root.GetProperty("vsetkyZnamky").EnumerateArray())
            {
                if (JsonFields.Id(grade.GetProperty("studentid")) != expectedStudentId)
                    throw new ProtocolException("A grade belongs to a different child.");
                var provider = JsonFields.Text(grade, "provider");
                var assessment = root.GetProperty("vsetkyUdalosti").GetProperty(provider)
                    .GetProperty(JsonFields.Id(grade.GetProperty("udalostid")));
                var subject = root.GetProperty("predmety")
                    .GetProperty(JsonFields.Id(grade.GetProperty("predmetid")));
                var parameter = assessment.GetProperty("p_vaha");
                var weight = parameter.ValueKind == JsonValueKind.Number
                    ? parameter.GetDecimal()
                    : decimal.Parse(parameter.GetString()!, CultureInfo.InvariantCulture);
                result.Add(new(
                    JsonFields.Id(grade.GetProperty("znamkaid")), expectedStudentId,
                    JsonFields.Text(subject, "p_meno"), JsonFields.Text(assessment, "p_meno"),
                    JsonFields.Text(grade, "data"),
                    DateTime.SpecifyKind(DateTime.ParseExact(JsonFields.Text(grade, "datum"),
                        "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture), DateTimeKind.Unspecified),
                    JsonFields.Id(assessment.GetProperty("p_typ_udalosti")), weight));
            }
            return result.AsReadOnly();
        }
        catch (Exception error) when (error is KeyNotFoundException or InvalidOperationException or FormatException or OverflowException)
        {
            throw new ProtocolException("The grades page has an unsupported data structure.");
        }
    }
}
