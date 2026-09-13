# EduPageApi.Net

An independent, fluent .NET client for consuming authorized EduPage data.

The initial public API supports parent login and a snapshot of associated
children. Public grade queries and additional authentication steps are not yet
available.

## Usage

```csharp
using EduPageApi;
using Microsoft.Extensions.DependencyInjection;

services.AddEduPageSessions();

// Resolve or constructor-inject the singleton factory.
var factory = serviceProvider.GetRequiredService<EduPageSessionFactory>();
await using var session = await factory
    .ForSchool("my-school")
    .ConnectAsync(username, password, cancellationToken);

var children = session.Children;
```

Each connection creates an independent session owned by the caller. Keep it for
as long as needed and dispose it explicitly. Disposing a DI scope or the provider
does not dispose sessions created by the factory. Registration does not use
IHttpClientFactory or accept externally pooled HTTP clients. Credentials are not
retained by the factory or returned session. Disposing a session releases local
resources; it does not log out on the server. Expired sessions are not
automatically reauthenticated.

DI is optional: `new EduPageSessionFactory()` provides the same behavior.

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
