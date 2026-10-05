# Full-Alphabet Recognition — Design Proposal (post-demo)

**Status:** proposal / future work. **Not implemented in the live system.**
Author: Juan. For discussion once the live demo is delivered and the pipeline is
stable (the "revisit it then" work the lead and supervisor agreed to park).

> This document describes an *approach*. The prototype in
> `ml-service/experimental/handshape_features.py` demonstrates the geometry on
> synthetic hands. Neither this doc nor that module changes
> `gesture_classifier.py`, the `/api/gestures/classify` endpoint, or
> `references_raw/`. The working demo is untouched.

## The problem

The current recogniser (DTW over normalised hand-landmark sequences, matched
against reference recordings) reliably handles the four greeting signs, which
differ by distinct movements. A full fingerspelling alphabet is harder because
the letters are mostly *static* handshapes, and many are nearly identical. The
confusions fall into **two different categories**, which need **two different
fixes**:

| Category | Example pairs | Why the current recogniser struggles |
|---|---|---|
| **Orientation** — same handshape, different direction | K/P, G/Q, U/H | The recogniser normalises each hand into its own local frame, deliberately *discarding* orientation to match shapes at any angle. So it literally cannot tell K (up) from P (down). |
| **Handshape** — different finger configuration | U/V, M/N, R/U, D/F/O | The difference (finger spread / count / crossing) is a *tiny fraction* of the whole-sequence DTW distance, so natural variation and landmark noise swamp it. |

## The approach (four complementary pieces)

**1. Explicit finger-geometry features** → fixes the *handshape* pairs.
Instead of matching raw landmarks, compute what actually distinguishes the
letters: each finger's *curl*, the *spread* angle between adjacent fingers, and
which fingers are *extended*. The index/middle spread angle is exactly the U
(together) vs V (apart) cue. Prototyped in `handshape_features.py`
(`finger_curl`, `finger_spread`, `extended_fingers`).

**2. Head-anchored pointing direction** → fixes the *orientation* pairs.
Use the head/nose landmark (already captured — the pipeline runs MediaPipe
**Holistic**, not hands-only) as a body-relative anchor: "up = toward the head."
This recovers the direction information the local-frame normalisation throws
away, and because it's relative to the *body* rather than the screen, it's
robust to camera angle. Prototyped as `pointing_direction`.

**3. Confidence-gated "palm-check" fallback** → a cheap UX safety net.
When the recogniser is *unsure* — specifically when the top two candidates are
too close ("it's U or V and I can't tell") — prompt the learner to turn their
palm to the camera for a clean, low-occlusion read, then re-check. Only on
ambiguity, never as a routine extra step. Using it *selectively* also avoids a
trap: forcing palm-to-camera always would flatten orientation and re-break K/P,
so it must fire only for *handshape* ambiguity. Prototyped as
`should_request_palm_check`.

**4. A trained model on the above features** → generalisation.
DTW template-matching gives way (or is supplemented) by a classifier trained on
the engineered features, over a *diverse* dataset (multiple signers, varied
conditions). Training learns the decision boundaries and generalises across
people — but only amplifies signal that is *in* the data, so good features (1, 2)
come first. The avatar could generate synthetic training data to bulk this out
(with sim-to-real caveats).

## Honest limits (where the ceiling is)

- **Depth noise.** MediaPipe's z-axis is its least accurate; distinctions that
  live in depth stay shaky.
- **Occlusion.** M, N, T fold fingers over each other and R crosses them, so
  landmarks get hidden/guessed exactly where precision is needed. The palm-check
  reduces this but cannot eliminate it. **R and the folded letters are the
  genuinely hard cases** — expect them last, if at all.
- **Data + verification.** A trained model needs a lot of diverse, *verified*
  data (Deaf-advisor sign-off, report §8.1). Garbage or single-person data →
  overfitting and wrong learning.
- **Pedagogy.** A "turn your palm to check" prompt teaches handshape slightly in
  isolation from natural orientation; SASL treats orientation as meaningful, so
  this needs a conscious note in the Deaf-advisor review.

## Suggested staging (if/when greenlit, after the demo)

1. Add finger-geometry + head-anchor features alongside the existing DTW (not
   replacing it), on a feature branch, reviewed — recapture references with the
   new feature vector.
2. Validate on the *numbers* first (distinct, achievable), then distinct letters
   (B, L, V, Y, W), then the confusable pairs.
3. Add the confidence-gated palm-check once per-pair accuracy is measured.
4. Only then consider a trained model + a proper labelled dataset.

## What this is NOT

- Not a change to the live recogniser or references — those stay as the lead
  tuned them.
- Not tested for real-world accuracy — the prototype validates geometry only.
- Not for the current milestone. The live demo stays scoped to the four
  greetings (recognised) plus the alphabet/numbers as content, per the README.
