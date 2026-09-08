# TrackZ anatomy assets

Historical image-generation provenance from 2026-09-08. The generated body atlas was rejected and is no longer shipped. The current native SVG uses attributed MIT source; see `assets/anatomy/README.md`. The generated shoulder thumbnail below remains bundled.

## Body atlas

Archived asset: `artifacts/anatomy-review/rejected-body-atlas.png` (not packaged).

Reference: the user-approved three-screen muscle coverage mock. This former approach used a neutral atlas plus runtime masks. The user rejected inaccurate polygon boundaries; native curved SVG paths now replace both the atlas and masks.

Prompt:

> Create a production anatomical illustration asset for TrackZ mobile app. Reference is STYLE ONLY: match the detailed grayscale athletic anatomical figures, NOT the phone mockup UI. Output a single portrait 1024x1536 image containing two full-body male anatomical figures side by side, anterior view on left, posterior view on right, identical scale and neutral relaxed arms slightly away from torso, full head and feet visible. Each figure centered at 25% and 75% canvas width, head top at 3% and soles at 97%, no perspective. Dark charcoal short athletic briefs covering private areas, glute muscles on posterior visible below minimal waistband as educational anatomy. Monochrome silver/gray muscles with realistic muscle fiber striations, smooth sculptural shading and clearly separated deltoids, biceps, triceps, forearms, chest, abdomen, obliques, lats, trapezius, quadriceps, inner thighs, hamstrings, glutes and calves. Neutral grayscale ONLY, no lime/cyan highlights at all: software will apply dynamic region colors. Pure flat solid #090D0E background, no halo, no floor, no cast shadow outside figures, no text, no numbers, no labels, no arrows, no phone, no UI. Clean anatomically plausible educational illustration, athletic not exaggerated bodybuilder. Preserve natural hands and feet. Make this a polished actual app asset, not a mockup.

## Seated barbell shoulder press

Asset: `src/TrackZ.Mobile/Resources/Images/seated_barbell_shoulder_press.png`.

Bundled offline fallback for the canonical supplemental exercise only. A downloaded local image takes precedence; a server image route remains eligible for normal download. This does not modify the separately approved server image publication manifest.

Prompt:

> Production square exercise thumbnail for Seated Barbell Shoulder Press. Reference image is style only: match grayscale anatomical educational athlete, white warm-gray studio backdrop, fine muscle fiber shading, dark shorts and trainers and black adjustable upright bench. Show ONE seated man performing a BARBELL overhead shoulder press, two hands holding the SAME single straight barbell slightly wider than shoulder width, palms forward, equal small round plates on both ends. Main figure bar directly overhead, arms extended without exaggerated lockout, back supported by upright bench, feet grounded, ribs neutral. Smaller faint ghost figure left indicates starting position with SAME BARBELL in front of upper chest, never behind neck. Shoulder muscles subtle muted red emphasis. Single gray upward arrow beside figure. Clearly a barbell with continuous shaft, NOT separate dumbbells; no extra arms, no extra bars, no text, no labels, no branding. Complete body, bench and entire bar with plates visible within square, professional clean consistent exercise library asset.

## Interpretation

The illustrations are educational UI assets, not individualized form assessment or a clinical anatomy reference. Coverage colors summarize recorded exercise attribution, not measured activation, muscle growth or a diagnosis. The current deep-core illustration is a separate labeled cross-section, not a surface projection; chest subdivisions describe training emphasis.
