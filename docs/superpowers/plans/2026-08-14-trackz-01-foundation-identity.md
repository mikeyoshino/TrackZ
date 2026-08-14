# TrackZ Foundation and Identity Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Create a compiling .NET 10 solution with native MAUI targets, Clean Architecture boundaries, PostgreSQL persistence, stable ProblemDetails errors, and a tested email/password JWT authentication API.

**Architecture:** The backend is a modular monolith with Domain, Application, Infrastructure, API, and Contracts projects. MediatR feature slices live in Application; EF Core, Identity primitives, JWT, and PostgreSQL live in Infrastructure. The MAUI project is scaffolded now, but feature UI arrives in later plans.

**Tech Stack:** .NET SDK 10.0.302, .NET MAUI XAML, ASP.NET Core minimal APIs, MediatR, EF Core with Npgsql, PostgreSQL 17 container, xUnit, ASP.NET Core integration testing.

## Global Constraints

- Target Android and iOS from one native .NET MAUI XAML application; do not use WebView for production UI.
- Keep authoritative business logic on the server and organize Application code by feature slice.
- Use five-digit `CCEEE` business error codes; published numeric values are immutable.
- Store all timestamps as UTC and all canonical weights as decimal kilograms.
- Support Thai and English resources from the first user-facing endpoint.
- Do not log passwords, access tokens, refresh tokens, or personally identifying request bodies.
- The directory is not currently a Git repository; request approval before executing `git init`.
- MAUI workload is absent; request approval before executing networked workload installation.

---

### Task 1: Repository and Solution Scaffold

**Files:**
- Create: `.gitignore`
- Create: `global.json`
- Create: `Directory.Build.props`
- Create: `TrackZ.slnx`
- Create: `src/TrackZ.Contracts/TrackZ.Contracts.csproj`
- Create: `src/TrackZ.Domain/TrackZ.Domain.csproj`
- Create: `src/TrackZ.Application/TrackZ.Application.csproj`
- Create: `src/TrackZ.Infrastructure/TrackZ.Infrastructure.csproj`
- Create: `src/TrackZ.Api/TrackZ.Api.csproj`
- Create: `src/TrackZ.Mobile/TrackZ.Mobile.csproj`
- Create: `tests/TrackZ.Domain.Tests/TrackZ.Domain.Tests.csproj`
- Create: `tests/TrackZ.Application.Tests/TrackZ.Application.Tests.csproj`
- Create: `tests/TrackZ.Infrastructure.Tests/TrackZ.Infrastructure.Tests.csproj`
- Create: `tests/TrackZ.Api.Tests/TrackZ.Api.Tests.csproj`
- Create: `tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj`

**Interfaces:**
- Consumes: installed .NET SDK `10.0.302`; user approval for Git initialization and MAUI workload installation.
- Produces: solution/project graph used by every later task.

- [ ] **Step 1: Verify and initialize repository prerequisites**

Run after approval:

```bash
dotnet --version
dotnet workload install maui
git init
dotnet new gitignore
```

Expected: SDK prints `10.0.302`, MAUI workload succeeds, and `git status` reports an empty repository.

- [ ] **Step 2: Pin SDK and shared compiler settings**

Create `global.json`:

```json
{
  "sdk": {
    "version": "10.0.302",
    "rollForward": "latestPatch"
  }
}
```

Create `Directory.Build.props`:

```xml
<Project>
  <PropertyGroup>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>
</Project>
```

- [ ] **Step 3: Create solution and projects**

