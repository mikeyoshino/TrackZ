# Exercise Technique Guide Implementation Plan

**Goal:** Let a customer open any exercise from the picker, read short plain-language instructions, enlarge its image, and add the exercise without losing picker state.

**Architecture:** A mobile-core guidance catalog maps every system exercise to a movement family and returns localized steps, a tip, and a caution. A scoped detail view model loads the cached exercise and image. The picker opens a dedicated detail page; that page owns a full-screen image overlay and can select the exercise through the existing picker view model.

**Tech Stack:** .NET 10, .NET MAUI XAML, xUnit, iOS Simulator.

## Global Constraints

- Thai copy uses plain language and short sentences.
- Existing TrackZ dark surfaces, lime accent, Noto Sans Thai typography, and 44-point touch targets remain authoritative.
- Reading details must not toggle selection; only the checkbox or “เพิ่มท่านี้” changes selection.
- The detail page and image viewer hide the app tab bar.

### Task 1: Guidance data and state

- Add a catalog that covers all 90 system exercise names and a safe fallback for custom exercises.
- Add a view model that loads one cached exercise and its image.
- Write tests for representative movement families, Thai wording, fallback behavior, and selection.

### Task 2: Picker interaction

- Add “วิธีฝึก” to each picker row.
- Make the image/content area open details while the checkbox remains the selection control.
- Add and test the detail navigation route.

### Task 3: Detail and image UI

- Build the compact detail screen shown in the approved mock.
- Add a full-screen image overlay with close and zoom controls.
- Verify accessibility, small-screen scrolling, reduced motion, tests, iOS build, and Simulator behavior.
