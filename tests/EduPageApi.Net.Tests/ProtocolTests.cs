using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using EduPageApi.Protocol;

namespace EduPageApi.Net.Tests;

public sealed class ProtocolTests
{
    [Fact]
    public void EncodesUtf8FormAsRawDeflateWithChecksum()
    {
        const string form = "rpcparams=%7B%22username%22%3A%22parent%40example.invalid%22%7D&test=ä+漢字";
        var envelope = EnvelopeCodec.Encode(form);
        Assert.Equal("1", envelope["eqaz"]);
        Assert.StartsWith("dz:", envelope["eqap"]);
        using var stream = new MemoryStream(Convert.FromBase64String(envelope["eqap"][3..]));
        using var deflate = new DeflateStream(stream, CompressionMode.Decompress);
        using var reader = new StreamReader(deflate, Encoding.UTF8);
        Assert.Equal(form, reader.ReadToEnd());
        Assert.Equal(Convert.ToHexStringLower(SHA1.HashData(Encoding.UTF8.GetBytes(envelope["eqap"]))), envelope["eqacs"]);
    }

    [Theory]
    [InlineData("eqz:eyJzdGF0dXMiOiJPSyJ9", "{\"status\":\"OK\"}")]
    [InlineData("eqz:w6Q=", "ä")]
    [InlineData("{\"status\":\"OK\"}", "{\"status\":\"OK\"}")]
    public void DecodesObservedResponseFormats(string input, string expected) =>
        Assert.Equal(expected, EnvelopeCodec.Decode(input));

    [Theory]
    [InlineData("eqwd:private-server-response")]
    [InlineData("eqz:private-invalid-base64")]
    [InlineData("eqz:/w==")]
    public void RejectsInvalidEnvelopeWithoutLeakingResponse(string response)
    {
        var error = Assert.Throws<ProtocolException>(() => EnvelopeCodec.Decode(response));
        Assert.DoesNotContain(response, error.ToString());
        Assert.Null(error.InnerException);
    }

    [Fact]
    public void ReadsJsonWithoutExecutingPageScripts()
    {
        using var json = EmbeddedJson.Read("""
            <script>throw new Error('must never execute');
            $j('#panel').userhome({"text":"A } brace and \"quote\"", "nested":{"x":1}});
            </script>
            """, "userhome");
        Assert.Equal("A } brace and \"quote\"", json.RootElement.GetProperty("text").GetString());
    }

    [Theory]
    [InlineData("<html>Login</html>")]
    [InlineData("<div>.userhome({})</div>")]
    [InlineData("<script>x.userhome({invalid});</script>")]
    [InlineData("<script>x.userhome({});x.userhome({});</script>")]
    [InlineData("<script>x.userhome({} + other);</script>")]
    public void RejectsMissingMalformedOrAmbiguousPageData(string html) =>
        Assert.Throws<ProtocolException>(() => EmbeddedJson.Read(html, "userhome"));

    [Fact]
    public void ReadsOnlyChildrenExplicitlyLinkedToParent()
    {
        var children = ParentPageParser.Parse("""
            <script>$j('#home').userhome({
              "parentStudentids":[-101,"-102"],
              "dbi":{"students":{
                "-101":{"id":"-101","firstname":"Ada","lastname":"Example"},
                "-102":{"id":-102,"firstname":"Ben","lastname":"Example"},
                "-999":{"id":"-999","firstname":"Unrelated","lastname":"Student"}
              }}
            });</script>
            """);
        Assert.Equal(["-101", "-102"], children.Select(child => child.Id));
        Assert.Equal("Ada", children[0].FirstName);
    }

    [Theory]
    [InlineData("{\"parentStudentids\":[],\"dbi\":{\"students\":{}}}", false)]
    [InlineData("{\"dbi\":{\"students\":{}}}", true)]
    [InlineData("{\"parentStudentids\":[-101],\"dbi\":{\"students\":{}}}", true)]
    public void DistinguishesEmptyChildrenFromBrokenData(string json, bool invalid)
    {
        var html = "<script>x.userhome(" + json + ");</script>";
        if (invalid)
            Assert.Throws<ProtocolException>(() => ParentPageParser.Parse(html));
        else
            Assert.Empty(ParentPageParser.Parse(html));
    }

    [Fact]
    public void JoinsGradesToProviderEventAndSubjectWithoutAssumingNumericGrades()
    {
        var grade = Assert.Single(GradesPageParser.Parse(Grades, "-101"));
        Assert.Equal("Mathematik", grade.Subject);
        Assert.Equal("Übung", grade.Assessment);
        Assert.Equal("2 (gut erklärt)", grade.Value);
        Assert.Equal(0.5m, grade.AssessmentParameter);
        Assert.Equal("1", grade.AssessmentType);
        Assert.Equal(new DateTime(2025, 2, 3, 10, 15, 0), grade.RecordedAt);
        Assert.Equal(DateTimeKind.Unspecified, grade.RecordedAt.Kind);
    }

    [Fact]
    public void RejectsAnotherChildInsteadOfReturningTheirGrades() =>
        Assert.Throws<ProtocolException>(() => GradesPageParser.Parse(Grades, "-102"));

    [Theory]
    [InlineData("\"studentid\":\"-101\"", "\"studentid\":\"-102\"")]
    [InlineData("\"udalostid\":\"301\"", "\"udalostid\":\"999\"")]
    [InlineData("2025-02-03 10:15:00", "invalid-date")]
    [InlineData("\"p_vaha\":\"0.5\"", "\"p_vaha\":\"unknown\"")]
    public void RejectsInconsistentOrMalformedGradeData(string before, string after) =>
        Assert.Throws<ProtocolException>(() => GradesPageParser.Parse(Grades.Replace(before, after), "-101"));

    // Entirely invented values; only field structure comes from the authorized capture.
    private const string Grades = """
        <script>$j('#grades').znamkyStudentViewer({
          "studentid":-101,
          "vsetkyZnamky":[{
            "provider":"edupage","znamkaid":"201","studentid":"-101",
            "predmetid":"-10","udalostid":"301","data":"2 (gut erklärt)",
            "datum":"2025-02-03 10:15:00"
          }],
          "vsetkyUdalosti":{"edupage":{"301":{
            "p_meno":"Übung","p_typ_udalosti":"1","p_vaha":"0.5"
          }}},
          "predmety":{"-10":{"p_meno":"Mathematik"}}
        });</script>
        """;
}