```bash
dotnet new sln -n TrackZ --format slnx
dotnet new classlib -n TrackZ.Contracts -o src/TrackZ.Contracts -f net10.0
dotnet new classlib -n TrackZ.Domain -o src/TrackZ.Domain -f net10.0
dotnet new classlib -n TrackZ.Application -o src/TrackZ.Application -f net10.0
dotnet new classlib -n TrackZ.Infrastructure -o src/TrackZ.Infrastructure -f net10.0
dotnet new webapi -n TrackZ.Api -o src/TrackZ.Api -f net10.0 --no-https
dotnet new maui -n TrackZ.Mobile -o src/TrackZ.Mobile
dotnet new xunit -n TrackZ.Domain.Tests -o tests/TrackZ.Domain.Tests -f net10.0
dotnet new xunit -n TrackZ.Application.Tests -o tests/TrackZ.Application.Tests -f net10.0
dotnet new xunit -n TrackZ.Infrastructure.Tests -o tests/TrackZ.Infrastructure.Tests -f net10.0
dotnet new xunit -n TrackZ.Api.Tests -o tests/TrackZ.Api.Tests -f net10.0
dotnet new xunit -n TrackZ.Mobile.Tests -o tests/TrackZ.Mobile.Tests -f net10.0
dotnet sln TrackZ.slnx add src/*/*.csproj tests/*/*.csproj
```

Set these stable MAUI identity properties in `src/TrackZ.Mobile/TrackZ.Mobile.csproj`:

```xml
<TargetFrameworks>net10.0-android;net10.0-ios</TargetFrameworks>
<ApplicationTitle>TrackZ</ApplicationTitle>
<ApplicationId>com.trackz.app</ApplicationId>
<ApplicationDisplayVersion>1.0</ApplicationDisplayVersion>
<ApplicationVersion>1</ApplicationVersion>
```

- [ ] **Step 4: Add project references and verify the empty graph**

```bash
dotnet add src/TrackZ.Application reference src/TrackZ.Domain src/TrackZ.Contracts
dotnet add src/TrackZ.Infrastructure reference src/TrackZ.Application src/TrackZ.Domain
dotnet add src/TrackZ.Api reference src/TrackZ.Application src/TrackZ.Infrastructure src/TrackZ.Contracts
dotnet add src/TrackZ.Mobile reference src/TrackZ.Contracts
dotnet add tests/TrackZ.Domain.Tests reference src/TrackZ.Domain
dotnet add tests/TrackZ.Application.Tests reference src/TrackZ.Application
dotnet add tests/TrackZ.Infrastructure.Tests reference src/TrackZ.Infrastructure
dotnet add tests/TrackZ.Api.Tests reference src/TrackZ.Api
dotnet add tests/TrackZ.Mobile.Tests reference src/TrackZ.Mobile
dotnet build TrackZ.slnx
```

Expected: all projects build with zero warnings and zero errors.

- [ ] **Step 5: Commit the scaffold**

```bash
git add .gitignore global.json Directory.Build.props TrackZ.slnx src tests
git commit -m "build: scaffold TrackZ solution"
```

### Task 2: Architecture Boundaries and Dependency Registration

**Files:**
- Create: `src/TrackZ.Domain/AssemblyMarker.cs`
- Create: `src/TrackZ.Application/AssemblyMarker.cs`
- Create: `src/TrackZ.Infrastructure/DependencyInjection.cs`
- Create: `src/TrackZ.Application/DependencyInjection.cs`
- Create: `tests/TrackZ.Application.Tests/Architecture/DependencyRulesTests.cs`

**Interfaces:**
- Consumes: solution graph from Task 1.
- Produces: `AddApplication()` and `AddInfrastructure(IConfiguration)` extension methods; enforceable dependency rules.

- [ ] **Step 1: Write failing architecture tests**

Add an architecture-test package and create `DependencyRulesTests.cs`:

```bash
dotnet add tests/TrackZ.Application.Tests package NetArchTest.Rules
```

```csharp
using NetArchTest.Rules;

namespace TrackZ.Application.Tests.Architecture;

public sealed class DependencyRulesTests
{
    [Fact]
    public void Domain_Must_Not_Depend_On_Outer_Layers()
    {
        var result = Types.InAssembly(typeof(TrackZ.Domain.AssemblyMarker).Assembly)
            .ShouldNot().HaveDependencyOnAny("TrackZ.Application", "TrackZ.Infrastructure", "TrackZ.Api")
            .GetResult();

        Assert.True(result.IsSuccessful, string.Join(Environment.NewLine, result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Application_Must_Not_Depend_On_Infrastructure_Or_Api()
    {
        var result = Types.InAssembly(typeof(TrackZ.Application.AssemblyMarker).Assembly)
            .ShouldNot().HaveDependencyOnAny("TrackZ.Infrastructure", "TrackZ.Api")
            .GetResult();

        Assert.True(result.IsSuccessful, string.Join(Environment.NewLine, result.FailingTypeNames ?? []));
    }
}
```

