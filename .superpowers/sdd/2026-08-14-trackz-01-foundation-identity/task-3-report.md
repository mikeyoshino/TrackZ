# Task 3 Report: Stable Business Error Contract

## Summary

Added the published `BusinessErrorCode` contract with immutable numeric values, immutable camel-case `ApiProblemDetails`, `BusinessException`, and middleware that translates only business exceptions to `application/problem+json`. The response includes RFC ProblemDetails fields, the numeric business code, server message, request trace ID, and a `null` `fieldErrors` value. Non-business exceptions are deliberately rethrown to the outer exception handler.

## Files Changed

- `src/TrackZ.Contracts/Errors/BusinessErrorCode.cs`
- `src/TrackZ.Contracts/Errors/ApiProblemDetails.cs`
- `src/TrackZ.Application/Common/Exceptions/BusinessException.cs`
- `src/TrackZ.Api/Middleware/BusinessExceptionMiddleware.cs`
- `tests/TrackZ.Api.Tests/Errors/BusinessExceptionMiddlewareTests.cs`

## TDD Evidence

### RED

After the test file was corrected for C# collection-initializer syntax, the following command failed at compilation exactly because Task 3 production types were absent:

```sh
dotnet test tests/TrackZ.Api.Tests/TrackZ.Api.Tests.csproj --filter BusinessExceptionMiddlewareTests
```

Observed errors: `CS0234` for missing `TrackZ.Api.Middleware`, `TrackZ.Application.Common.Exceptions`, and `TrackZ.Contracts.Errors`, followed by `CS0246` for `BusinessErrorCode`.

### GREEN

Implemented only the requested contract, exception, and middleware. The same focused command then passed:

```sh
dotnet test tests/TrackZ.Api.Tests/TrackZ.Api.Tests.csproj --filter BusinessExceptionMiddlewareTests
```

Observed result: 15 passed, 0 failed, 0 skipped.

The tests assert all 13 published numeric values, status, `application/problem+json`, camel-case `errorCode`/`message`/`traceId`, null `fieldErrors`, ProblemDetails title/status, and unexpected-exception propagation.

## Verification Evidence

```sh
dotnet test tests/TrackZ.Api.Tests/TrackZ.Api.Tests.csproj --no-restore
```

Passed: 16 passed, 0 failed, 0 skipped. This is the strongest feasible non-mobile test graph and compiles Contracts, Domain, Application, Infrastructure, API, and API tests.

```sh
dotnet test TrackZ.slnx --no-restore
```

All non-mobile test projects passed (Domain 1, Application 4, Infrastructure 2, API 16; Mobile.Tests 1). The solution command remains blocked by the pre-existing environment condition `XA5300`: Android SDK directory is not installed/configured for the MAUI Android target.

## Risks and Limitations

- Full MAUI Android solution verification remains unavailable until an Android SDK is installed or `AndroidSdkDirectory` is configured.
- Error-code-specific ProblemDetails type URIs and localized messages belong to later endpoint/localization work; this foundational middleware uses the stable generic business-rule type and preserves the supplied business message.

## Fix Round 1: Started and Dirty Responses

### Root Cause

The middleware previously caught a `BusinessException` unconditionally. If downstream code had already started the response, it attempted to change committed headers and append a second JSON document. If the response was still unstarted but had pending state, it retained downstream headers and status. ASP.NET Core documents that headers cannot be changed after `HasStarted`, and `HttpResponse.Clear()` resets an unstarted response's headers, status, and body.

### RED

Added two focused tests and ran:

```sh
dotnet test tests/TrackZ.Api.Tests/TrackZ.Api.Tests.csproj --filter "FullyQualifiedName~Business_exception_after_response_started|FullyQualifiedName~Business_exception_replaces_a_dirty_unstarted"
```

Observed result: 2 failed, 0 passed. The started-response test reported that no `BusinessException` was propagated. The dirty-unstarted test reported `Assert.False() Failure` because the pending `X-Downstream` header remained. Test setup uses an explicit started response feature rather than pretending a `MemoryStream` can model clearing bytes already committed to a client.

### GREEN

The middleware now rethrows the original exception when `Response.HasStarted`; otherwise it calls `Response.Clear()` before setting the ProblemDetails status/content type and serializing. The same focused command passed: 2 passed, 0 failed, 0 skipped.
