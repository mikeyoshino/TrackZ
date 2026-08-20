using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Microsoft.EntityFrameworkCore;
using TrackZ.Application.Common.Interfaces;
using TrackZ.Infrastructure.Identity;
using TrackZ.Infrastructure.Persistence;
using TrackZ.Application.Exercises.ListExercises;
using TrackZ.Infrastructure.Exercises;
using TrackZ.Application.Exercises.Custom;
using TrackZ.Application.Media;
using TrackZ.Infrastructure.Media;
using TrackZ.Infrastructure.Persistence.Seed;
using TrackZ.Application.Workouts;
using TrackZ.Infrastructure.Workouts;
using TrackZ.Application.Sync;
using TrackZ.Application.Sync.Pull;
using TrackZ.Infrastructure.Sync;
using TrackZ.Application.Progress.RebuildExercisePerformance;
using TrackZ.Application.Gamification.ReconcileWorkoutXp;
using TrackZ.Application.Gamification.EvaluateStreak;
using TrackZ.Application.Gamification.EvaluateBadges;
using TrackZ.Application.Progress.GetSummary;
using TrackZ.Infrastructure.Progress;

namespace TrackZ.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("TrackZ")
                ?? throw new InvalidOperationException("The TrackZ database connection string is not configured.")));

        services.AddScoped<IAppDbContext>(provider => provider.GetRequiredService<AppDbContext>());
        services.AddScoped<IExerciseCatalogReadStore>(provider => provider.GetRequiredService<AppDbContext>());
        services.AddScoped<ICustomExerciseStore>(provider => provider.GetRequiredService<AppDbContext>());
        services.AddScoped<IExerciseImageUploadStore>(provider => provider.GetRequiredService<AppDbContext>());
        services.AddScoped<IWorkoutReadStore>(provider => provider.GetRequiredService<AppDbContext>());
        services.AddScoped<ISyncPushStore>(provider => provider.GetRequiredService<AppDbContext>());
        services.AddScoped<IExercisePerformanceRebuildStore>(provider => provider.GetRequiredService<AppDbContext>());
        services.AddScoped<IGamificationStore>(provider => provider.GetRequiredService<AppDbContext>());
        services.AddScoped<IStreakStore>(provider => provider.GetRequiredService<AppDbContext>());
        services.AddScoped<IBadgeStore>(provider => provider.GetRequiredService<AppDbContext>());
        services.AddScoped<IProgressReadStore, ProgressReadStore>();
        services.AddScoped<ISyncPullStore>(provider => provider.GetRequiredService<AppDbContext>());
        services.AddSingleton<ISyncCursorCodec, HmacSyncCursorCodec>();
        services.AddSingleton<ObjectStorage>();
        services.AddSingleton<IObjectStorage>(provider => provider.GetRequiredService<ObjectStorage>());
        services.AddSingleton<IStagingObjectLifecycle>(provider => provider.GetRequiredService<ObjectStorage>());
        services.AddHostedService<StagingObjectLifecycleService>();
        services.AddSingleton<IImageProcessor, ImageProcessor>();
        services.AddScoped<ObjectStorageExerciseCatalogAssetDeployment>();
        services.AddScoped<IExerciseCatalogAssetDeployment>(provider =>
            provider.GetRequiredService<ObjectStorageExerciseCatalogAssetDeployment>());
        services.AddScoped<ExerciseCatalogSeeder>();
        services.AddScoped<LevelThresholdSeeder>();
        services.AddScoped<BadgeDefinitionSeeder>();
        services.AddScoped<ExerciseCatalogDeploymentService>();
        services.AddSingleton(TimeProvider.System);
        services.AddOptions<MediaAccessOptions>()
            .Bind(configuration.GetSection(MediaAccessOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(static options => options.IsValid(), "Media access settings must provide a clean HTTP(S) origin, a 32-character signing key, and a 15-300 second lifetime.")
            .ValidateOnStart();
        services.AddSingleton<IMediaAccessUrlSigner, SignedMediaAccessUrlSigner>();
        services.AddOptions<ImageUploadCleanupOptions>().Bind(configuration.GetSection(ImageUploadCleanupOptions.SectionName));
        services.AddHostedService<ImageUploadCleanupService>();
        services.AddOptions<ObjectStorageOptions>()
            .Bind(configuration.GetSection(ObjectStorageOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(static options => options.IsValid(), "Object storage settings must provide an HTTP(S) service URL, DNS-compatible bucket, access key, and secret key.")
            .ValidateOnStart();
        services.AddSingleton<IExerciseCursorCodec, HmacExerciseCursorCodec>();
        services.AddOptions<WorkoutCursorOptions>()
            .Bind(configuration.GetSection(WorkoutCursorOptions.SectionName))
            .Validate(static options => options.IsValid(), "Workout cursor lifetime must be between 5 and 1440 minutes.")
            .ValidateOnStart();
        services.AddSingleton<IWorkoutCursorCodec, HmacWorkoutCursorCodec>();
        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddSingleton<ITokenService, JwtTokenService>();

        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .Validate(static options => options.IsValid(), "Jwt settings must provide issuer, audience, a 32-character signing key, and valid UTC lifetimes.")
            .ValidateOnStart();

        var jwt = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
        if (!jwt.IsValid())
        {
            throw new InvalidOperationException("Jwt settings must provide issuer, audience, a 32-character signing key, and valid UTC lifetimes.");
        }

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero
                };
            });

        return services;
    }
}
