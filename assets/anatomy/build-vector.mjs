// Rebuild the packaged, editable SVG from pinned MIT source. No TS is executed.
// TrackZ modifications: training-region IDs, curved chest/deltoid subdivisions,
// deep-layer inset and illustrative fibre paths. See README.md and vendor/LICENSE.
import { readFileSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, resolve } from 'node:path';
import svgpath from './vendor/svgpath/index.js';
const here = dirname(fileURLToPath(import.meta.url));
const output = [];
let sequence = 0;
const escape = value => value.replaceAll('&', '&amp;').replaceAll('"', '&quot;').replaceAll('<', '&lt;');
function normalize(d) {
  const geometry = svgpath(d).abs().unshort().unarc().round(4);
  if (geometry.err) throw new Error(geometry.err);
  const commands = [];
  geometry.iterate((segment, _, x, y) => {
    if (segment[0] === 'H') segment = ['L', segment[1], y];
    if (segment[0] === 'V') segment = ['L', x, segment[1]];
    if (segment[0] === 'z') segment = ['Z'];
    if (!['M','L','C','Q','Z'].includes(segment[0])) throw new Error(`Unsupported normalized command ${segment[0]}`);
    if (segment[0] === 'Z' && commands.at(-1) === 'Z') return;
    commands.push(segment.join(' '));
  });
  return commands.join(' ');
}
function path(d, region, side, extra = '') {
  output.push(`  <path id="muscle-${++sequence}"${region ? ` data-region="${region}"` : ''} data-side="${side}"${extra} d="${escape(normalize(d))}"/>`);
}
const mapping = {
  obliques: 'obliques', abs: 'abs', biceps: 'biceps', triceps: 'triceps',
  adductors: 'adductors', quadriceps: 'quads', tibialis: 'shins',
  calves: 'calves', forearm: 'forearms', hamstring: 'hamstrings',
  'lower-back': 'lower-back'
};
for (const side of ['front', 'back']) {
  const text = readFileSync(resolve(here, `vendor/body${side === 'front' ? 'Front' : 'Back'}.ts`), 'utf8');
  for (const block of text.split(/\n  \/\//).slice(1)) {
    const slug = /slug: "([^"]+)"/.exec(block)?.[1];
    if (!slug) continue;
    if (side === 'front' && (slug === 'chest' || slug === 'deltoids')) continue;
    for (const [index, match] of [...block.matchAll(/"([Mm][^"]+)"/g)].entries()) {
      let region = mapping[slug];
      if (slug === 'trapezius' && side === 'back') region = 'upper-back';
      if (slug === 'deltoids') region = 'shoulder-rear';
      if (slug === 'upper-back') region = index === 1 || index === 5 ? 'lats' : 'upper-back';
      if (slug === 'gluteal') region = index % 2 === 0 ? 'lateral-hips' : 'glutes';
      path(match[1], region, side);
    }
  }
}

// Pectoral emphasis zones follow the same outer silhouette; boundaries fan from
// the humeral attachment toward the sternum. They are not isolated muscles.
for (const [region, d] of [
  ['chest-upper', 'M260 345 C266 332 287 320 302 319 C318 317 337 317 342 321 C351 328 354 340 357 354 C323 350 289 344 260 350 Z'],
  ['chest-middle', 'M260 352 C289 346 323 352 357 356 C360 370 359 387 357 397 C326 399 292 387 257 365 C257 360 258 355 260 352 Z'],
  ['chest-lower', 'M257 368 C289 389 324 401 356 399 C355 410 352 418 344 424 C328 437 310 438 297 433 C282 430 271 423 265 411 C259 398 257 384 257 368 Z'],
  ['chest-upper', 'M469 345 C463 332 442 320 427 319 C411 317 392 317 387 321 C378 328 375 340 372 354 C406 350 440 344 469 350 Z'],
  ['chest-middle', 'M469 352 C440 346 406 352 372 356 C369 370 370 387 372 397 C403 399 437 387 472 365 C472 360 471 355 469 352 Z'],
  ['chest-lower', 'M472 368 C440 389 405 401 373 399 C374 410 377 418 385 424 C401 437 419 438 432 433 C447 430 458 423 464 411 C470 398 472 384 472 368 Z'],
  // Deltoid anterior and lateral sections meet along a curved fibre boundary.
  ['shoulder-front', 'M251 304 C260 304 268 307 274 312 C277 314 279 318 278 321 C267 327 253 329 249 341 C246 349 249 360 246 369 C242 380 235 387 228 394 C226 397 223 397 220 395 C239 371 239 332 251 304 Z'],
  ['shoulder-side', 'M248 304 C236 303 225 308 219 316 C207 325 197 337 195 352 C193 369 201 384 217 394 C235 369 236 332 248 304 Z'],
  ['shoulder-front', 'M478 304 C469 304 461 307 455 312 C452 314 450 318 451 321 C462 327 476 329 480 341 C483 349 480 360 483 369 C487 380 494 387 501 394 C503 397 506 397 509 395 C490 371 490 332 478 304 Z'],
  ['shoulder-side', 'M481 304 C493 303 504 308 510 316 C522 325 532 337 534 352 C536 369 528 384 512 394 C494 369 493 332 481 304 Z']
]) path(d, region, 'front');

