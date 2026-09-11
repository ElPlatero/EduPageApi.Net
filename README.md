# EduPageApi.Net

An independent, fluent .NET client for consuming authorized EduPage data.

The project is currently in its planning and protocol-discovery phase. Its first
vertical use case will read grades for a child through an authorized parent
account. EduPage's web protocol will be documented from authorized, independently
captured network traffic before client functionality is implemented.

## Repository structure

```text
EduPageApi.Net.slnx
├── src/EduPageApi.Net/
└── tests/EduPageApi.Net.Tests/
```

The library currently targets .NET 10. The test project is part of this library
repository; it is not the separate application that will eventually consume the
package.

## Development workflow

- `main` is the only long-lived branch and must always be releasable.
- Changes are proposed through short-lived feature or hotfix branches.
- Pull requests are integrated using squash merge only.
- Every merged pull request contributes exactly one commit to `main`.
- Merge commits and rebase merges are disabled.
- Merged branches are deleted automatically.
- Releases are identified by annotated tags on `main`.

No credentials, active session data, or personal EduPage data may be committed.

## API design

The public fluent API will use concrete, sealed and immutable types. Interfaces
are reserved for genuine internal boundaries such as transport, protocol parsing
and session persistence. Consumer applications should define their own narrowly
scoped ports instead of mocking an entire fluent client graph.
