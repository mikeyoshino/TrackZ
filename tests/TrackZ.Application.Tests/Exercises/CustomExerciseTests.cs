using TrackZ.Application.Exercises.CreateCustom;
using TrackZ.Application.Exercises.Custom;
using TrackZ.Application.Exercises.DeleteCustom;
using TrackZ.Application.Exercises.ListExercises;
using TrackZ.Application.Exercises.UpdateCustom;
using TrackZ.Application.Common.Exceptions;
using TrackZ.Contracts.Errors;
using TrackZ.Domain.Exercises;

namespace TrackZ.Application.Tests.Exercises;

public sealed class CustomExerciseTests
{
    [Fact]
    public async Task Create_trims_name_and_assigns_the_authenticated_user_as_owner()
    {
        var currentUser = new TestCurrentUser(Guid.NewGuid());
        var store = new InMemoryCustomExerciseStore();
        var handler = new CreateCustomExerciseHandler(store, currentUser);

        var id = await handler.Handle(
            new CreateCustomExerciseCommand("  My Press  ", BodyPart.Chest, TrackingMode.Weighted, null, null),
            CancellationToken.None);

        var exercise = Assert.Single(store.Exercises);
        Assert.Equal(id, exercise.Id);
        Assert.Equal(currentUser.UserId, exercise.OwnerId);
        Assert.Equal("My Press", exercise.Name);
        Assert.Equal(BodyPart.Chest, exercise.BodyPart);
        Assert.Equal(TrackingMode.Weighted, exercise.TrackingMode);
    }

    [Fact]
    public async Task Create_maps_an_active_owner_name_conflict_to_the_stable_duplicate_error()
    {
        var ownerId = Guid.NewGuid();
        var store = new InMemoryCustomExerciseStore { RejectCreates = true };
        var handler = new CreateCustomExerciseHandler(store, new TestCurrentUser(ownerId));

        var error = await Assert.ThrowsAsync<BusinessException>(() => handler.Handle(
            new CreateCustomExerciseCommand("My Press", BodyPart.Chest, TrackingMode.Weighted, null, null),
            CancellationToken.None));

        Assert.Equal(BusinessErrorCode.ExerciseNameDuplicate, error.Code);
        Assert.Equal(409, error.StatusCode);
    }

    [Fact]
    public async Task Create_rejects_unverified_image_identifiers_instead_of_trusting_them()
    {
        var store = new InMemoryCustomExerciseStore();
        var handler = new CreateCustomExerciseHandler(store, new TestCurrentUser(Guid.NewGuid()));

        var error = await Assert.ThrowsAsync<BusinessException>(() => handler.Handle(
            new CreateCustomExerciseCommand("My Press", BodyPart.Chest, TrackingMode.Weighted, Guid.NewGuid(), "https://untrusted.example/image.jpg"),
            CancellationToken.None));

        Assert.Equal(BusinessErrorCode.InvalidRequest, error.Code);
        Assert.Empty(store.Exercises);
    }

    [Theory]
    [InlineData("", BodyPart.Chest, TrackingMode.Weighted)]
    [InlineData("xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx", BodyPart.Chest, TrackingMode.Weighted)]
    [InlineData("My Press", (BodyPart)99, TrackingMode.Weighted)]
    [InlineData("My Press", BodyPart.Chest, (TrackingMode)99)]
    public async Task Create_maps_malformed_domain_values_to_invalid_request(string name, BodyPart bodyPart, TrackingMode trackingMode)
    {
        var handler = new CreateCustomExerciseHandler(new InMemoryCustomExerciseStore(), new TestCurrentUser(Guid.NewGuid()));

        var error = await Assert.ThrowsAsync<BusinessException>(() => handler.Handle(
            new CreateCustomExerciseCommand(name, bodyPart, trackingMode, null, null), CancellationToken.None));

        Assert.Equal(BusinessErrorCode.InvalidRequest, error.Code);
        Assert.Equal(400, error.StatusCode);
    }

