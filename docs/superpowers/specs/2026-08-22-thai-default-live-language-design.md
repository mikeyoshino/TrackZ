# TrackZ Thai-Default Live Language Design

Date: 2026-08-22
Status: Proposed for user review

## Objective

TrackZ shall open in Thai on a fresh installation, regardless of the device language. The user can select Thai or English from Profile, and all app-owned user-facing copy changes immediately without clearing authentication, workouts, caches, outbox operations, or preferences such as kg/lb.

The change also establishes an automated localization audit for every shipped mobile page and shared user-facing component.

## User Experience Contract

- With no saved language preference, the app uses `th-TH`.
- Profile contains one native two-option language selector: `ภาษาไทย` and `English`.
- The selected option has the same semantic selected state and minimum 44-point target used by the kg/lb selector.
- Selecting the already-active language is a no-op.
- Selecting another language persists the preference and changes the visible app immediately.
- The rebuilt UI preserves the signed-in account, active workout, locally saved sets, sync outbox, cached exercise catalog, images, progress cache, kg/lb selection, and reduce-motion/haptic settings.
- After a signed-in language change, the new Shell opens the same root tab. Profile therefore remains on the You tab.
- A fresh process uses the persisted choice before any localized page or view model is constructed.
- API exercise names and other server-owned catalog data are not machine-translated. App-owned labels around them are localized.
- Stored identifiers, JSON, SQLite decimal values, API payloads, and signatures remain culture-invariant.

## Chosen Architecture

### 1. Language preference and culture authority

Add a framework-neutral language model with exactly two stable values: Thai and English. An `IAppLanguageStore` persists the choice under one versioned preference key. Absence means Thai; malformed or unsupported stored values fail closed to Thai.

An `IAppLanguageCoordinator` is the sole authority allowed to change application culture. It applies the selected culture to:

- `CultureInfo.CurrentCulture`
- `CultureInfo.CurrentUICulture`
- `CultureInfo.DefaultThreadCurrentCulture`
- `CultureInfo.DefaultThreadCurrentUICulture`

Thai maps to `th-TH`; English maps to `en-US`. The coordinator applies the saved/default value before localized DI registrations are resolved.

### 2. Localized UI composition scope

The current app injects immutable `WorkoutTextSet`, `GamificationTextSet`, and `AuthTextSet` objects, and several pages/view models are singletons. Updating thread culture alone therefore cannot update an already-created UI.

Localized text sets, pages, page view models, `AppShell`, and `AuthShell` move into a disposable localized UI scope. Data, account/session, synchronization, repositories, caches, coordinators, secure token storage, and preference services remain root singletons.

The scope resolves all text sets from the coordinator's current culture. Shell content templates and registered route factories resolve pages from that same scope rather than from the root provider.

`App` owns the active localized scope. On a language change it:

1. captures the current authentication state and root tab;
2. persists the new preference;
3. applies the new culture;
4. creates a complete replacement localized scope and root page;
5. swaps the window root on the UI thread;
6. restores the root tab when signed in;
7. disposes the old UI scope after it is detached.

The root synchronization lifecycle remains running during a signed-in Shell-to-Shell replacement. The replacement must not create a second sync runner or reset the account boundary.

If preference persistence or replacement composition fails, the old scope stays visible, the previous culture/preference is restored, and Profile presents a localized non-destructive error. No partially rebuilt navigation tree is installed.

### 3. Profile presentation

Profile receives an app-language presentation model containing the two localized choices, selected state, accessible descriptions, and an async selection command. The command is serialized to prevent double taps from creating multiple scopes. It is disabled while a switch is in progress.

The language selector appears above the kg/lb selector because language affects the meaning of all following settings. The control uses shared native button/chip resources; it does not introduce page-local colors, radii, or spacing.

### 4. Resource ownership

All app-owned copy must come from one of the typed localized text sets. This includes:

- page and navigation titles;
- tab titles;
- buttons, placeholders, picker prompts, validation and error messages;
- empty/loading/offline/conflict states;
- confirmations and notices;
- accessibility names and descriptions;
- custom-exercise image and library instructions;
- body-part and tracking-mode display labels;
- language-selection labels and failures.

Custom Exercise currently contains hard-coded English XAML and binds raw enum values. It will use typed EN/TH resources and localized option objects while keeping enum values as the stored/API contract.

Technical exception messages used only for programmer invariants, telemetry identifiers, file formats, MIME types, route names, and server-provided exercise names are not UI localization resources.

## Localization Audit

The audit enumerates the explicit shipped-page set and shared user-facing components. It fails when:

- a page silently drops out of the audited manifest;
- a user-facing XAML property contains literal English or Thai copy instead of a binding/resource;
- a UI-facing C# property returns literal app copy outside an explicit technical allowlist;
- the English and Thai resource key sets differ;
- a typed text-set constructor omits or misorders a resource key;
- a page title, tab, action, placeholder, state, dialog, or accessibility description lacks both languages;
- a raw `BodyPart` or `TrackingMode` enum is presented directly to the user;
- a localized UI service is accidentally registered in the root singleton scope;
- a Shell route resolves a page outside the active localized scope.

The audit intentionally allows punctuation-only glyphs, image filenames, route identifiers, numeric format declarations, MIME types, and server-owned exercise names.

## Runtime and Acceptance Tests

Tests will prove:

1. no stored preference plus an English device culture still creates Thai auth and signed-in UI;
2. malformed stored preference falls back to Thai;
3. choosing English changes the current page, tab labels, dialog copy, date/number culture, and accessibility copy without process restart;
4. choosing Thai again reverses the same surfaces;
5. the selection persists across a complete Maui app recreation;
6. account generation, tokens, active workout IDs, locally logged sets, pending outbox operations, caches, kg/lb, and sync-runner identity survive a language switch;
7. concurrent/double selection produces one replacement scope;
8. same-language selection creates no replacement scope;
9. reset/logout racing a switch cannot restore a stale signed-in Shell;
10. storage or composition failure preserves the prior language and UI;
11. old page/view-model instances become collectible after scope disposal;
12. all shipped pages pass the static localization audit in both languages;
13. iOS XAML source generation and simulator compilation succeed.

## Migration and Compatibility

No database or API migration is required. The preference key is new. Existing installations without the key begin in Thai on their next launch. Existing kg/lb and account data are untouched.

Changing language resets only the in-memory navigation stack to its current root tab. Draft and active workout state is already durable and remains available. Modal editors are not automatically reopened after the language switch.

## Out of Scope

- translating server-authored exercise names or user-authored custom names;
- adding languages beyond Thai and English;
- remote language packs;
- backend localization changes;
- changing weight-unit behavior;
- changing existing UI layout except where the Profile language selector requires one new settings row.

## Completion Gate

The work is complete only when the full Mobile test suite, localization audit, Mobile.Core build, iOS XAML compile, and iOS simulator build pass; Android remains subject to the repository's existing SDK availability gate. The app must then be relaunched through `scripts/trackz-dev` for visual verification in both Thai and English.
