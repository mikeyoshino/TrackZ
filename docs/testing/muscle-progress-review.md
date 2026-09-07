# Muscle progress: implementation and visual review

Approved direction: overview → muscle group → exercise evidence. Implemented on the Progress tab with the existing dark/lime palette, Thai typography, body icons and native bottom navigation.

## Behavior

- Four-week, twelve-week and custom inclusive date ranges, using the device's local date. Custom ranges are limited to one year. Returning from a drilldown preserves the selected range and muscle group.
- Only completed workouts contribute to progress. Warm-ups and unclassified sets are not counted as working sets. The report discloses unclassified volume.
- Compare the earliest comparable observation in the selected period with the latest session for the same exercise, tracking mode and working-set count. Same load compares minimum reps across working sets. More weight / less assistance requires maintained or increased reps. A change to both load and reps that cannot be compared is not reported as improvement.
- Unknown or mixed-load latest sessions do not silently reuse an older positive result. One observation is insufficient. The trend requires four comparable observations, uses real date spacing and never connects different loads.
- Form check-ins are device-local, tied to the original session and last set. They preserve effort/pain information, do not accept a progression target, and do not authorize a load increase. An edited/deleted session is reloaded before saving. Account-reset boundaries fence reads and writes.
- Historical deep links open the requested exercise once and expand the period when needed. Loading, retry, empty history and insufficient-data states are implemented.

## Simulator evidence

Device: iPhone 17e, iOS 26.5, 390-point portrait viewport. Screenshots are in `artifacts/progress-review/` (local artifacts, not tracked in git).

- `overview.png`: six body areas, compact single-selection date control, state plus explanatory text.
- `area.png`: three exercises and weekly volume visible above the existing tab bar.
- `detail.png`: equipment label, centered before/after values, arrow, four-point chart, check-in and log link.
- `check-in.png`: actual tap selects only one answer and displays saved feedback.
- `back-to-area.png`: actual back-button tap returns to the same body group.
- `twelve-weeks.png`: actual date-control tap updates the active segment and the displayed range.
- `custom-dates.png`: actual date-control tap opens labeled start/end fields and Apply.
- `empty.png`: no invented progress when no sessions exist.
- `large-text.png` / `large-text-fixed.png`: largest accessibility text originally truncated the horizontal date control; it now switches to vertically stacked controls. The rest of the page remains scrollable. Simulator text size was restored to its original `large` setting.

Screenshots used an in-memory fixture matching the approved mock, rendered by the production page and components. No sample workouts were inserted into the user's storage or sent to the API. The temporary preview launch hook was removed before the final build. A copy of the fixture is retained only beside the local review artifacts.

## Comparison with mock

The first visual pass found oversized separate period buttons, excessively tall exercise cards, missing before/after arrow, and white improvement labels. These were corrected in subsequent native screenshots. The final default layout retains the mock's three-level information hierarchy, compact cards, charcoal surfaces, lime indicators and restrained bottom actions.

Intentional differences: existing TrackZ body icons and native tab bar are retained; chart ticks use compact local numeric dates; real data controls wording, counts and insufficient-data states. Accessibility layouts reflow vertically. This is a visual implementation match, not a claim of pixel-identical output from the generated bitmap across all devices.

## Validation

- `dotnet test tests/TrackZ.Mobile.Tests/TrackZ.Mobile.Tests.csproj --no-restore -m:1 -v quiet`
- `dotnet build src/TrackZ.Mobile/TrackZ.Mobile.csproj -f net10.0-ios -r iossimulator-arm64 --no-restore -m:1 -v quiet`
- `git diff --check`

Coverage includes comparison arithmetic, same-load/set-count requirements, assisted direction, active workout exclusion, local date boundaries, missing/unknown data, durable check-ins, deep links, return navigation, retries and account-reset cancellation.

Final verification after removing the temporary fixture: **948 tests passed**, iOS simulator build passed with **0 warnings / 0 errors**, and `git diff --check` passed. The production build was installed on the same simulator; the preview app was closed.

Not claimed: a live backend walkthrough, Android runtime verification, VoiceOver walkthrough, or a complete device/landscape matrix. Existing API/Docker services were not needed for the in-memory visual review.
