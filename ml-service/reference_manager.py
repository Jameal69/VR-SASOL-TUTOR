"""
Reference manager  -  view and curate the recognizer's reference recordings
===========================================================================
A visual tool for quality-controlling the sign references WITHOUT touching the
matcher. It plays every reference back as a hand skeleton so you can see what
was signed, and lets you mark any recording USED or EXCLUDED.

How "exclude" works (and why it's safe):
  - The recogniser (gesture_classifier.py) loads every .npy it finds in
    references_raw/<sign>/ , and re-reads that folder on each classification.
  - So excluding a recording just MOVES its file from references_raw/<sign>/
    into references_retired/<sign>/ . The matcher stops using it on the next
    classification. Re-including moves it back. Nothing is deleted, and NO
    code in the matcher changes - this tool only moves files.

references_raw      = USED      (what the recogniser feeds on right now)
references_retired  = EXCLUDED  (parked, ignored by the recogniser)

Run:  double-click manage.bat   (or:  py -3.12 reference_manager.py)
Needs: Python 3.12 +  pip install opencv-python numpy   (same as the recorder)

Controls (in the window):
    n / b        next / previous recording
    space        replay
    x            toggle this recording USED <-> EXCLUDED  (moves the file)
    f            cycle filter: ALL -> USED only -> EXCLUDED only
    q            quit
Every change is logged to reference_manager_log.csv next to this script.
"""
import os
import re
import csv
import glob
import shutil
import datetime

import cv2
import numpy as np

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
RAW = os.path.join(SCRIPT_DIR, "references_raw")       # USED by the recogniser
RET = os.path.join(SCRIPT_DIR, "references_retired")   # EXCLUDED (ignored)
LOG = os.path.join(SCRIPT_DIR, "reference_manager_log.csv")

W, H = 760, 560
LEFT_COL = (230, 190, 60)
RIGHT_COL = (60, 210, 240)
HAND_CONNECTIONS = [
    (0,1),(1,2),(2,3),(3,4), (0,5),(5,6),(6,7),(7,8), (5,9),(9,10),(10,11),(11,12),
    (9,13),(13,14),(14,15),(15,16), (13,17),(17,18),(18,19),(19,20), (0,17),
]


def next_free_index(sign_dir):
    if not os.path.isdir(sign_dir):
        return 0
    nums = [int(m.group(1)) for f in os.listdir(sign_dir)
            for m in [re.match(r"rep_(\d+)\.npy$", f)] if m]
    return max(nums) + 1 if nums else 0


def scan():
    """Every reference under both folders, with its USED/EXCLUDED status."""
    items = []
    for base, status in ((RAW, "USED"), (RET, "EXCLUDED")):
        for path in sorted(glob.glob(os.path.join(base, "*", "rep_*.npy"))):
            sign = os.path.basename(os.path.dirname(path))
            items.append({"sign": sign, "status": status, "path": path})
    items.sort(key=lambda it: (it["sign"], it["status"], it["path"]))
    return items


def counts_for(items, sign):
    u = sum(1 for it in items if it["sign"] == sign and it["status"] == "USED")
    e = sum(1 for it in items if it["sign"] == sign and it["status"] == "EXCLUDED")
    return u, e


def log_change(action, sign, src, dst):
    new = not os.path.exists(LOG)
    with open(LOG, "a", newline="") as fh:
        w = csv.writer(fh)
        if new:
            w.writerow(["timestamp", "action", "sign", "from", "to"])
        w.writerow([datetime.datetime.now().isoformat(timespec="seconds"), action, sign,
                    os.path.relpath(src, SCRIPT_DIR), os.path.relpath(dst, SCRIPT_DIR)])


def toggle(item):
    """Move a recording between USED (references_raw) and EXCLUDED (references_retired)."""
    sign, src = item["sign"], item["path"]
    if item["status"] == "USED":
        dst_dir = os.path.join(RET, sign); action = "EXCLUDE"
    else:
        dst_dir = os.path.join(RAW, sign); action = "INCLUDE"
    os.makedirs(dst_dir, exist_ok=True)
    dst = os.path.join(dst_dir, f"rep_{next_free_index(dst_dir)}.npy")
    shutil.move(src, dst)
    # tidy: drop the source folder if it's now empty
    src_dir = os.path.dirname(src)
    if os.path.isdir(src_dir) and not os.listdir(src_dir):
        os.rmdir(src_dir)
    log_change(action, sign, src, dst)
    return dst


