# Recording Plan — references for all signs (best-effort)

**Goal:** let the camera attempt *every* sign, not just the four greetings —
accepting that the hard ones will be imperfect at first, then fine-tuning later.

**Key point:** the recogniser already compares an attempt against every folder in
`ml-service/references_raw/`. A sign becomes "looked for" the moment it has
reference recordings. So this is a *recording* task, not a code change.

## How to record one sign
From `ml-service/` (venv active, webcam connected):
```
py -3.12 dtw_recognizer.py record <sign_name>
```
Perform the sign, SPACE to start/stop each rep, ~5–6 reps. Then confirm:
```
py -3.12 dtw_recognizer.py compare <sign_name>
```

**Folder-name convention** (must match what the lesson passes as the label):
greetings already use `hello`, `goodbye`, `please`, `thank_you`. Suggested:
letters `a`…`z`, numbers `1`…`9`. Keep it consistent with the `curriculum_items`
so the lesson's sign maps to the right reference folder.

## Suggested order (fastest confidence first)

**1. Quick wins — distinct signs, should reach greeting-level confidence:**
numbers `1, 4, 5, 7`; letters `b, l, y, w, i`. Record these first — they pay off
immediately with no fix needed.

**2. The rest of the clearly-formed set:** `c, d, f, o, x, v`, numbers `2, 3`,
and the movement letters `j, z`.

**3. Orientation / spread set — record them, but expect misreads until the fix:**
`k, p, g, q, h, u`. The system will attempt them; confidence will be shaky until
the head-anchor/features land.

**4. Hard set — best-effort, will be low-confidence:** `e, m, n, t, r`, plus
`a, s` and numbers `6, 8, 9`. Record them anyway so the camera *tries*; flag the
results as low-confidence and refine later.

## Tips for cleaner references
- Consistent framing and good, even lighting. Plain background.
- Hold the sign steady; for static letters a short, still clip is fine.
- For handshape clarity, a roughly palm-to-camera presentation reduces occlusion
  (the same idea as the "palm-check"). Won't save M/N/T, but helps most.
- Record a couple of slight angle variations per sign for robustness.
- More references per sign = better matching, up to a point (5–8 is a good start).

## Honest expectation
Recording references makes the camera *attempt* every sign. The **distinct** ones
will work well; the **orientation** ones need the fix; the **occluded** ones
(E, M, N, T, R) will stay low-confidence no matter how many clips — that's a
tracking limit. Surface the confidence to the learner ("best guess: M — low
confidence") rather than hiding it, and improve from there.
