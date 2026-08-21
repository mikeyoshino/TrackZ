# TrackZ Native iOS Experience Design

**Date:** 2026-08-20
**Status:** Approved in collaborative visual design review
**Primary platform:** iOS
**Presentation stack:** .NET MAUI XAML with narrowly scoped iOS-native adapters
**Approved direction:** Native Performance

## 1. Purpose and Relationship to the Product Design

This specification replaces the presentation, navigation, and interaction-design portions of the original TrackZ mobile design. The existing product rules, workout domain, offline-first persistence, sync protocol, exercise catalog, progress calculations, gamification rules, API contracts, and business error codes remain authoritative unless this document explicitly changes their presentation.

The current mobile application is functionally native but visually behaves like a responsive web dashboard: generic cards, full-width buttons, repeated grids, and weak iOS navigation hierarchy. The redesign must make TrackZ feel designed for an iPhone rather than merely rendered on one.

TrackZ remains a tracker for experienced lifters. It does not prescribe exercises or target weights. It surfaces the user's own previous performance and makes logging fast.

## 2. Approved Experience Principles

### 2.1 Native Performance

The approved visual direction combines native iOS structure with TrackZ's dark performance identity:

- system navigation, tabs, sheets, gestures, typography, safe areas, and accessibility behavior;
- charcoal surfaces with restrained lime emphasis;
- high-contrast performance numbers;
- exercise artwork as useful content rather than decoration;
- game energy expressed through motion, haptics, progress reveals, XP, streaks, and earned badges;
- no cartoon game interface, fake currencies, noisy HUD, WebView, or responsive-web layout patterns.

### 2.2 Fast During Training

The interface must be usable between sets with one hand. The primary action stays within thumb reach. Weight and repetition controls use large targets. The previous session remains visible without requiring navigation. A user can match the last set without retyping it.

### 2.3 Reward the Work, Then Get Out of the Way

Game elements appear at meaningful moments:

- a set is durably saved;
- a personal record is achieved;
- a workout is completed;
- a consistency milestone or badge is earned;
- long-term performance improves.

Game elements must not dominate the exercise picker or slow repeated logging.

## 3. Platform Strategy

iOS is the primary polished experience for this release. Shared state, business logic, API clients, sync, SQLite persistence, localization, and view models remain in `TrackZ.Mobile.Core`.

Presentation remains MAUI XAML. Platform-specific behavior is permitted only behind small interfaces when MAUI does not expose the required native behavior, including:

- iOS sheet presentation and detents;
- native haptic patterns;
- system Reduce Motion observation;
- navigation-bar appearance and large titles;
- animation cancellation coordinated with the account/session boundary.

These interfaces must allow a later Android Material presentation without forcing iOS metaphors onto Android. This release does not require Android visual parity, but it must not break the shared Android-capable core.

## 4. Navigation Architecture

The authenticated root is a native four-tab hierarchy:

1. **Train** — today's entry point, body-area selection, exercise selection, active workout, and summary.
2. **History** — completed workouts, workout detail, historical edits, and deletion.
3. **Progress** — exercise charts, PRs, XP, level, streak, and badges.
4. **You** — account, language, units, sync state, accessibility, and motion preferences.

Each tab owns an independent navigation stack. Native back gestures and navigation-bar behavior must work without custom replacement arrows. The active workout may be resumed from Train after process termination.

Large titles are used only at tab roots. Detail pages use compact navigation titles so exercise content remains visible.

## 5. Primary Workout Flow

The approved flow has four stages.

### 5.1 Today

Train opens with the question **"What are you training today?"** It shows:

- a single primary **Choose workout** action;
- the active workout, when one exists;
- recent body-area sessions as shortcuts;
- a quiet offline/sync state when relevant;
- no prescribed program or recommended exercise list.

### 5.2 Body-Area Sheet

Choosing a workout opens a native iOS sheet with Chest, Back, Shoulders, Arms, Legs, and Core. The user may choose a starting area, then add exercises from other areas in the next step. The sheet uses native detents, drag indicator, dismissal behavior, and accessibility focus.

### 5.3 Exercise Library

The exercise picker is a pushed iOS library screen, not a web-like filter form. It contains:

- native search;
- horizontally scrolling filter chips for body area and equipment;
- one row per exercise with API artwork, name, equipment/body area, LAST, PR, and selection control;
- a bottom selection bar that shows the selected count and **Add to workout**;
- native context actions for editing a user-owned custom exercise;
- loading, empty, offline, and isolated image-failure states.

Search and body-area filtering operate on the cached catalog immediately. A background refresh reconciles server changes without reordering or clearing the user's current selection.

### 5.4 Active Workout

The active screen shows session time, exercise order, artwork, LAST context, and the number of sets logged for each exercise. It must not display a prescribed set target, required-set progress, or an exercise-completion percentage. The user can add, remove, and reorder exercises while the workout is active. Native swipe/context actions are used for secondary operations. The user chooses an exercise to continue logging and explicitly finishes the workout when the existing domain rules permit it.

## 6. Set Logger

The set logger is the core repeated interaction.

### 6.1 Exercise Context

