"""
Bring reviewed volunteer recordings into the project's references.

Input is the 'approved_for_project' folder exported by Juan's recorder tool
(sasl-harvester/review.py):  approved_for_project/<sign>/rep_N.npy  plus
approved_manifest.csv (sign, path, contributor, rel).

    py -3.12 import_approved.py "C:/path/to/sasl-harvester/approved_for_project"           # dry run: shows the plan
    py -3.12 import_approved.py "C:/path/to/sasl-harvester/approved_for_project" --apply   # actually copies

What it does:
  - checks every file is a real recording in our format (frames x 126 floats)
  - copies it to references_raw/<sign>/rep_N.npy using the next FREE number,
    so nothing already there is ever overwritten
  - records provenance in references_raw/SOURCES.csv with a signer CODE
    (P01, P02, ...) - never the person's name. The name -> code list is kept
    privately next to the approved folder (signer_codes_PRIVATE.csv), outside
    the repo, so codes stay the same across batches.

Full process (consent, review, checks before merging): docs/volunteer-recordings.md
"""

import argparse
import csv
import datetime
import os
import re
import shutil
import sys

import numpy as np

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
DEFAULT_TARGET = os.path.join(SCRIPT_DIR, "references_raw")
MIN_FRAMES = 10


def next_free_index(sign_dir):
    if not os.path.isdir(sign_dir):
        return 0
    numbers = [int(m.group(1)) for m in (re.match(r"rep_(\d+)\.", f) for f in os.listdir(sign_dir)) if m]
    return max(numbers) + 1 if numbers else 0


def load_contributors(approved_dir):
    """approved file (sign/rep_N.npy) -> contributor name, from the review export manifest."""
    manifest = os.path.join(approved_dir, "approved_manifest.csv")
    owners = {}
    if not os.path.exists(manifest):
        return owners
    with open(manifest, newline="", encoding="utf-8") as fh:
        for row in csv.reader(fh):
            if len(row) < 3 or row[0] == "sign":
                continue
            sign, path, contributor = row[0], row[1], row[2]
            owners[f"{sign}/{os.path.basename(path)}"] = contributor
    return owners


def load_codes(codes_file):
    codes = {}
    if os.path.exists(codes_file):
        with open(codes_file, newline="", encoding="utf-8") as fh:
            for row in csv.reader(fh):
                if len(row) == 2 and row[0] != "name":
                    codes[row[0]] = row[1]
    return codes


def code_for(name, codes):
    if name not in codes:
        codes[name] = f"P{len(codes) + 1:02d}"
    return codes[name]


def check_recording(path):
    """Returns (ok, reason, frames)."""
    try:
        arr = np.load(path)
    except Exception as e:
        return False, f"unreadable ({e})", 0
    if arr.ndim != 2 or arr.shape[1] != 126:
        return False, f"wrong shape {arr.shape}, expected (frames, 126)", 0
    if not np.all(np.isfinite(arr)):
        return False, "contains NaN/inf", len(arr)
    hand_frames = int(np.sum(np.any(arr != 0, axis=1)))
    if hand_frames < MIN_FRAMES:
        return False, f"only {hand_frames} frames with a hand (need {MIN_FRAMES}+)", len(arr)
    return True, "", len(arr)


def main():
    parser = argparse.ArgumentParser(description="Import reviewed volunteer recordings into references_raw.")
    parser.add_argument("approved_dir", help="the approved_for_project folder from the recorder's review tool")
    parser.add_argument("--target", default=DEFAULT_TARGET, help="reference folder to import into (default: references_raw)")
    parser.add_argument("--apply", action="store_true", help="actually copy the files (default is a dry run)")
    args = parser.parse_args()

    approved_dir = os.path.abspath(args.approved_dir)
    if not os.path.isdir(approved_dir):
        sys.exit(f"Not a folder: {approved_dir}")
    repo_root = os.path.dirname(SCRIPT_DIR)
    try:
        inside_repo = os.path.commonpath([approved_dir, repo_root]) == repo_root
    except ValueError:  # different drive, so certainly not inside the repo
        inside_repo = False
    if inside_repo:
        sys.exit("The approved folder is inside the repo. Keep volunteer data outside the repo and point to it.")

    owners = load_contributors(approved_dir)
    codes_file = os.path.join(os.path.dirname(approved_dir), "signer_codes_PRIVATE.csv")
    codes = load_codes(codes_file)

    plan, skipped = [], []
    next_index = {}
    for sign in sorted(os.listdir(approved_dir)):
        src_dir = os.path.join(approved_dir, sign)
        if not os.path.isdir(src_dir):
            continue
        dst_dir = os.path.join(args.target, sign)
        next_index.setdefault(sign, next_free_index(dst_dir))
        for fname in sorted(os.listdir(src_dir), key=lambda f: [int(t) if t.isdigit() else t for t in re.split(r"(\d+)", f)]):
            if not fname.endswith(".npy"):
                continue
            src = os.path.join(src_dir, fname)
            ok, reason, frames = check_recording(src)
            if not ok:
                skipped.append((f"{sign}/{fname}", reason))
                continue
            code = code_for(owners.get(f"{sign}/{fname}", "unknown"), codes)
            dst = os.path.join(dst_dir, f"rep_{next_index[sign]}.npy")
            next_index[sign] += 1
            plan.append((sign, src, dst, code, frames))

    print(f"{'APPLYING' if args.apply else 'DRY RUN (nothing copied; add --apply)'}: {approved_dir}")
    print(f"  -> into {args.target}\n")
    for sign, src, dst, code, frames in plan:
        new_sign = "" if os.path.isdir(os.path.dirname(dst)) else "   (new sign folder)"
        print(f"  {code}  {sign}/{os.path.basename(src):12s} -> {os.path.relpath(dst, args.target)}  ({frames} frames){new_sign}")
    for name, reason in skipped:
        print(f"  SKIP  {name}: {reason}")
    print(f"\n{len(plan)} to import, {len(skipped)} skipped, {len(set(c for *_, c, _ in plan))} signer(s)")

    if not args.apply or not plan:
        return

    sources = os.path.join(args.target, "SOURCES.csv")
    new_sources = not os.path.exists(sources)
    now = datetime.datetime.now().isoformat(timespec="seconds")
    with open(sources, "a", newline="", encoding="utf-8") as fh:
        writer = csv.writer(fh)
        if new_sources:
            writer.writerow(["file", "sign", "signer_code", "imported_at"])
        for sign, src, dst, code, frames in plan:
            os.makedirs(os.path.dirname(dst), exist_ok=True)
            shutil.copy2(src, dst)
            writer.writerow([os.path.relpath(dst, args.target).replace("\\", "/"), sign, code, now])

    with open(codes_file, "w", newline="", encoding="utf-8") as fh:
        writer = csv.writer(fh)
        writer.writerow(["name", "code"])
        writer.writerows(sorted(codes.items(), key=lambda kv: kv[1]))

    print(f"\nCopied {len(plan)} recordings. Provenance (codes only): {sources}")
    print(f"Name -> code list (PRIVATE, do not commit): {codes_file}")
    print("Next: cd ../backend-api && python check_references.py --scoring best_k   (see docs/volunteer-recordings.md)")


if __name__ == "__main__":
    main()
