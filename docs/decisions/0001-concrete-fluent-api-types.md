# 0001: Concrete types for the public fluent API

- Status: Accepted
- Date: 2026-09-11

## Context

The client will expose a fluent API whose final shape can only be validated while
independently discovering EduPage operations. Introducing one public interface for
every step of that fluent graph would duplicate the API surface and turn early
assumptions into contracts.

Tests still need seams around external communication, and the later consuming
application must not depend on EduPage-specific behavior throughout its domain.

## Decision

Public fluent API stages are concrete, sealed and immutable classes. Adding a
configuration step returns a new query object rather than mutating shared state.

Interfaces are introduced only at genuine implementation boundaries, initially:

- HTTP transport;
- EduPage protocol operations and parsing;
- optional session persistence.

The consuming application defines its own narrow domain-facing interfaces and
implements them with adapters backed by `EduPageApi.Net`.

## Consequences

- The initial public API remains small and can evolve from observed use cases.
- HTTP and parser behavior remain independently testable.
- Consumers mock their own domain ports instead of a long fluent call chain.
- A public client interface can still be added later if a real alternative
  implementation requires it.