def hand_points(vec, offset):
    half = vec[offset:offset + 63]
    if not np.any(half):
        return None
    return half.reshape(21, 3)[:, :2]


def draw(seq, fi, item, shown_idx, shown_total, counts, filt, pending):
    n = len(seq)
    cur = fi % n
    canvas = np.full((H, W, 3), 32, dtype=np.uint8)
    vec = seq[cur]
    for offset, col in ((0, LEFT_COL), (63, RIGHT_COL)):
        pts = hand_points(vec, offset)
        if pts is None:
            continue
        px = [(int(x * W), int(y * H)) for x, y in pts]
        for a, b in HAND_CONNECTIONS:
            cv2.line(canvas, px[a], px[b], col, 2)
        for p in px:
            cv2.circle(canvas, p, 4, col, -1)

    used = item["status"] == "USED"
    scol = (80, 220, 80) if used else (90, 90, 230)
    cv2.putText(canvas, f"sign '{item['sign']}'   [{shown_idx+1}/{shown_total} shown]   filter: {filt}",
                (12, 26), cv2.FONT_HERSHEY_SIMPLEX, 0.6, (255, 255, 255), 1)
    cv2.putText(canvas, f"STATUS: {item['status']}", (12, 54),
                cv2.FONT_HERSHEY_SIMPLEX, 0.8, scol, 2)
    cv2.putText(canvas, f"'{item['sign']}':  {counts[0]} used,  {counts[1]} excluded", (12, 82),
                cv2.FONT_HERSHEY_SIMPLEX, 0.6, (200, 200, 200), 1)
    cv2.putText(canvas, f"file: {os.path.basename(item['path'])}", (12, 106),
                cv2.FONT_HERSHEY_SIMPLEX, 0.5, (150, 150, 150), 1)
    msg = "press x -> EXCLUDE (stop using)" if used else "press x -> INCLUDE (use again)"
    cv2.putText(canvas, msg, (12, H - 44), cv2.FONT_HERSHEY_SIMPLEX, 0.55, (0, 200, 255), 1)
    cv2.putText(canvas, "n next  b back  space replay  x toggle use  f filter  q quit",
                (12, H - 18), cv2.FONT_HERSHEY_SIMPLEX, 0.5, (200, 200, 200), 1)
    cv2.putText(canvas, f"frame {cur+1}/{n}", (W - 150, 26),
                cv2.FONT_HERSHEY_SIMPLEX, 0.5, (190, 190, 190), 1)
    return canvas


def main():
    if not os.path.isdir(RAW) and not os.path.isdir(RET):
        print(f"No references found next to this script.\nExpected: {RAW}\n      or: {RET}")
        print("Put reference_manager.py in ml-service/ (beside references_raw).")
        return
    items = scan()
    filters = ["ALL", "USED", "EXCLUDED"]
    fi_filter = 0
    print(f"Loaded {len(items)} reference recording(s). Opening window...")
    print("n/b move, space replay, x toggle use/exclude, f filter, q quit.\n")

    i = 0
    while True:
        shown = [it for it in items if filters[fi_filter] == "ALL" or it["status"] == filters[fi_filter]]
        if not shown:
            fi_filter = 0
            shown = items
        i = max(0, min(i, len(shown) - 1))
        item = shown[i]
        seq = np.load(item["path"])
        counts = counts_for(items, item["sign"])
        fcur = 0
        while True:
            canvas = draw(seq, fcur, item, i, len(shown), counts, filters[fi_filter], None)
            cv2.imshow("Reference manager", canvas)
            key = cv2.waitKey(45) & 0xFF
            fcur += 1
            if key == ord("n"):
                i = min(i + 1, len(shown) - 1); break
            elif key == ord("b"):
                i = max(i - 1, 0); break
            elif key == ord(" "):
                fcur = 0
            elif key == ord("f"):
                fi_filter = (fi_filter + 1) % len(filters); i = 0; break
            elif key == ord("x"):
                toggle(item)
                items = scan()  # re-read after the move
                break
            elif key == ord("q"):
                cv2.destroyAllWindows()
                u = sum(1 for it in items if it["status"] == "USED")
                e = sum(1 for it in items if it["status"] == "EXCLUDED")
                print(f"\nDone. {u} used, {e} excluded. Changes logged to reference_manager_log.csv.")
                return


if __name__ == "__main__":
    main()
