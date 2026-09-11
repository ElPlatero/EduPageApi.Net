# EduPageApi.Net

An independent, fluent .NET client for consuming authorized EduPage data.

The project is currently in its planning and protocol-discovery phase. EduPage's
web protocol will be documented from authorized, independently captured network
traffic before client functionality is implemented.

## Development workflow

- `main` is the only long-lived branch and must always be releasable.
- Changes are proposed through short-lived feature or hotfix branches.
- Pull requests are integrated using squash merge only.
- Every merged pull request contributes exactly one commit to `main`.
- Merge commits and rebase merges are disabled.
- Merged branches are deleted automatically.
- Releases are identified by annotated tags on `main`.

No credentials, active session data, or personal EduPage data may be committed.