The header contains the API exercise artwork, exercise name, body area/equipment, and session timer. It shows:

- the exact ordered sets from the previous completed session;
- today's saved sets;
- the previous-session best and the all-time PR;
- tracking-mode-aware terminology for weighted, bodyweight, and assisted exercises.

### 6.2 Set Entry Sheet

Tapping **+ Add set** expands one inline editor beneath the previous-versus-today comparison. The editor is scrolled into view and announced for assistive technology. It provides:

- the next set number;
- a **Match last set** shortcut;
- large weight/assistance and repetition steppers;
- direct numeric keyboard entry when the displayed value is tapped;
- the shared kg/lb preference while preserving canonical kilograms in persistence and API contracts;
- one sticky primary **Save set** action in the same thumb position as **+ Add set**.

Add and Save use phase-stable commands, so a rapid second Add event cannot become a Save. Cancel restores the durable suggested measurement and collapses the editor without writing SQLite or the outbox. A successful Save persists first, appends the set to TODAY, then collapses the editor and runs feedback/sync.

Bodyweight exercises omit weight. Assisted exercises clearly label assistance and visually communicate that lower assistance represents progress.

### 6.3 Durable Feedback Boundary

Feedback occurs only after the set and its outbox operation commit atomically to SQLite. A successful normal set receives a light haptic and short row transition. A matched result receives restrained acknowledgement. A PR receives a sub-one-second spring/glow reveal and XP summary.

Animation completion is never a business-state dependency. Navigation, account reset, disposal, and Reduce Motion can cancel visual feedback safely.

Saved sets support native swipe/context actions for edit and delete.

## 7. Motivation and Progress

### 7.1 Workout Completion

The summary reveals, in order:

1. completed status and duration;
2. set, repetition, and volume totals;
3. PRs;
4. XP and level movement;
5. streak or badge changes.

The full reveal remains interruptible and does not exceed approximately 1.2 seconds. The user may dismiss immediately.

### 7.2 XP and Levels

XP is derived from durable server/domain events and remains idempotent. The UI may show explicitly provisional offline progress but replaces it with authoritative results after sync.

### 7.3 Streaks

Streaks reward consistency against the user's weekly goal. Missing a single day does not create a punitive reset because the product tracks weekly training consistency, not daily app opens.

### 7.4 Badges

Badges represent real accomplishments such as first workout, first PR, session counts, progressive overload, and streak milestones. Locked badges may appear in Progress, but everyday training does not show badge clutter.

## 8. Visual System

### 8.1 Typography

iOS uses the system font and Dynamic Type. OpenSans is removed from the iOS presentation. Typography roles are semantic rather than page-specific:

- large navigation title;
- page title;
- section title;
- body;
- secondary/caption;
- performance number;
- compact label.

Weight, reps, time, XP, and volume may use tabular numerals where supported.

### 8.2 Color and Materials

- Background: near-black charcoal, not pure black on every surface.
- Primary action and positive performance: lime.
- Informational context: restrained system blue.
- Warning/conflict: amber.
- Destructive/error: semantic red.
- Secondary content: accessible cool gray.

Cards are used only when grouping improves comprehension. Borders and rounded rectangles must not wrap every element. Native list separation, whitespace, and material hierarchy replace most existing card chrome.

Semantic colors must support dark-mode contrast and may adapt to system accessibility settings. The approved release is dark-first; light mode is not required to mimic the dark brand palette.

### 8.3 Touch and Layout

- minimum interactive target: 44 by 44 points;
- primary actions remain reachable near the bottom safe area;
- no desktop-style multi-column controls on iPhone;
- layouts must support the smallest supported iPhone width and larger Pro Max sizes;
- Dynamic Type must not clip essential actions or performance values.

## 9. Motion and Haptics

Motion is centralized through presentation services/behaviors rather than scattered delays in pages.

- press feedback: subtle scale/spring response;
- navigation: native iOS push/pop;
- sheets: native detent transition;
- saved set: haptic plus short row/pulse transition;
- PR: short glow/spring and optional stronger success haptic;
- summary: brief numeric and progress reveal;
- tab changes and search results: no decorative animation.

Reduce Motion replaces transforms and count-ups with fades or immediate state. Haptics remain separately configurable. All animation tasks accept cancellation and are stopped on page deactivation or account reset.

## 10. Exercise Artwork and API Media Flow

Exercise artwork is server-owned content. Bundled artwork is not the runtime source of truth.

The runtime path is:

1. exercise catalog metadata includes image identity/state;
2. the authenticated API returns a short-lived authorized media capability or URL;
3. a credential-safe media client downloads the bytes without leaking bearer credentials to an untrusted origin;
4. the mobile client validates and stores a bounded local thumbnail cache;
5. native `Image` controls display the cached bytes;
6. offline mode reuses the last valid cached image.

Each exercise row isolates image loading. A failed image displays a neutral anatomy/equipment placeholder and offers retry without hiding the exercise or affecting other rows. Search results must not wait for all images before becoming interactive.

Standard exercise images remain the approved anatomy/movement illustrations already represented in the catalog: realistic proportions, grayscale treatment, red target muscle, recognizable equipment, and movement path. No poster collage is displayed in the app; each exercise receives its own image.

