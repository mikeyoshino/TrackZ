using MediatR;
using TrackZ.Contracts.Common;
using TrackZ.Contracts.Exercises;
using TrackZ.Domain.Exercises;

namespace TrackZ.Application.Exercises.ListExercises;

public sealed record ListExercisesQuery(
    BodyPart? BodyPart,
    string? Search,
    string? Cursor,
    int PageSize = 30) : IRequest<CursorPage<ExerciseSummaryDto>>;
