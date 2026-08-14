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

## Fix Round 2: Safe Generic Exception Responses

### Summary

Added `UnhandledExceptionMiddleware` outside `BusinessExceptionMiddleware` in the API pipeline. It rethrows business exceptions, rethrows every exception after the response starts, and otherwise clears the pending response before returning safe localized ProblemDetails. `InternalServerError = 90001` is a new immutable server-category code for the shared mobile payload. The handler does not serialize exception details, request bodies, tokens, or other request data.

### RED

Added generic-handler and enum-stability tests, then ran:

```sh
dotnet test tests/TrackZ.Api.Tests/TrackZ.Api.Tests.csproj --filter "UnhandledExceptionMiddlewareTests|Published_business_error_code_has_its_immutable_numeric_value"
```

Observed compilation failures: `CS0117` because `BusinessErrorCode.InternalServerError` did not exist, and `CS0246` because `UnhandledExceptionMiddleware` did not exist.

### GREEN

Added the outer middleware, `90001`, localized English/Thai resource entries, and pipeline wiring after request localization and before the business middleware. The same focused command passed: 21 passed, 0 failed, 0 skipped. It verifies English and Thai safe messages, HTTP 500, `application/problem+json`, trace ID, null field errors, no exception/token leakage, started-response propagation without appended JSON, and preservation of a wrapped business exception's 404/30001/message.

### Full API Verification

```sh
dotnet test tests/TrackZ.Api.Tests/TrackZ.Api.Tests.csproj --no-restore
```

Passed: 39 passed, 0 failed, 0 skipped.

## Fix Round 3: Business Middleware Handles Only Business Exceptions

### Correction

`BusinessExceptionMiddleware` must handle only `BusinessException`. A later implementation had added `JsonException` and `BadHttpRequestException` catches that converted arbitrary downstream failures into validation responses. Those catches have been removed. Identity endpoints retain their own explicit JSON parsing and normalization, so malformed and wrong-type identity requests continue to return the localized `400`/`10009` validation contract at the endpoint boundary.

### RED

Added outer-pipeline regressions for a downstream `JsonException` and `BadHttpRequestException`, then ran:

```sh
dotnet test tests/TrackZ.Api.Tests/TrackZ.Api.Tests.csproj --filter Non_business_parser_exception_reaches_the_outer_safe_problem_handler
```

Observed result: 2 failed, 0 passed. Both failures expected HTTP 500 but received 400, proving the business middleware's broad catches hid the non-business exceptions.

### GREEN

Removed only the two non-business catch clauses and their validation response helper. The targeted middleware regression plus business contract class passed:

```sh
dotnet test tests/TrackZ.Api.Tests/TrackZ.Api.Tests.csproj --filter "Non_business_parser_exception_reaches_the_outer_safe_problem_handler|BusinessExceptionMiddlewareTests"
```

Passed: 24 passed, 0 failed, 0 skipped. Both parser exceptions now reach `UnhandledExceptionMiddleware`, which returns the localized safe `500`/`90001` response with trace ID and no parser details.

Identity endpoint-local validation coverage also remained green:

```sh
dotnet test tests/TrackZ.Api.Tests/TrackZ.Api.Tests.csproj --filter "Identity_boundary_and_binding_failures_use_the_shared_validation_contract|Malformed_body_uses_exact_localized_body_messages|Wrong_type_field_message_is_exactly_localized_and_safe|Identity_routes_reject_unsupported_body_content_types_with_safe_body_error"
```

Passed: 8 passed, 0 failed, 0 skipped.

### Full API Verification

```sh
dotnet test tests/TrackZ.Api.Tests/TrackZ.Api.Tests.csproj --no-restore
```

Passed: 89 passed, 0 failed, 0 skipped (1 minute 44 seconds).