- [ ] **Step 2: Run the test and observe the missing marker failure**

```bash
dotnet test tests/TrackZ.Application.Tests --filter DependencyRulesTests
```

Expected: compile failure because `AssemblyMarker` types do not exist.

- [ ] **Step 3: Add markers and dependency-registration entry points**

```csharp
// src/TrackZ.Domain/AssemblyMarker.cs
namespace TrackZ.Domain;
public sealed class AssemblyMarker;
```

```csharp
// src/TrackZ.Application/AssemblyMarker.cs
namespace TrackZ.Application;
public sealed class AssemblyMarker;
```

```csharp
// src/TrackZ.Application/DependencyInjection.cs
using Microsoft.Extensions.DependencyInjection;

namespace TrackZ.Application;
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddMediatR(configuration =>
            configuration.RegisterServicesFromAssembly(typeof(AssemblyMarker).Assembly));
        return services;
    }
}
```

```csharp
// src/TrackZ.Infrastructure/DependencyInjection.cs
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace TrackZ.Infrastructure;
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
        => services;
}
```

Add MediatR to Application and abstractions packages required to compile the extension.

- [ ] **Step 4: Run architecture tests and build**

```bash
dotnet test tests/TrackZ.Application.Tests --filter DependencyRulesTests
dotnet build TrackZ.slnx
```

Expected: PASS and build succeeds.

- [ ] **Step 5: Commit architecture guardrails**

```bash
git add src/TrackZ.Domain src/TrackZ.Application src/TrackZ.Infrastructure tests/TrackZ.Application.Tests
git commit -m "build: enforce clean architecture boundaries"
```

### Task 3: Stable Business Error Contract

**Files:**
- Create: `src/TrackZ.Contracts/Errors/BusinessErrorCode.cs`
- Create: `src/TrackZ.Contracts/Errors/ApiProblemDetails.cs`
- Create: `src/TrackZ.Application/Common/Exceptions/BusinessException.cs`
- Create: `src/TrackZ.Api/Middleware/BusinessExceptionMiddleware.cs`
- Create: `tests/TrackZ.Api.Tests/Errors/BusinessExceptionMiddlewareTests.cs`

**Interfaces:**
- Consumes: ASP.NET Core request pipeline and shared Contracts assembly.
- Produces: `BusinessException(BusinessErrorCode code, string message, int statusCode)` and JSON ProblemDetails contract.

- [ ] **Step 1: Write a failing middleware contract test**

```csharp
[Fact]
public async Task Business_exception_returns_stable_problem_details()
{
    var middleware = new BusinessExceptionMiddleware(_ =>
        throw new BusinessException(BusinessErrorCode.WorkoutNotFound,
            "Workout session was not found.", StatusCodes.Status404NotFound));
    var context = new DefaultHttpContext();
    context.Response.Body = new MemoryStream();

    await middleware.InvokeAsync(context);
    context.Response.Body.Position = 0;
    var body = await JsonSerializer.DeserializeAsync<ApiProblemDetails>(context.Response.Body);

    Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
    Assert.Equal(30001, (int)body!.ErrorCode);
    Assert.Equal("Workout session was not found.", body.Message);
    Assert.False(string.IsNullOrWhiteSpace(body.TraceId));
}
```

- [ ] **Step 2: Run the test and verify missing contract types**

```bash
dotnet test tests/TrackZ.Api.Tests --filter Business_exception_returns_stable_problem_details
```

Expected: compile failure for the undefined error types and middleware.

