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

Options (for checking volunteer recordings before they're merged, see
docs/volunteer-recordings.md):
    python check_references.py --scoring best_k         # score with the best-K mode
    python check_references.py --refs path/to/staging   # check a candidate folder
"""
import argparse
import os
import numpy as np
from app import gesture_classifier as gc

parser = argparse.ArgumentParser(description="Offline check of reference recordings.")
parser.add_argument("--scoring", choices=["all", "best_k"], default=gc.SCORING,
                    help="how a sign's references are combined (default: the classifier's current SCORING)")
parser.add_argument("--k", type=int, default=gc.BEST_K, help="K for --scoring best_k")
parser.add_argument("--refs", default=gc.REFERENCES_DIR, help="reference folder to check (default: references_raw)")
args = parser.parse_args()
gc.SCORING, gc.BEST_K = args.scoring, args.k
print(f"scoring={args.scoring}" + (f" (K={args.k})" if args.scoring == "best_k" else "") + f"  refs={args.refs}")

WEIGHTS = (0.0, 0.5, 1.0, 2.0)

raw = {}  # sign -> [(filename, raw array)]
for sign in sorted(os.listdir(args.refs)):
    d = os.path.join(args.refs, sign)
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
                    avgs[other] = gc.sign_score(ds)
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