// Transverse abdominal wall: separate diagram layer, never painted over rectus abs.
path('M230 570 C240 495 489 495 499 570 C489 650 240 650 230 570 Z M251 570 C261 520 468 520 478 570 C468 626 261 626 251 570 Z', 'deep-core', 'front', ' data-layer="deep" display="none" fill-rule="evenodd"');

// Sparse vector fibre lines are illustrative linework, clipped by their own muscle
// shape. They are not additional status masks or a medical fibre-orientation map.
function detail(region, side, d) {
  output.push(`  <path id="fibre-${++sequence}" data-detail-for="${region}" data-side="${side}" fill="none" stroke-width="0.8" stroke-opacity="0.15" d="${escape(normalize(d))}"/>`);
}
for (const region of ['chest-upper', 'chest-middle', 'chest-lower']) {
  for (let i = 0; i < 10; i++) {
    const y = 322 + i * 12;
    detail(region, 'front', `M259 350 C284 ${y} 324 ${y+5} 359 ${y+9}`);
    detail(region, 'front', `M470 350 C445 ${y} 405 ${y+5} 370 ${y+9}`);
  }
}
for (const [region, side, x, y, w, h] of [
  ['quads','front',245,667,70,273], ['quads','front',414,667,70,273],
  ['hamstrings','back',980,787,78,177], ['hamstrings','back',1109,787,78,177],
  ['biceps','front',178,418,44,110], ['biceps','front',507,418,44,110],
  ['calves','back',996,1000,66,180], ['calves','back',1105,1000,66,180],
  ['glutes','back',984,641,84,131], ['glutes','back',1100,641,84,131],
  ['lats','back',984,419,68,164], ['lats','back',1117,419,68,164]
]) {
  for (let i=1; i<8; i++) {
    const start = x + w*i/8;
    detail(region, side, `M${start} ${y} C${start-8} ${y+h*.32} ${start+8} ${y+h*.7} ${x+w*.5+(start-x-w*.5)*.4} ${y+h}`);
  }
}
const svg = `<?xml version="1.0" encoding="utf-8"?>
<!-- Derived from react-native-body-highlighter, MIT, Copyright (c) 2022 ELABBASSI Hicham.
     TrackZ modified region mapping, chest/deltoid paths and vector detail. See body.LICENSE.txt. -->
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1440 1450" fill="#626B72" stroke="#090D0E" stroke-width="1">
  <title>TrackZ front and back muscle coverage</title>
  <desc>Training-region illustration. Colours represent recorded training, not measured muscle growth. Deep-core is a separate layer.</desc>
${output.join('\n')}
</svg>
`;
writeFileSync(resolve(here, '../../src/TrackZ.Mobile/Resources/Raw/muscles/body.svg'), svg);
writeFileSync(resolve(here, '../../src/TrackZ.Mobile/Resources/Raw/muscles/body.LICENSE.txt'), readFileSync(resolve(here, 'vendor/LICENSE')));
console.log(`Wrote ${sequence} vector paths; no bitmap elements.`);
