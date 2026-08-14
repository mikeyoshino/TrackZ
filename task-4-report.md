# Task 4: PostgreSQL Persistence and Identity Model

## Summary

- Added PostgreSQL 17 local compose configuration.
- Added `User` and `RefreshToken` domain entities, with UTC timestamps, trimmed/display email, uppercase normalized email, hashed refresh-token storage, ownership, and revocation state.
- Added `IAppDbContext`, `AppDbContext`, EF configurations, PostgreSQL DI registration wired from the API composition root, a design-time context factory, and the initial migration.
- Added real PostgreSQL 17 Testcontainers coverage for normalization, unique normalized email, migration execution, refresh-token ownership/hash persistence, and cascade deletion.

## Files

- `compose.yaml`
- `src/TrackZ.Domain/Identity/User.cs`
- `src/TrackZ.Domain/Identity/RefreshToken.cs`
- `src/TrackZ.Application/Common/Interfaces/IAppDbContext.cs`
- `src/TrackZ.Infrastructure/Persistence/`
- `tests/TrackZ.Infrastructure.Tests/Persistence/`
- Updated project package references and infrastructure DI registration.

## RED → GREEN evidence

1. RED: `dotnet test tests/TrackZ.Infrastructure.Tests --filter User_email_is_unique_case_insensitively --no-restore`
   - Result: expected compile failure: `TrackZ.Domain.Identity` and its model types did not exist.
2. GREEN: the same focused test was rerun after the model, mappings, and real PostgreSQL fixture were added.
   - Result: 1 passed against Testcontainers PostgreSQL 17.
3. RED: `dotnet test tests/TrackZ.Infrastructure.Tests --filter Identity_schema_is_created_from_the_initial_migration --no-restore`
   - Result: expected assertion failure because no migration had been applied.
4. GREEN: `dotnet ef migrations add InitialIdentity --project src/TrackZ.Infrastructure --startup-project src/TrackZ.Api --output-dir Persistence/Migrations --no-build`
   - Result: generated `20260814175047_InitialIdentity` and the model snapshot.
5. GREEN: `dotnet test tests/TrackZ.Infrastructure.Tests --filter IdentityPersistenceTests --no-restore --disable-build-servers`
   - Result: 5 passed against Testcontainers PostgreSQL 17.

## Final verification

- `dotnet test tests/TrackZ.Infrastructure.Tests --no-restore --disable-build-servers`: 8 passed.
- `dotnet test tests/TrackZ.Api.Tests --no-restore --disable-build-servers`: 18 passed.
- `dotnet test tests/TrackZ.Application.Tests --no-restore --disable-build-servers`: 4 passed.
- `dotnet test tests/TrackZ.Domain.Tests --no-restore --disable-build-servers`: 1 passed.
- `dotnet build src/TrackZ.Api/TrackZ.Api.csproj --no-restore --disable-build-servers`: succeeded with 0 warnings and 0 errors.

## Migration evidence

`20260814175047_InitialIdentity` creates `users` and `refresh_tokens`, including the unique `users.NormalizedEmail` index, unique refresh-token hash index, user/session and expiry indexes, and a required cascade FK from refresh tokens to users. The migration-execution test verifies that `InitialIdentity` is recorded as applied.

## Limitations

- The task verification intentionally exercised the non-mobile graph; MAUI was not built because it is unrelated to persistence and identity storage.
- Sandboxed `dotnet build` processes stalled while attempting a network connection despite `--no-restore`; rerunning the same build/test commands with normal host network access completed successfully. Initial direct Docker probing was also denied in the sandbox, while Testcontainers PostgreSQL tests completed successfully with host access.

## Fix Round 1: local API database configuration

- Root cause: `Program` registers Infrastructure, but the actual API development configuration did not provide the `ConnectionStrings:TrackZ` value required when `IAppDbContext` is resolved.
- Added the local-compose connection string only to `src/TrackZ.Api/appsettings.Development.json`; the production base settings remain free of the development database connection.
- Added tests that copy and load the actual API settings files from the test output using `AppContext.BaseDirectory`, resolve `IAppDbContext` without opening a connection, and verify the base configuration has no `TrackZ` connection string.
- RED: `dotnet test tests/TrackZ.Infrastructure.Tests --filter Api_development_configuration_resolves_AppDbContext_without_connecting --no-restore --disable-build-servers`
  - Result: expected `InvalidOperationException`: the TrackZ database connection string was not configured.
- GREEN: `dotnet test tests/TrackZ.Infrastructure.Tests --filter "Api_development_configuration_resolves_AppDbContext_without_connecting|Api_base_configuration_does_not_embed_the_development_database_connection" --no-restore --disable-build-servers`
  - Result: 2 passed.