    [Fact]
    public async Task Update_changes_the_owners_name_and_body_part_without_changing_mode_when_omitted()
    {
        var ownerId = Guid.NewGuid();
        var exercise = ExerciseDefinition.CreateCustom(ownerId, "Old Press", BodyPart.Chest, TrackingMode.Weighted);
        var store = new InMemoryCustomExerciseStore(exercise);
        var handler = new UpdateCustomExerciseHandler(store, new TestCurrentUser(ownerId));

        await handler.Handle(new UpdateCustomExerciseCommand(exercise.Id, "  Renamed Press ", BodyPart.Back, null, null, null), CancellationToken.None);

        Assert.Equal("Renamed Press", exercise.Name);
        Assert.Equal(BodyPart.Back, exercise.BodyPart);
        Assert.Equal(TrackingMode.Weighted, exercise.TrackingMode);
    }

    [Fact]
    public async Task Update_hides_another_users_exercise_as_not_found()
    {
        var exercise = ExerciseDefinition.CreateCustom(Guid.NewGuid(), "Private Press", BodyPart.Chest, TrackingMode.Weighted);
        var handler = new UpdateCustomExerciseHandler(new InMemoryCustomExerciseStore(exercise), new TestCurrentUser(Guid.NewGuid()));

        var error = await Assert.ThrowsAsync<BusinessException>(() => handler.Handle(
            new UpdateCustomExerciseCommand(exercise.Id, "Renamed", BodyPart.Back, null, null, null), CancellationToken.None));

        Assert.Equal(BusinessErrorCode.ExerciseNotFound, error.Code);
        Assert.Equal(404, error.StatusCode);
    }

    [Fact]
    public async Task Update_rejects_tracking_mode_change_after_history_without_losing_other_fields()
    {
        var ownerId = Guid.NewGuid();
        var exercise = ExerciseDefinition.CreateCustom(ownerId, "My Press", BodyPart.Chest, TrackingMode.Weighted);
        exercise.RecordSetHistory();
        var handler = new UpdateCustomExerciseHandler(new InMemoryCustomExerciseStore(exercise), new TestCurrentUser(ownerId));

        var error = await Assert.ThrowsAsync<BusinessException>(() => handler.Handle(
            new UpdateCustomExerciseCommand(exercise.Id, "Renamed", BodyPart.Back, TrackingMode.Bodyweight, null, null), CancellationToken.None));

        Assert.Equal(BusinessErrorCode.InvalidRequest, error.Code);
        Assert.Equal("My Press", exercise.Name);
        Assert.Equal(BodyPart.Chest, exercise.BodyPart);
        Assert.Equal(TrackingMode.Weighted, exercise.TrackingMode);
    }

    [Fact]
    public async Task Delete_archives_the_owners_exercise_without_removing_it()
    {
        var ownerId = Guid.NewGuid();
        var exercise = ExerciseDefinition.CreateCustom(ownerId, "My Press", BodyPart.Chest, TrackingMode.Weighted);
        var store = new InMemoryCustomExerciseStore(exercise);
        var handler = new DeleteCustomExerciseHandler(store, new TestCurrentUser(ownerId));

        await handler.Handle(new DeleteCustomExerciseCommand(exercise.Id), CancellationToken.None);

        Assert.True(exercise.IsArchived);
        Assert.Contains(exercise, store.Exercises);
    }

    private sealed class TestCurrentUser(Guid userId) : ICurrentUser
    {
        public Guid UserId => userId;
    }

    private sealed class InMemoryCustomExerciseStore(params ExerciseDefinition[] exercises) : ICustomExerciseStore
    {
        public List<ExerciseDefinition> Exercises { get; } = [.. exercises];
        public bool RejectCreates { get; init; }

        public Task<bool> TryCreateCustomAsync(ExerciseDefinition exercise, CancellationToken cancellationToken)
        {
            if (RejectCreates) return Task.FromResult(false);
            Exercises.Add(exercise);
            return Task.FromResult(true);
        }

        public Task<ExerciseDefinition?> FindActiveCustomOwnedAsync(Guid exerciseId, Guid ownerId, CancellationToken cancellationToken) =>
            Task.FromResult(Exercises.SingleOrDefault(exercise =>
                exercise.Id == exerciseId && exercise.OwnerId == ownerId && exercise.IsCustom && !exercise.IsArchived));

        public Task<bool> TrySaveCustomAsync(CancellationToken cancellationToken) => Task.FromResult(true);
    }
}
