# Preflight Task 0 Report — Restore serialized headless UI dispatch

Date: 2026-08-22

## Root-cause trace

`ExercisePickerViewModel.RefreshAsync` uses `Parallel.ForEachAsync` with four thumbnail workers. Each worker calls `IUiDispatcher.InvokeAsync(Action)` while updating visible artwork state. The default `InlineUiDispatcher` invoked the action immediately without serialization, so parallel workers concurrently mutated the UI-owned `_artworkStates` dictionary in `SetVisibleArtworkState` / `SetVisibleArtworkReady`. The fix adds a private `SemaphoreSlim(1, 1)` gate to `InlineUiDispatcher`; every action waits for the gate, runs inside `try`, and releases it in `finally`. This preserves the original action exception while ensuring a later action can acquire the gate.

The approved persistent visual reference exists at `docs/design/momentum-home-reference.html`; it was opened for this task and no Home UI files were changed.

## TDD evidence

The pre-existing RED command was run before the production change:

```text
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --filter 'FullyQualifiedName~ExercisePickerViewModelTests.Thumbnail_fill_uses_bounded_parallelism' --verbosity minimal -m:1
Passed: 1, Failed: 0, Skipped: 0, Total: 1
```

That known baseline race did not reproduce in this environment during the preflight run. The required focused dispatcher test was added before the production change and produced the deterministic RED:

```text
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --filter 'FullyQualifiedName~ExercisePickerViewModelTests.Inline_ui_dispatcher_serializes_actions_and_releases_gate_after_throw' --verbosity minimal -m:1
Failed: 1, Passed: 0, Skipped: 0, Total: 1
Error: Assert.False() failure; the second action completed immediately with the old dispatcher.
```

The focused test proves maximum concurrent action count is exactly one and verifies that an exception does not retain the gate: a later action completes after the throwing action.

## GREEN commands and counts

```text
dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --filter 'FullyQualifiedName~ExercisePickerViewModelTests.Inline_ui_dispatcher_serializes_actions_and_releases_gate_after_throw' --verbosity minimal -m:1
Passed: 1, Failed: 0, Skipped: 0, Total: 1

dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --filter 'FullyQualifiedName~ExercisePickerViewModelTests.Thumbnail_fill_uses_bounded_parallelism' --verbosity minimal -m:1
Passed: 1, Failed: 0, Skipped: 0, Total: 1

dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --filter 'FullyQualifiedName~TrackZ.Mobile.Tests.Exercises' --verbosity minimal -m:1
Passed: 120, Failed: 0, Skipped: 0, Total: 120

dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore --verbosity minimal -m:1
Passed: 530, Failed: 0, Skipped: 0, Total: 530
```

`git diff --check` completed cleanly.

## Changed files

- `src/TrackZ.Mobile.Core/Features/Exercises/ExerciseServices.cs`: serialize `InlineUiDispatcher.InvokeAsync(Action)` with a private semaphore and `finally` release.
- `tests/TrackZ.Mobile.Tests/Exercises/ExercisePickerViewModelTests.cs`: add the behavior-sensitive serialization/exception-release test.
- `.superpowers/sdd/2026-08-22-trackz-momentum-home/task-0-report.md`: this report.

No `ExercisePickerViewModel`, thumbnail parallelism, collection, Home, API, Docker, container, or Simulator files were changed.

## Self-review

- `IUiDispatcher` signature is unchanged.
- The gate is private and has capacity one.
- The action runs only after successful gate acquisition.
- `finally` releases the gate when the action throws, and the original exception is propagated by the async method.
- Existing bounded thumbnail parallelism remains unchanged; only UI action dispatch is serialized.
- The added test exercises real `InlineUiDispatcher` behavior and checks both concurrency and exception release.
- Unrelated dirty files were preserved and are not staged.

## Service/process audit

No API, Docker Compose, container, or iOS Simulator was started. A process audit after testing found no TrackZ API, Docker Compose/container, or Simulator process. Existing unrelated development/editor processes were left untouched.

## Commit

The implementation, focused test, and this report are committed together; the final commit SHA is supplied in the task handoff.

## Concerns

The brief and progress ledger identify `Thumbnail_fill_uses_bounded_parallelism` as a deterministic baseline RED, but it passed before the fix in this environment. The new dispatcher test reproduced the underlying contract failure deterministically, and the original test plus the full Mobile suite pass after the fix. No other concerns.
