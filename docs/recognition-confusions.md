# Recognition: confusion analysis of imported references

Offline check (`backend-api/check_references.py` logic) on the 291 recordings in
`ml-service/references_raw` (Juan's `juan/references-import` branch). Each recording
is compared with every other one using DTW; "correct" means it is nearest its own sign.

## Results

| TRAJECTORY_WEIGHT | `all` scoring | `best_k` (K=3) |
|---|---|---|
| 0.0 (current default) | 285/291 | 286/291 |
| 0.5 | 289/291 | 289/291 |
| 1.0 | 289/291 | 290/291 |
| 2.0 | 286/291 | 286/291 |

## Which signs get confused

- **f vs 3** is the only confusion at every weight (1-3 of 8 `f` recordings land on `3`).
- Weight 0 only: `t` -> `n` (2), `q` -> `g` (1); with `best_k` also `x` and `please` lose one or two. Wrist position fixes most of these.
- Weight 2.0: wrist position starts to hurt (`c`, `o`, `s`, `v`, `x` each lose one).
- Closest sign pairs by mean DTW distance (weight 0): 2-v 0.63, 7-l 0.70, 2-h 0.77, 6-a 0.80, 4-b 0.83, 5-b 0.87. Correct offline, but little margin live.

## Hello vs Thank You

- Mean distance between the two: 1.46 (w=0), 1.53 (0.5), 1.68 (1.0), 2.15 (2.0). Their within-sign spread grows by the same factor (0.9 -> 1.2), so the trajectory weight does not separate them better.
- At weight 0 the distance (1.46) is just under `MATCH_THRESHOLD` (1.5): a Thank You attempt can score nearly as a Hello match.
- Offline each of the 9 Hello/Thank You recordings is still nearest its own sign; the tightest case is 0.91 (own) vs 1.13 (other).
- The recordings contain only hand landmarks (no nose or shoulders), so hand-position-relative-to-face cannot be computed from them.

## Caveats

1. **Every sign was recorded by exactly one signer** (see `SOURCES.csv`). A leave-one-signer-out test is impossible, so these scores measure one person's repeatability, not recognition of a new learner. Trajectory weight may also look better partly because it picks up camera/seating differences between signers.
2. **Recording quality:** digit `1` has 55-74% empty frames; `e`, `f`, `g`, `k`, `2` roughly 30-65%; `m` contains some left-hand frames; `3/rep_4` is only 33 frames; `1/rep_6` is 361 frames.
3. **Gaps:** `u` missing; `v` (3), `x` (4), `please` (4), `hello` (4), `thank_you` (5) recordings are few.

## Recommendation

- Do not change the `TRAJECTORY_WEIGHT` default in this PR.
- Try 0.5-1.0 with `best_k` after a live test with a signer who is not in the references.
- Next step: record nose/shoulder positions in the recording format and re-record Hello/Thank You so hand position relative to the face can be used.
