using MediatR;

namespace TrackZ.Application.Exercises.DeleteCustom;

public sealed record DeleteCustomExerciseCommand(Guid ExerciseId) : IRequest;
