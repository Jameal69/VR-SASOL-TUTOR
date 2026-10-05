# Sign Difficulty & Identification-Confidence Scoring

**Status:** reasoned analysis, **not measured.** Scores are estimates derived
from each sign's handshape and from how the recogniser works (DTW on
orientation-normalised hand landmarks). Real figures need camera capture and
evaluation. Use this to *prioritise*, not as ground truth.

## Scales

- **Difficulty (1–5):** how hard the sign is for a learner to *produce*
  (1 = very easy, 5 = hard).
- **ID now:** how reliably the **current** system could identify it, *assuming
  references are recorded*. High / Med / Low.
- **ID + fix:** projected reliability **if** the identification fix (finger-
  geometry features + head-anchor direction + palm-check fallback) is built and
  works as designed.
- **Limiter:** the main thing holding the confidence down.

## Lesson 1 — Greetings (currently recognised)

| Sign | Difficulty | ID now | ID + fix | Limiter |
|---|---|---|---|---|
| Hello | 1 | **High** | High | distinct dynamic sign — recorded & working |
| Goodbye | 1 | **High** | High | distinct dynamic sign — recorded & working |
| Please | 1 | **High** | High | distinct dynamic sign — recorded & working |
| Thank You | 1 | **High** | High | distinct dynamic sign — recorded & working |

## Lesson 2 — Alphabet

| Sign | Difficulty | ID now | ID + fix | Limiter |
|---|---|---|---|---|
| A | 2 | Low | Med | fist cluster (A/S/T/E) — bunched, subtle thumb |
| B | 1 | **High** | High | distinct, open |
| C | 1 | **High** | High | distinct, open |
| D | 2 | Med | High | mild D/F/O overlap |
| E | 3 | **Low** | **Low** | fist cluster + occlusion |
| F | 2 | Med | High | overlaps number 9 |
| G | 2 | Low | High | orientation pair with Q (fix resolves) |
| H | 2 | Low | High | orientation pair with U (fix resolves) |
| I | 1 | **High** | High | distinct (pinky only) |
| J | 3 | Med | High | movement letter; shares I hand + motion |
| K | 3 | Low | High | orientation pair with P (fix resolves) |
| L | 1 | **High** | High | very distinct |
| M | 3 | **Low** | **Low** | thumb under 3 fingers — occlusion, confuses N |
| N | 3 | **Low** | **Low** | thumb under 2 fingers — occlusion, confuses M |
| O | 2 | Med | High | mild overlap with C |
| P | 3 | Low | High | orientation pair with K (fix resolves) |
| Q | 3 | Low | High | orientation pair with G (fix resolves) |
| R | 3 | **Low** | **Low–Med** | crossed fingers — occlusion/depth (palm-turn may help) |
| S | 2 | Low | Med | fist cluster |
| T | 3 | **Low** | **Low** | thumb between folded fingers — occlusion |
| U | 2 | Low | High | confuses V + orientation pair with H (fix resolves) |
| V | 1 | Med | High | confuses U (fix resolves via spread) |
| W | 2 | Med–High | High | fairly distinct (3 spread) |
| X | 2 | Med | High | bent index |
| Y | 1 | **High** | High | very distinct (thumb + pinky) |
| Z | 3 | Med | High | movement letter; distinct motion |

## Lesson 3 — Numbers

| Sign | Difficulty | ID now | ID + fix | Limiter |
|---|---|---|---|---|
| 1 | 1 | **High** | High | distinct (index up) |
| 2 | 1 | Med–High | High | shares V hand; distinct among numbers |
| 3 | 2 | Med | High | overlaps W / 6 |
| 4 | 1 | **High** | High | distinct (four up) |
| 5 | 1 | **High** | High | very distinct (five spread) |
| 6 | 2 | Low–Med | Med | ambiguous OPEN-A config |
| 7 | 1 | **High** | High | distinct (L hand) |
| 8 | 2 | Low–Med | Med | specific config, mild ambiguity |
| 9 | 2 | Low–Med | Med | overlaps letter F |

## The still-difficult list

**Low confidence even WITH the fix** (the real ceiling — occlusion/depth, a
camera-input limit that features and training can't fully overcome):

- **E, M, N, T** — fingers fold over/under each other; landmarks unreliable.
- **R** — crossed fingers; borderline (a palm-turn may expose the cross).
- Borderline: **A, S** (fist cluster), **6, 8, 9** (ambiguous number configs).

**Low now, but the fix should resolve** (orientation + spread — logic limits, not
input limits):

- **G, Q, K, P, H, U** (orientation pairs) and **V** (U/V spread).

**Already reliable or easily so** (distinct — just need references recorded):

- Greetings (recorded); **B, C, L, W, Y, I**; numbers **1, 4, 5, 7** (and 2).

## What this means in one line

The current system is confident only on the 4 greetings. Recording references
for the **distinct** signs (B, L, Y, W, I; numbers 1, 4, 5, 7) would add
greeting-level confidence with no code change. The **orientation/spread** set
(K, P, G, Q, H, U, V) needs the fix. The **occluded** set (E, M, N, T, R and the
fist/ambiguous-number stragglers) stays hard regardless — that's where new
ideas are still needed.