- [ ] **Step 3: Implement the enum, exception, response, and middleware**

```csharp
public enum BusinessErrorCode
{
    InvalidCredentials = 10001,
    EmailAlreadyExists = 10002,
    RefreshTokenInvalid = 10003,
    EmailVerificationInvalid = 10004,
    PasswordResetInvalid = 10005,
    ExerciseNotFound = 20001,
    ExerciseNameDuplicate = 20002,
    WorkoutNotFound = 30001,
    WorkoutAlreadyCompleted = 30002,
    InvalidSetValue = 30004,
    ImageTooLarge = 50002,
    ImageTypeNotSupported = 50003,
    VersionConflict = 60001
}
```

```csharp
public sealed record ApiProblemDetails(
    string Type,
    string Title,
    int Status,
    BusinessErrorCode ErrorCode,
    string Message,
    string TraceId,
    IReadOnlyDictionary<string, string[]>? FieldErrors);
```

The middleware catches only `BusinessException`, writes the exception's status/code/message, and uses `HttpContext.TraceIdentifier`. Leave unexpected exceptions to the generic production exception handler.

- [ ] **Step 4: Run contract and enum-stability tests**

```bash
dotnet test tests/TrackZ.Api.Tests --filter BusinessExceptionMiddlewareTests
dotnet test TrackZ.slnx
```

Expected: all tests pass. Add assertions for every published numeric enum value.

- [ ] **Step 5: Commit the error contract**

```bash
git add src/TrackZ.Contracts src/TrackZ.Application/Common src/TrackZ.Api/Middleware tests/TrackZ.Api.Tests
git commit -m "feat: add stable business error contract"
```

### Task 4: PostgreSQL Persistence and Identity Model

**Files:**
- Create: `compose.yaml`
- Create: `src/TrackZ.Domain/Identity/User.cs`
- Create: `src/TrackZ.Domain/Identity/RefreshToken.cs`
- Create: `src/TrackZ.Application/Common/Interfaces/IAppDbContext.cs`
- Create: `src/TrackZ.Infrastructure/Persistence/AppDbContext.cs`
- Create: `src/TrackZ.Infrastructure/Persistence/Configurations/UserConfiguration.cs`
- Create: `src/TrackZ.Infrastructure/Persistence/Configurations/RefreshTokenConfiguration.cs`
- Create: `tests/TrackZ.Infrastructure.Tests/Persistence/IdentityPersistenceTests.cs`
- Create: `tests/TrackZ.Infrastructure.Tests/Persistence/PostgreSqlFixture.cs`

**Interfaces:**
- Consumes: Domain entities and Infrastructure registration.
- Produces: `IAppDbContext`, PostgreSQL schema, and transactional identity persistence.

- [ ] **Step 1: Write a failing PostgreSQL persistence test**

```csharp
[Fact]
public async Task User_email_is_unique_case_insensitively()
{
    await using var database = await PostgreSqlFixture.StartAsync();
    await database.Db.Users.AddAsync(User.Create("athlete@example.com", "hash"));
    await database.Db.SaveChangesAsync();
    await database.Db.Users.AddAsync(User.Create("ATHLETE@example.com", "hash-2"));

    await Assert.ThrowsAsync<DbUpdateException>(() => database.Db.SaveChangesAsync());
}
```

Use a PostgreSQL test-container package; do not substitute EF InMemory.

- [ ] **Step 2: Run the test and confirm the model is missing**

```bash
dotnet test tests/TrackZ.Infrastructure.Tests --filter User_email_is_unique_case_insensitively
```

Expected: compile failure for `User`, `AppDbContext`, and fixture types.

- [ ] **Step 3: Implement entities, EF configurations, and container compose file**

```yaml
services:
  postgres:
    image: postgres:17-alpine
    environment:
      POSTGRES_DB: trackz
      POSTGRES_USER: trackz
      POSTGRES_PASSWORD: trackz_local_only
    ports:
      - "5432:5432"
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U trackz -d trackz"]
      interval: 5s
      timeout: 3s
      retries: 10
```

