using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using TrackZ.Contracts.Common;
using TrackZ.Contracts.Errors;
using TrackZ.Contracts.Exercises;
using TrackZ.Contracts.Workouts;

namespace TrackZ.Mobile.Features.Exercises.Services;

public sealed class TrackZExerciseApiClient(HttpClient httpClient) :
    IExerciseCatalogApi,
    IExerciseHistoryApi,
    ICustomExerciseApi,
    IExerciseImageApi
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<ExerciseSummaryDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var exercises = new List<ExerciseSummaryDto>();
        string? cursor = null;
        do
        {
            var path = "/api/v1/exercises?pageSize=50" +
                (cursor is null ? string.Empty : $"&cursor={Uri.EscapeDataString(cursor)}");
            using var response = await httpClient.GetAsync(path, cancellationToken);
            await EnsureSuccessAsync(response, cancellationToken);
            var page = await ReadSuccessAsync<CursorPage<ExerciseSummaryDto>>(
                response,
                value => value.Items is not null,
                cancellationToken);
            exercises.AddRange(page.Items);
            cursor = page.NextCursor;
        } while (cursor is not null);
        return exercises;
    }

    public async Task<ExerciseHistorySessionDto?> GetMostRecentAsync(
        Guid exerciseId,
        CancellationToken cancellationToken = default)
    {
        if (exerciseId == Guid.Empty)
            throw new ArgumentException("Exercise ID is required.", nameof(exerciseId));
        using var response = await httpClient.GetAsync(
            $"/api/v1/exercises/{exerciseId:D}/history?pageSize=1", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var page = await ReadSuccessAsync<CursorPage<ExerciseHistorySessionDto>>(
            response,
            value => value.Items is not null && value.Items.Count <= 1,
            cancellationToken);
        return page.Items.SingleOrDefault();
    }

    public async Task<Guid> CreateAsync(CustomExerciseDraft exercise, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync(
            "/api/v1/exercises/custom",
            new CreateCustomExerciseRequest(
                exercise.Name,
                exercise.BodyPart,
                exercise.TrackingMode,
                exercise.LibraryImageId,
                null,
                exercise.OperationId,
                exercise.LocalExerciseId),
            JsonOptions,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var created = await ReadSuccessAsync<CreatedExerciseResponse>(
            response,
            value => value.Id != Guid.Empty,
            cancellationToken);
        return created.Id;
    }

    public async Task UpdateAsync(Guid exerciseId, CustomExerciseDraft exercise, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PutAsJsonAsync(
            $"/api/v1/exercises/custom/{exerciseId:D}",
            new UpdateCustomExerciseRequest(
                exercise.Name,
                exercise.BodyPart,
                exercise.TrackingMode,
                exercise.LibraryImageId,
                null),
            JsonOptions,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<ImageUploadReservation> RequestUploadAsync(
        Guid exerciseId,
        string contentType,
        long length,
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync(
            "/api/v1/media/exercise-images/uploads",
            new RequestImageUploadRequest(exerciseId, contentType, length),
            JsonOptions,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var reservation = await ReadSuccessAsync<UploadReservationResponse>(
            response,
            value => value.UploadId != Guid.Empty
                && value.UploadUri is not null
                && IsUploadContentRoute(value.UploadUri, value.UploadId)
                && value.ExpiresAt > DateTimeOffset.UnixEpoch,
            cancellationToken);
        return new ImageUploadReservation(
            reservation.UploadId,
            reservation.UploadUri,
            reservation.ExpiresAt.ToUniversalTime());
    }

    public async Task UploadContentAsync(
        Uri uploadUri,
        Stream original,
        string contentType,
        long length,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, uploadUri);
        request.Content = new StreamContent(original);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        request.Content.Headers.ContentLength = length;
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<UploadedExerciseImage> CompleteUploadAsync(Guid uploadId, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsync(
            $"/api/v1/media/exercise-images/uploads/{uploadId:D}/complete",
            null,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await ReadSuccessAsync<UploadedExerciseImage>(
            response,
            value => value.Id != Guid.Empty
                && IsCompletedImageRoute(value.MasterUrl, value.Id, "master")
                && IsCompletedImageRoute(value.ThumbnailUrl, value.Id, "thumbnail"),
            cancellationToken);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        var authenticationRequired = response.StatusCode is
            System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden;
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<ApiProblemDetails>(JsonOptions, cancellationToken);
            if (problem is not null)
                throw new MobileApiException(
                    problem.ErrorCode,
                    problem.Message,
                    problem.FieldErrors,
                    isRetryable: problem.ErrorCode == BusinessErrorCode.VersionConflict
                        || (int)response.StatusCode >= 500
                        || response.StatusCode is System.Net.HttpStatusCode.RequestTimeout
                        or System.Net.HttpStatusCode.TooManyRequests,
                    isAuthenticationRequired: authenticationRequired);
        }
        catch (MobileApiException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            if (authenticationRequired)
                throw AuthenticationRequired(exception);
            throw InvalidResponse(exception);
        }
        if (authenticationRequired) throw AuthenticationRequired();
        throw InvalidResponse();
    }

    private static async Task<T> ReadSuccessAsync<T>(
        HttpResponseMessage response,
        Func<T, bool> isValid,
        CancellationToken cancellationToken) where T : class
    {
        try
        {
            var value = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
            return value is not null && isValid(value) ? value : throw InvalidResponse();
        }
        catch (MobileApiException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            throw InvalidResponse(exception);
        }
    }

    private static bool IsUploadContentRoute(Uri route, Guid uploadId) =>
        !route.IsAbsoluteUri
        && string.Equals(
            route.OriginalString,
            $"/api/v1/media/exercise-images/uploads/{uploadId:D}/content",
            StringComparison.Ordinal);

    private static bool IsCompletedImageRoute(string? route, Guid imageId, string rendition) =>
        string.Equals(
            route,
            $"/api/v1/media/exercise-images/{imageId:D}/{rendition}",
            StringComparison.Ordinal);

    private static MobileApiException InvalidResponse(Exception? exception = null) => new(
        BusinessErrorCode.InternalServerError,
        "The server returned an invalid response.",
        innerException: exception,
        isRetryable: true);

    private static MobileApiException AuthenticationRequired(Exception? exception = null) => new(
        BusinessErrorCode.InvalidRequest,
        "Authentication is required.",
        innerException: exception,
        isAuthenticationRequired: true);

    private sealed record CreatedExerciseResponse(Guid Id);
    private sealed record UploadReservationResponse(Guid UploadId, Uri UploadUri, DateTimeOffset ExpiresAt);
}
