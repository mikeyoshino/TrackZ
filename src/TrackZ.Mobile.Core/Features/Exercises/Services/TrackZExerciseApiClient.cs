using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using TrackZ.Contracts.Common;
using TrackZ.Contracts.Errors;
using TrackZ.Contracts.Exercises;

namespace TrackZ.Mobile.Features.Exercises.Services;

public sealed class TrackZExerciseApiClient(HttpClient httpClient) :
    IExerciseCatalogApi,
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
            var page = await response.Content.ReadFromJsonAsync<CursorPage<ExerciseSummaryDto>>(JsonOptions, cancellationToken)
                ?? throw InvalidResponse();
            exercises.AddRange(page.Items);
            cursor = page.NextCursor;
        } while (cursor is not null);
        return exercises;
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
                null),
            JsonOptions,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var created = await response.Content.ReadFromJsonAsync<CreatedExerciseResponse>(JsonOptions, cancellationToken)
            ?? throw InvalidResponse();
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
        var reservation = await response.Content.ReadFromJsonAsync<UploadReservationResponse>(JsonOptions, cancellationToken)
            ?? throw InvalidResponse();
        return new ImageUploadReservation(reservation.UploadId, reservation.UploadUri);
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
        return await response.Content.ReadFromJsonAsync<UploadedExerciseImage>(JsonOptions, cancellationToken)
            ?? throw InvalidResponse();
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<ApiProblemDetails>(JsonOptions, cancellationToken);
            if (problem is not null)
                throw new MobileApiException(problem.ErrorCode, problem.Message, problem.FieldErrors);
        }
        catch (MobileApiException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            throw InvalidResponse(exception);
        }
        throw InvalidResponse();
    }

    private static MobileApiException InvalidResponse(Exception? exception = null) => new(
        BusinessErrorCode.InternalServerError,
        "The server returned an invalid response.",
        innerException: exception);

    private sealed record CreatedExerciseResponse(Guid Id);
    private sealed record UploadReservationResponse(Guid UploadId, Uri UploadUri, DateTimeOffset ExpiresAt);
}
