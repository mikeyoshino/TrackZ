using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using TrackZ.Application.Progress.GetSummary;
using TrackZ.Contracts.Gamification;
using TrackZ.Contracts.Progress;

namespace TrackZ.Api.Tests.Progress;

public sealed class ProgressEndpointTests : IClassFixture<ProgressEndpointTests.ProgressApiFactory>, IDisposable
{
    private readonly ProgressApiFactory _factory;
    private readonly HttpClient _client;

    public ProgressEndpointTests(ProgressApiFactory factory)
    {
        Environment.SetEnvironmentVariable("ConnectionStrings__TrackZ", "Host=localhost;Database=unused;Username=unused;Password=unused");
        Environment.SetEnvironmentVariable("Jwt__Issuer", "trackz-api");
        Environment.SetEnvironmentVariable("Jwt__Audience", "trackz-mobile");
        Environment.SetEnvironmentVariable("Jwt__SigningKey", "test-signing-key-that-is-at-least-thirty-two-bytes-long");
        Environment.SetEnvironmentVariable("Jwt__AccessTokenMinutes", "15");
        Environment.SetEnvironmentVariable("Jwt__RefreshTokenDays", "14");
        _factory = factory;
        _client = factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        Environment.SetEnvironmentVariable("ConnectionStrings__TrackZ", null);
        Environment.SetEnvironmentVariable("Jwt__Issuer", null);
        Environment.SetEnvironmentVariable("Jwt__Audience", null);
        Environment.SetEnvironmentVariable("Jwt__SigningKey", null);
        Environment.SetEnvironmentVariable("Jwt__AccessTokenMinutes", null);
        Environment.SetEnvironmentVariable("Jwt__RefreshTokenDays", null);
    }

    [Fact]
    public async Task Routes_are_auth_first_and_return_user_scoped_projection_dtos()
    {
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await _client.GetAsync("/api/v1/progress/summary")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await _client.GetAsync("/api/v1/gamification/profile")).StatusCode);

        using var summaryRequest = Authorized(HttpMethod.Get, "/api/v1/progress/summary");
        using var profileRequest = Authorized(HttpMethod.Get, "/api/v1/gamification/profile");
        var summaryResponse = await _client.SendAsync(summaryRequest);
        var profileResponse = await _client.SendAsync(profileRequest);
        var summary = await summaryResponse.Content.ReadFromJsonAsync<ProgressSummaryDto>();
        var profile = await profileResponse.Content.ReadFromJsonAsync<GamificationProfileDto>();

        Assert.Equal(HttpStatusCode.OK, summaryResponse.StatusCode);
        Assert.Equal(12500m, summary!.TotalVolumeKg);
        Assert.Equal(12, summary.CompletedWorkouts);
        Assert.Equal(HttpStatusCode.OK, profileResponse.StatusCode);
        Assert.Equal(4, profile!.Level);
        Assert.Equal(2, profile.CurrentStreakWeeks);
        Assert.Equal("streak-4", Assert.Single(profile.Badges).Key);
        Assert.Equal([_factory.UserId, _factory.UserId], _factory.Store.RequestedUsers);
    }

    private HttpRequestMessage Authorized(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _factory.CreateToken());
        return request;
    }

    public sealed class ProgressApiFactory : WebApplicationFactory<Program>
    {
        private const string SigningKey = "test-signing-key-that-is-at-least-thirty-two-bytes-long";
        public Guid UserId { get; } = Guid.Parse("10000000-0000-0000-0000-000000000000");
        public RecordingProgressStore Store { get; } = new();

        public string CreateToken()
        {
            var now = DateTimeOffset.UtcNow;
            var token = new JwtSecurityToken(
                issuer: "trackz-api",
                audience: "trackz-mobile",
                claims: [new Claim(JwtRegisteredClaimNames.Sub, UserId.ToString("D"))],
                notBefore: now.UtcDateTime,
                expires: now.AddMinutes(15).UtcDateTime,
                signingCredentials: new SigningCredentials(
                    new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
                    SecurityAlgorithms.HmacSha256));
            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder) => builder
            .UseEnvironment("Testing")
            .ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:TrackZ"] = "Host=localhost;Database=unused;Username=unused;Password=unused",
                ["Jwt:Issuer"] = "trackz-api",
                ["Jwt:Audience"] = "trackz-mobile",
                ["Jwt:SigningKey"] = SigningKey,
                ["Jwt:AccessTokenMinutes"] = "15",
                ["Jwt:RefreshTokenDays"] = "14"
            }))
            .ConfigureServices(services =>
            {
                services.RemoveAll<IProgressReadStore>();
                services.AddSingleton<IProgressReadStore>(Store);
                services.ReplaceStagingLifecycleWithNoOpForTests();
            });
    }

    public sealed class RecordingProgressStore : IProgressReadStore
    {
        public List<Guid> RequestedUsers { get; } = [];

        public Task<ProgressSummaryDto> GetProgressSummaryAsync(Guid userId, CancellationToken cancellationToken)
        {
            RequestedUsers.Add(userId);
            return Task.FromResult(new ProgressSummaryDto(12500m, 3200m, 12, 4, []));
        }

        public Task<GamificationProfileDto> GetGamificationProfileAsync(Guid userId, CancellationToken cancellationToken)
        {
            RequestedUsers.Add(userId);
            return Task.FromResult(new GamificationProfileDto(
                2750, 4, 1500, 2500, 4, 3, 2, 5,
                [new EarnedBadgeDto("streak-4", "name", "description", "badge-streak-4", DateTimeOffset.UtcNow)],
                []));
        }
    }
}