## 11. State, Offline Behavior, and Errors

### 11.1 Local-First Mutations

Starting a workout, adding/reordering/removing an exercise, saving a set, completing a workout, and historical edits commit locally with their outbox operation before the UI reports success. The sync scheduler runs independently of page animations and survives relaunch.

### 11.2 Offline Presentation

Offline state uses a quiet inline banner or status pill. It never blocks logging. Cached catalog data, images, previous history, and active workout state remain available.

### 11.3 Business Errors

The client maps stable numeric server error codes to localized actions and copy. Raw numeric codes, raw server exception messages, storage keys, and signing details are not user-facing.

### 11.4 Authentication

Authentication expiry uses the shared session boundary and bounded refresh behavior. Account changes cancel stale media, sync, animation, and page work before clearing private caches.

### 11.5 Conflicts

Sync conflicts open a native comparison sheet with local and server values. The user receives explicit **Keep Server** and **Apply Local** actions only when each action is valid for that operation. Ambiguous in-flight mutations remain in a reconciling state and cannot be destructively resolved until the server outcome is known.

## 12. Presentation Boundaries

### 12.1 Shared Core

`TrackZ.Mobile.Core` owns:

- view models and presentation state;
- workout/exercise/history/progress DTOs;
- SQLite repositories and outbox;
- sync orchestration;
- API and media abstractions;
- localization keys and state-machine behavior.

It must not reference MAUI, UIKit, platform lifecycle types, or XAML assemblies.

### 12.2 MAUI Presentation

`TrackZ.Mobile` owns:

- XAML pages and reusable visual components;
- Shell/tab and navigation composition;
- page lifecycle activation/deactivation;
- platform adapters for sheets, haptics, Reduce Motion, navigation appearance, and animation drivers;
- accessibility semantics and visual states.

Code-behind may coordinate purely visual/native presentation but must not issue HTTP requests, SQL, or domain mutations directly.

## 13. Accessibility and Localization

- Thai and English remain fully supported.
- User-facing copy comes from resources, including empty, offline, conflict, image-failure, and reward states.
- Exercise art includes concise accessibility descriptions based on the exercise name and target body area.
- Increment and decrement controls expose distinct action-specific labels.
- Color is never the sole indicator of completion, PR, conflict, or selection.
- VoiceOver order follows the visible task order.
- Dynamic Type, Bold Text, Reduce Motion, contrast, and 44-point targets are acceptance requirements.

## 14. Verification and Acceptance

### 14.1 Automated Verification

- view-model/state tests for every screen state and command;
- local-first transaction/outbox tests;
- account/session cancellation and lifecycle tests;
- API catalog/media authorization tests;
- signed-media origin, tamper, expiry, retry, and bearer-leak tests;
- bounded image-cache and offline-image tests;
- localization and accessibility-label tests;
- clean-architecture guards keeping MAUI/UIKit out of Mobile.Core;
- XAML compilation and iOS application build;
- existing domain, application, infrastructure, API, and mobile regressions.

### 14.2 Simulator Acceptance Walkthrough

The release candidate must complete this sequence on an iOS simulator using the local API, PostgreSQL, and object storage:

1. sign in;
2. open Train and choose Shoulders;
3. search for an exercise by name;
4. see the correct per-exercise API artwork, LAST, and PR;
5. select multiple exercises and start;
6. log sets offline and online;
7. edit or delete a set through a native action;
8. complete the workout;
9. observe PR/XP/streak/badge changes;
10. relaunch and verify durable state, sync, and cached artwork.

The walkthrough is repeated with Reduce Motion enabled and with a large Dynamic Type size.

### 14.3 Visual Acceptance

Capture and review screenshots for the smallest supported iPhone width and a current Pro Max size. The application must show:

- native tab/navigation hierarchy;
- no clipped or desktop-style multi-column controls;
- consistent system typography;
- correct safe-area treatment;
- artwork loaded from the API path;
- clear loading, offline, empty, error, conflict, and success states;
- dark-mode contrast suitable for training environments.

## 15. Migration Strategy

The redesign is delivered in vertical slices so the application remains testable:

1. native Shell, visual tokens, typography, lifecycle, and motion foundations;
2. Train root and body-area sheet;
3. API-backed exercise picker and media states;
4. active workout and native exercise actions;
5. set logger and durable feedback;
6. history and historical actions;
7. Progress, summary, XP, streak, and badges;
8. You/settings, accessibility hardening, full simulator acceptance, and removal of obsolete presentation components.

Existing generic pages are replaced slice by slice. Shared domain, sync, and persistence behavior is reused rather than rewritten. Obsolete XAML styles/components are deleted only after their replacement is verified.

## 16. Approved Visual References

The collaborative Visual Companion established and approved these references:

- **Visual direction:** Native Performance;
- **Workout flow:** Today → body-area sheet → API exercise library → active workout;
- **Logger:** previous-versus-today comparison → inline set editor → durable save reward;
- **Motivation:** completion reveal → level/streak → long-term progress and earned badges.

The mockups are design references, not production HTML and not assets to embed in the application.