Normalize email with `Trim().ToUpperInvariant()`, configure a unique index on `NormalizedEmail`, and store refresh-token hashes only.

- [ ] **Step 4: Add and exercise the initial migration**

```bash
dotnet ef migrations add InitialIdentity --project src/TrackZ.Infrastructure --startup-project src/TrackZ.Api --output-dir Persistence/Migrations
dotnet test tests/TrackZ.Infrastructure.Tests --filter IdentityPersistenceTests
```

Expected: PostgreSQL integration tests pass.

- [ ] **Step 5: Commit persistence**

```bash
git add compose.yaml src/TrackZ.Domain/Identity src/TrackZ.Application/Common src/TrackZ.Infrastructure/Persistence tests/TrackZ.Infrastructure.Tests
git commit -m "feat: persist users and refresh tokens"
```

### Task 5: Registration and Login Feature Slices

**Files:**
- Create: `src/TrackZ.Application/Identity/Register/RegisterCommand.cs`
- Create: `src/TrackZ.Application/Identity/Register/RegisterHandler.cs`
- Create: `src/TrackZ.Application/Identity/Login/LoginCommand.cs`
- Create: `src/TrackZ.Application/Identity/Login/LoginHandler.cs`
- Create: `src/TrackZ.Application/Identity/Common/AuthTokenPair.cs`
- Create: `src/TrackZ.Contracts/Identity/RegisteredUser.cs`
- Create: `src/TrackZ.Application/Common/Interfaces/IPasswordHasher.cs`
- Create: `src/TrackZ.Application/Common/Interfaces/ITokenService.cs`
- Create: `src/TrackZ.Infrastructure/Identity/PasswordHasher.cs`
- Create: `src/TrackZ.Infrastructure/Identity/JwtTokenService.cs`
- Create: `src/TrackZ.Api/Endpoints/IdentityEndpoints.cs`
- Test: `tests/TrackZ.Application.Tests/Identity/RegisterHandlerTests.cs`
- Test: `tests/TrackZ.Api.Tests/Identity/LoginEndpointTests.cs`

**Interfaces:**
- Consumes: `IAppDbContext`, `IPasswordHasher`, and `ITokenService`.
- Produces: `AuthTokenPair(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt)` and `/api/v1/auth/register|login`.

- [ ] **Step 1: Write failing register and invalid-login tests**

```csharp
[Fact]
public async Task Register_creates_user_with_hashed_password()
{
    var result = await _handler.Handle(new RegisterCommand("athlete@example.com", "ValidPass!42"), default);
    Assert.Equal("athlete@example.com", result.Email);
    Assert.DoesNotContain("ValidPass!42", _db.Users.Single().PasswordHash);
}

[Fact]
public async Task Invalid_login_returns_code_10001()
{
    var response = await _client.PostAsJsonAsync("/api/v1/auth/login",
        new { email = "athlete@example.com", password = "wrong" });
    var problem = await response.Content.ReadFromJsonAsync<ApiProblemDetails>();
    Assert.Equal(10001, (int)problem!.ErrorCode);
}
```

- [ ] **Step 2: Run focused tests and verify failure**

```bash
dotnet test tests/TrackZ.Application.Tests --filter RegisterHandlerTests
dotnet test tests/TrackZ.Api.Tests --filter LoginEndpointTests
```

Expected: compile failure for missing commands/handlers/services.

- [ ] **Step 3: Implement minimal handlers and endpoints**

Register validates normalized-email uniqueness and password policy, hashes the password, and saves once. Login always returns the same `InvalidCredentials` response for an unknown email or bad password. JWT claims contain `sub`, `jti`, and device/session ID; never place email in logs.

```csharp
public sealed record RegisterCommand(string Email, string Password) : IRequest<RegisteredUser>;
public sealed record LoginCommand(string Email, string Password, string DeviceName) : IRequest<AuthTokenPair>;
public sealed record RegisteredUser(Guid UserId, string Email);
```

- [ ] **Step 4: Run feature and full tests**

