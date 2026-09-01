# ml-service

Gesture recognition pipeline. Takes landmark data (from MediaPipe)
and returns a predicted sign + confidence score.

Runs as its own process, separate from Unity, for performance reasons.

## Setup

Requires **Python 3.12** specifically — `mediapipe` doesn't yet support
3.13/3.14. If you have a different version installed, that's fine, just
make sure 3.12 is also installed alongside it, and always run with
`py -3.12` (not plain `python`) so it doesn't default to the wrong one.

```
py -3.12 -m pip install mediapipe opencv-python
```

Each script below auto-downloads its MediaPipe model file (~30-40MB) on
first run — that's expected, only happens once.

## Scripts

### `holistic_webcam_test.py` — feasibility spike (done)
Opens the webcam, runs MediaPipe Holistic, prints hand landmark
coordinates to the console. Proved gesture recognition works on our
hardware. Superseded by the two scripts below for anything further —
kept for reference.

### `landmark_streamer.py` — Python → Unity socket bridge
Runs Holistic and streams hand landmarks to Unity live over a local TCP
socket (`127.0.0.1:5052`), instead of just printing them.

```
py -3.12 landmark_streamer.py
```
Run this **before** pressing Play in Unity. It'll print
`Waiting for Unity to connect...` and pause there until the Unity side
connects (see `LandmarkReceiver.cs` in `frontend-unity`).

### `dtw_recognizer.py` — sign classifier
Dynamic Time Warping classifier. No training needed — record a few
reference examples of a sign, then compare a live attempt against them.

```
py -3.12 dtw_recognizer.py record <label>    # save reference examples
py -3.12 dtw_recognizer.py compare <label>   # test a live attempt
```
SPACE to start/stop a recording, `q` to quit.

Landmarks are normalized (position, scale, and rotation invariant)
before comparing, so the same sign held at a different angle/distance
from the camera still matches correctly. Verdict is by majority vote
across all saved references, not just the single closest one.

`classify(sequence, references)` returns `(is_match, confidence,
distances)` and is meant to be reused elsewhere (e.g. feeding Screen 7's
live accuracy bar) rather than only used via this CLI.

**Currently using a placeholder gesture**, not a real SASL sign — same
code will be used once `docs/curriculum-lesson1.md` lands, just record
over it with the real Lesson 1 signs.

## Known limitations / open questions

- **Not actually "live" yet.** DTW needs a complete sequence to compare,
  so `classify()` can only score an attempt after it's finished, not
  smoothly update mid-sign the way SR-16 in the M1 report describes.
  Needs a windowed/incremental approach to close this gap.
- **Only tested on one person's hand so far.** DTW here has zero
  training, so there's no guarantee it generalizes to different hand
  sizes/shapes without testing.
- **On-device vs. network classification** — open question in
  `docs/api-contract.md`, affects how this service's output eventually
  reaches the backend. Unresolved as of this writing.
- **Output format to Unity/backend** not yet locked down with Keegan/
  Jason — `classify()`'s return shape is a starting point, not final.

## Branch

Gesture recognition work happens on `feature/gesture-landmark-capture`
(or task-specific branches off it).
