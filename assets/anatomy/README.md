# Vector anatomy source

Pinned source: https://github.com/HichamELBSI/react-native-body-highlighter/tree/15df9e2dbc621450001960bed5a30e6a75357faa

`vendor/bodyFront.ts`, `vendor/bodyBack.ts`: unmodified MIT source paths, Copyright (c) 2022 ELABBASSI Hicham. License preserved in vendor/LICENSE and packaged body.LICENSE.txt. We import path data only; no React/JS runtime ships in the app.

Run `node assets/anatomy/build-vector.mjs` to rebuild `Resources/Raw/muscles/body.svg`. This statically extracts strings, never evaluates vendor TypeScript.

Build-time normalization uses vendored `fontello/svgpath` 2.6.0 (MIT, Copyright 2013–2015 Vitaly Puzrin). Download: https://registry.npmjs.org/svgpath/-/svgpath-2.6.0.tgz; verified SHA-512 base64 `OIWR6bKzXvdXYyO4DK/UWa1VA1JeKq8E+0ug2DG98Y/vOmMpfZNj+TIG988HjfYSqtcy/hFOtZq/n/j5GSESNg==`. Its license is in `vendor/svgpath/LICENSE`. This utility does not ship or run in the app. It expands relative/compact arc syntax into explicit absolute Bézier commands because the native MAUI parser accepted the original syntax without preserving its geometry. The resulting SVG remains editable vector geometry, not a raster overlay.

TrackZ changes: map paths to our 22 training regions; split pectoral and front deltoid illustrations with curved boundaries; separate gluteus medius illustration from gluteal mass; keep deep-core as a labelled separate inset, not a visible surface muscle; add sparse illustrative vector fibre lines. Back scapular paths stay upper-back, lateral back paths map to lats. Decorative joints/head/hands never receive training status.

This is a stylized fitness diagram, not a clinical anatomy atlas or an exact reproduction of the photorealistic mock. Chest zones express exercise emphasis. No claim of medical validation or actual measured muscle activation/growth. Refinements require content review, not guessing masks over a raster.
