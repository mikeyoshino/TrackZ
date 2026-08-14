# Task 2 Report: Architecture Boundaries and Dependency Registration

## Summary

Added Domain and Application assembly markers, clean-architecture dependency-rule tests, and DI entry points for Application and Infrastructure. `AddApplication()` registers MediatR handlers from the Application assembly. `AddInfrastructure(IConfiguration)` is the intentionally empty composition-root entry point for later infrastructure registrations.

## Files Changed

- `src/TrackZ.Domain/AssemblyMarker.cs`
- `src/TrackZ.Application/AssemblyMarker.cs`
- `src/TrackZ.Application/DependencyInjection.cs`
- `src/TrackZ.Application/TrackZ.Application.csproj`
- `src/TrackZ.Infrastructure/DependencyInjection.cs`
- `src/TrackZ.Infrastructure/TrackZ.Infrastructure.csproj`
- `tests/TrackZ.Application.Tests/Architecture/DependencyRulesTests.cs`
- `tests/TrackZ.Application.Tests/TrackZ.Application.Tests.csproj`

## TDD Evidence

### RED

Command:

```sh
dotnet test tests/TrackZ.Application.Tests/TrackZ.Application.Tests.csproj --filter DependencyRulesTests
```

Observed result: compilation failed with `CS0234` for both missing `TrackZ.Domain.AssemblyMarker` and `TrackZ.Application.AssemblyMarker`, which is the expected failure before production code existed.

### GREEN

Command:

```sh
dotnet test tests/TrackZ.Application.Tests/TrackZ.Application.Tests.csproj --filter DependencyRulesTests
```

Observed result: passed; 2 passed, 0 failed, 0 skipped.

## Build and Test Evidence

```sh
dotnet test tests/TrackZ.Application.Tests/TrackZ.Application.Tests.csproj --no-restore
```

Passed; 3 passed, 0 failed, 0 skipped.

```sh
dotnet test tests/TrackZ.Api.Tests/TrackZ.Api.Tests.csproj --no-restore
```

Passed; 1 passed, 0 failed, 0 skipped. This compiles the non-mobile chain: Contracts, Domain, Application, Infrastructure, API, and API tests, with zero warnings/errors.

```sh
dotnet build src/TrackZ.Mobile/TrackZ.Mobile.csproj --no-restore -v:minimal
```

Blocked by the environment, not the Task 2 changes: Android target compilation reports `XA5300` because the Android SDK directory is not installed/configured. The installed MAUI workload alone does not provide that SDK.

## Risks and Limitations

- The full solution cannot be verified here until an Android SDK is installed or `AndroidSdkDirectory` is configured.
- `AddInfrastructure(IConfiguration)` deliberately has no registrations yet; later persistence and identity work will populate it.

## Fix Round 1: DI Composition Coverage

Added focused composition tests in their owning test projects:

- `TrackZ.Application.Tests.DependencyInjectionTests.AddApplication_Registers_IMediator` builds a real service provider after `AddApplication()` and resolves `IMediator`.
- `TrackZ.Infrastructure.Tests.DependencyInjectionTests.AddInfrastructure_Returns_The_Provided_Service_Collection` passes a real `IConfiguration` and verifies that the same collection instance is returned.

### RED

```sh
dotnet test tests/TrackZ.Application.Tests/TrackZ.Application.Tests.csproj --filter FullyQualifiedName~DependencyInjectionTests
```

Observed result: failed with `InvalidOperationException`: MediatR requires `ILoggerFactory` to be registered. This demonstrated that the existing `AddApplication()` registration could not produce a resolvable mediator.

The Infrastructure API already met the new test's contract, so its sensitivity was proven with an uncommitted temporary mutation of `AddInfrastructure` to return `new ServiceCollection()`:

```sh
dotnet test tests/TrackZ.Infrastructure.Tests/TrackZ.Infrastructure.Tests.csproj --filter FullyQualifiedName~DependencyInjectionTests
```

Observed result: failed with `Assert.Same() Failure`; the original `=> services` implementation was restored immediately afterward.

### GREEN

Added `services.AddLogging()` before MediatR registration and added `Microsoft.Extensions.Logging` 10.0.11 to the Application project. Both focused DI tests then passed.

```sh
dotnet test tests/TrackZ.Application.Tests/TrackZ.Application.Tests.csproj --no-restore
dotnet test tests/TrackZ.Infrastructure.Tests/TrackZ.Infrastructure.Tests.csproj --no-restore
dotnet test tests/TrackZ.Api.Tests/TrackZ.Api.Tests.csproj --no-restore
```

Observed results: Application 4 passed, Infrastructure 2 passed, API 1 passed; no warnings or errors. The API run compiles the complete non-mobile production graph.
