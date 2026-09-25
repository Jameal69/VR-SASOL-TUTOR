"""
Offline check of the reference recordings. No camera, no backend needed.

Put this file in backend-api/ (next to the app folder) and run, with the venv active:
    python check_references.py

For every reference recording it measures DTW distance to every OTHER recording
(itself excluded), averages per sign, and reports which sign it looks most like.
If Hello recordings often come out closest to Goodbye, the features can't tell
those signs apart, no matter what threshold we pick.

Runs once per TRAJECTORY_WEIGHT (0 = hand shape only), so you can see how
much the wrist's screen position helps and pick the weight to use.
"""
import os
import numpy as np
from app import gesture_classifier as gc

WEIGHTS = (0.0, 0.5, 1.0, 2.0)

raw = {}  # sign -> [(filename, raw array)]
for sign in sorted(os.listdir(gc.REFERENCES_DIR)):
    d = os.path.join(gc.REFERENCES_DIR, sign)
    if not os.path.isdir(d):
        continue
    raw[sign] = [(f, np.load(os.path.join(d, f)))
                 for f in sorted(os.listdir(d)) if f.endswith(".npy")]

print("== Recordings ==")
for sign, items in raw.items():
    for f, arr in items:
        zero = float(np.mean(np.all(arr == 0, axis=1))) * 100
        lp = float(np.mean(np.any(arr[:, :63] != 0, axis=1))) * 100
        rp = float(np.mean(np.any(arr[:, 63:] != 0, axis=1))) * 100
        print(f"{sign:10s} {f:14s} shape={arr.shape}  empty={zero:.0f}%  left={lp:.0f}%  right={rp:.0f}%")

signs = list(raw)


def run(weight, verbose):
    gc.TRAJECTORY_WEIGHT = weight
    refs = {s: [(f, gc._sequence_features(a)) for f, a in items] for s, items in raw.items()}
    confusion = {s: {t: 0 for t in signs} for s in signs}
    own, nearest_other = [], []
    for sign, items in refs.items():
        for f, arr in items:
            avgs = {}
            for other, oitems in refs.items():
                ds = [gc._dtw_distance(arr, a) for g, a in oitems if not (other == sign and g == f)]
                if ds:
                    avgs[other] = sum(ds) / len(ds)
            best = min(avgs, key=avgs.get)
            confusion[sign][best] += 1
            own.append(avgs.get(sign, float("inf")))
            nearest_other.append(min(v for k, v in avgs.items() if k != sign))
            if verbose:
                line = "  ".join(f"{k}={v:.2f}" for k, v in avgs.items())
                print(f"{sign:10s} {f:14s} -> {best:10s} [{line}]")
    correct = sum(confusion[s][s] for s in signs)
    total = sum(len(v) for v in raw.values())
    print(f"\ntrajectory_weight={weight}  -> {correct}/{total} recordings nearest their own sign")
    # Gap between these two is the room a MATCH_THRESHOLD has to work with.
    print(f"avg distance to own sign={np.mean(own):.2f}   to nearest other sign={np.mean(nearest_other):.2f}")
    print(" " * 11 + "".join(f"{s:>11s}" for s in signs))
    for s in signs:
        print(f"{s:11s}" + "".join(f"{confusion[s][t]:11d}" for t in signs))


default_weight = gc.TRAJECTORY_WEIGHT
for w in WEIGHTS:
    print("\n" + "=" * 60)
    run(w, verbose=(w == default_weight))
gc.TRAJECTORY_WEIGHT = default_weight