```bash
dotnet test tests/TrackZ.Application.Tests --filter Identity
dotnet test tests/TrackZ.Api.Tests --filter Identity
dotnet test TrackZ.slnx
```

Expected: valid registration/login pass; duplicate email and bad credentials return localized stable errors.

- [ ] **Step 5: Commit registration and login**

```bash
git add src/TrackZ.Application/Identity src/TrackZ.Application/Common/Interfaces src/TrackZ.Infrastructure/Identity src/TrackZ.Api/Endpoints tests
git commit -m "feat: add registration and login"
```

### Task 6: Refresh Rotation, Localization, and Identity Security

**Files:**
- Create: `src/TrackZ.Application/Identity/Refresh/RefreshCommand.cs`
- Create: `src/TrackZ.Application/Identity/Refresh/RefreshHandler.cs`
- Create: `src/TrackZ.Application/Identity/Logout/LogoutCommand.cs`
- Create: `src/TrackZ.Api/Resources/BusinessMessages.resx`
- Create: `src/TrackZ.Api/Resources/BusinessMessages.th.resx`
- Modify: `src/TrackZ.Api/Endpoints/IdentityEndpoints.cs`
- Modify: `src/TrackZ.Api/Program.cs`
- Test: `tests/TrackZ.Api.Tests/Identity/RefreshRotationTests.cs`
- Test: `tests/TrackZ.Api.Tests/Errors/LocalizedProblemDetailsTests.cs`

**Interfaces:**
- Consumes: refresh-token persistence and ProblemDetails middleware.
- Produces: rotated refresh flow, per-device logout, rate-limited identity endpoints, and Thai/English messages.

- [ ] **Step 1: Write failing reuse and localization tests**

```csharp
[Fact]
public async Task Reusing_rotated_refresh_token_returns_10003()
{
    var original = await LoginAsync();
    _ = await RefreshAsync(original.RefreshToken);
    var response = await PostRefreshAsync(original.RefreshToken);
    Assert.Equal(10003, await response.ReadErrorCodeAsync());
}

[Fact]
public async Task Thai_accept_language_returns_Thai_message_with_same_code()
{
    var response = await InvalidLoginAsync("th-TH");
    var problem = await response.Content.ReadFromJsonAsync<ApiProblemDetails>();
    Assert.Equal(10001, (int)problem!.ErrorCode);
    Assert.Equal("อีเมลหรือรหัสผ่านไม่ถูกต้อง", problem.Message);
}
```

- [ ] **Step 2: Run focused tests and verify failure**

```bash
dotnet test tests/TrackZ.Api.Tests --filter "RefreshRotationTests|LocalizedProblemDetailsTests"
```

Expected: refresh endpoint and localized resources are missing.

- [ ] **Step 3: Implement atomic rotation, logout, localization, and rate limits**

Refresh runs in one transaction: lock the token row, reject expired/revoked/rotated hashes, revoke the old token, and insert the replacement. Configure request localization for `en` and `th`, and attach a stricter fixed-window rate-limit policy to register/login/refresh.

```csharp
public sealed record RefreshCommand(string RefreshToken, string DeviceName) : IRequest<AuthTokenPair>;
public sealed record LogoutCommand(Guid SessionId) : IRequest;
```

- [ ] **Step 4: Run security tests and build**

```bash
dotnet test tests/TrackZ.Api.Tests --filter Identity
dotnet test TrackZ.slnx
dotnet build src/TrackZ.Mobile/TrackZ.Mobile.csproj -f net10.0-android
```

Expected: rotation/reuse/localization/rate-limit tests pass and Android target builds.

- [ ] **Step 5: Commit the identity milestone**

```bash
git add src/TrackZ.Application/Identity src/TrackZ.Api src/TrackZ.Infrastructure tests
git commit -m "feat: secure and localize identity flows"
```

Plan 1 is complete when the API starts against PostgreSQL, register/login/refresh/logout work with stable localized errors, all tests pass, and the empty MAUI Android target compiles.
