namespace EduPageApi;

/// <summary>An immutable selection of a child within a session.</summary>
public sealed class EduPageChildQuery
{
    private readonly EduPageSession session;
    private readonly string childId;
    internal EduPageChildQuery(EduPageSession session, string childId)
    {
        this.session = session;
        this.childId = childId;
    }

    /// <summary>Describes a grades query without contacting the server.</summary>
    public EduPageGradesQuery Grades => new(session, childId);
}

/// <summary>An immutable grades query executed within its owning session.</summary>
public sealed class EduPageGradesQuery
{
    private readonly EduPageSession session;
    private readonly string childId;
    internal EduPageGradesQuery(EduPageSession session, string childId)
    {
        this.session = session;
        this.childId = childId;
    }

    /// <summary>Loads grades. Child-dependent operations are serialized within the session.</summary>
    public Task<IReadOnlyList<EduPageGrade>> GetAsync(CancellationToken cancellationToken = default) =>
        session.GetGradesAsync(childId, cancellationToken);
}

/// <summary>A grade with its subject and assessment title.</summary>
/// <param name="Id">The grade identifier.</param>
/// <param name="StudentId">The associated child's identifier.</param>
/// <param name="Subject">The subject name.</param>
/// <param name="Assessment">The assessment title.</param>
/// <param name="Value">The original grade text, including any inline comment.</param>
/// <param name="RecordedAt">The recorded local time, with unspecified time zone.</param>
public sealed record EduPageGrade(string Id, string StudentId, string Subject,
    string Assessment, string Value, DateTime RecordedAt);
