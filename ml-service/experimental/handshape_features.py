"""
handshape_features.py  —  PROPOSAL / PROTOTYPE, not wired into the live system.

Status: experimental. This module is a standalone proof-of-concept for the
alphabet-recognition approach discussed as *post-demo* work. It does NOT import
from, modify, or touch gesture_classifier.py, the /api/gestures/classify
endpoint, or ml-service/references_raw/. The working demo is unaffected.

It is NOT a tested recogniser. The geometry below is self-checked on synthetic
hands (run `python handshape_features.py`), but whether it actually improves
recognition on real signs is unvalidated and needs real capture + evaluation.

What it demonstrates (the ideas from the design discussion — see
docs/alphabet-recognition-proposal.md):

  1. Explicit finger-geometry features (curl, spread, which fingers extended)
     -> separates the HANDSHAPE pairs the current DTW struggles with: U/V, M/N.
  2. Head-anchored pointing direction ("up = toward the head")
     -> separates the ORIENTATION pairs the recogniser normalises away: K/P,
        G/Q, U/H.
  3. A confidence-gated "palm-check" trigger (fallback only when unsure)
     -> decides WHEN to ask the learner to turn their palm to the camera,
        instead of pestering them every time.

Landmark format matches MediaPipe Hands: a list/array of 21 points, each
[x, y, z]. Standard index map:
  0 wrist | 1-4 thumb | 5-8 index | 9-12 middle | 13-16 ring | 17-20 pinky
  fingertips = 4, 8, 12, 16, 20     MCP (knuckle) = 1, 5, 9, 13, 17
"""
from __future__ import annotations
import numpy as np

# finger -> (mcp, pip, dip, tip) landmark indices
FINGERS = {
    "thumb":  (1, 2, 3, 4),
    "index":  (5, 6, 7, 8),
    "middle": (9, 10, 11, 12),
    "ring":   (13, 14, 15, 16),
    "pinky":  (17, 18, 19, 20),
}
WRIST = 0


def _v(points, i):
    return np.asarray(points[i], dtype=np.float32)


def _angle(u, w):
    """Angle in degrees between two vectors."""
    nu, nw = np.linalg.norm(u), np.linalg.norm(w)
    if nu < 1e-6 or nw < 1e-6:
        return 0.0
    c = float(np.clip(np.dot(u, w) / (nu * nw), -1.0, 1.0))
    return float(np.degrees(np.arccos(c)))


# ---------------------------------------------------------------------------
# 1. Finger-geometry features  (separates U/V, M/N, ...)
# ---------------------------------------------------------------------------
def finger_curl(points) -> dict:
    """Per finger: the bend angle between the proximal and distal segments.
    ~0 deg = straight/extended, large = curled. Separates extended vs folded
    fingers (the M/N 'how many fingers are down' distinction)."""
    out = {}
    for name, (mcp, pip, dip, tip) in FINGERS.items():
        proximal = _v(points, pip) - _v(points, mcp)
        distal = _v(points, tip) - _v(points, dip)
        out[name] = round(_angle(proximal, distal), 1)
    return out


def extended_fingers(points, curl_threshold=40.0) -> dict:
    """Boolean per finger: is it roughly straight (extended)?"""
    return {k: v < curl_threshold for k, v in finger_curl(points).items()}


def finger_spread(points) -> dict:
    """Angles between adjacent extended-finger directions. The index/middle
    spread is exactly the U (together, small angle) vs V (apart, large) cue."""
    def direction(finger):
        mcp, _, _, tip = FINGERS[finger]
        return _v(points, tip) - _v(points, mcp)

    pairs = [("index", "middle"), ("middle", "ring"), ("ring", "pinky")]
    return {f"{a}-{b}": round(_angle(direction(a), direction(b)), 1) for a, b in pairs}


# ---------------------------------------------------------------------------
# 2. Head-anchored pointing direction  (separates K/P, G/Q, U/H)
# ---------------------------------------------------------------------------
def pointing_direction(points, head_point) -> dict:
    """Which way the hand points, relative to the BODY (robust to camera angle).
    `head_point` is a [x,y,z] pose landmark (e.g. the nose) from MediaPipe
    Holistic. 'up' is defined as toward the head.

    Returns the finger-pointing unit vector, the hand->head unit vector, and a
    coarse label. This is the signal the current recogniser throws away when it
    normalises each hand into its own local frame."""
    # finger-pointing direction: index MCP -> index tip (robust, usually extended)
    point_dir = _v(points, 8) - _v(points, 5)
    n = np.linalg.norm(point_dir)
    point_dir = point_dir / n if n > 1e-6 else point_dir

    to_head = np.asarray(head_point, dtype=np.float32) - _v(points, WRIST)
    nh = np.linalg.norm(to_head)
    to_head = to_head / nh if nh > 1e-6 else to_head

    align = float(np.dot(point_dir, to_head))  # +1 points at head, -1 away
    if align > 0.5:
        label = "up (toward head)"
    elif align < -0.5:
        label = "down (away from head)"
    else:
        # sideways: use screen x of the pointing vector
        label = "right" if point_dir[0] > 0 else "left"

    return {
        "point_dir": [round(float(x), 3) for x in point_dir],
        "to_head": [round(float(x), 3) for x in to_head],
        "alignment": round(align, 3),
        "label": label,
    }


# ---------------------------------------------------------------------------
# 3. Confidence-gated palm-check  (ask only when genuinely unsure)
# ---------------------------------------------------------------------------
def should_request_palm_check(candidate_distances: dict,
                              close_margin=0.15,
                              accept_below=1.2):
    """Decide whether to prompt the learner to turn their palm to the camera.

    candidate_distances: {sign: dtw_distance}  (lower = better match)

    Triggers on the case that actually matters for confusable pairs: the top
    two candidates being too CLOSE together ("it's U or V and I can't tell"),
    not merely low overall confidence. Returns (should_check, reason).
    """
    if not candidate_distances:
        return True, "no candidates"
    ranked = sorted(candidate_distances.items(), key=lambda kv: kv[1])
    best_sign, best = ranked[0]
    if len(ranked) == 1:
        return (best > accept_below), ("weak single match" if best > accept_below else "clear")
    second_sign, second = ranked[1]
    if (second - best) < close_margin:
        return True, f"ambiguous: {best_sign} vs {second_sign} within {close_margin}"
    if best > accept_below:
        return True, f"low confidence (best distance {best:.2f})"
    return False, f"clear: {best_sign}"


# ---------------------------------------------------------------------------
# Self-check on synthetic hands (no camera needed) — run this file directly.
# ---------------------------------------------------------------------------
def _synthetic_hand(index_tip_x, middle_tip_x, pointing="up"):
    """Build a crude 21-point hand. Fingers point +y ('up') or -y ('down').
    index_tip_x / middle_tip_x let us splay the two fingers apart (V) or keep
    them together (U). Only the landmarks the features use need to be sensible."""
    pts = [[0, 0, 0] for _ in range(21)]
    s = 1.0 if pointing == "up" else -1.0
    pts[WRIST] = [0.0, 0.0, 0.0]
    # index: mcp(5) low, tip(8) high; middle: mcp(9), tip(12)
    pts[5] = [0.0, s * 0.2, 0]; pts[6] = [0.0, s * 0.35, 0]; pts[7] = [0.0, s * 0.5, 0]
    pts[8] = [index_tip_x, s * 0.7, 0]
    pts[9] = [0.1, s * 0.2, 0]; pts[10] = [0.1, s * 0.35, 0]; pts[11] = [0.1, s * 0.5, 0]
    pts[12] = [middle_tip_x, s * 0.7, 0]
    return pts


if __name__ == "__main__":
    print("handshape_features.py — self-check (synthetic hands, no camera)\n")

    u_hand = _synthetic_hand(index_tip_x=0.0, middle_tip_x=0.1)   # fingers together
    v_hand = _synthetic_hand(index_tip_x=-0.3, middle_tip_x=0.4)  # fingers splayed

    u_spread = finger_spread(u_hand)["index-middle"]
    v_spread = finger_spread(v_hand)["index-middle"]
    print(f"1. Finger spread (U vs V):  U index-middle = {u_spread} deg,  V = {v_spread} deg")
    assert v_spread > u_spread + 10, "V should splay wider than U"
    print("   -> V is measurably wider than U. The U/V handshape cue works.\n")

    head = [0.0, 2.0, 0.0]  # head above the hand
    up = pointing_direction(_synthetic_hand(0, 0.1, "up"), head)["label"]
    down = pointing_direction(_synthetic_hand(0, 0.1, "down"), head)["label"]
    print(f"2. Head-anchored direction (K vs P):  fingers-up -> '{up}',  fingers-down -> '{down}'")
    assert "up" in up and "down" in down, "direction labels should flip"
    print("   -> Same handshape, opposite direction, cleanly separated.\n")

    clear = should_request_palm_check({"hello": 0.4, "goodbye": 1.1})
    ambiguous = should_request_palm_check({"u": 0.9, "v": 0.97})
    print(f"3. Palm-check gate:  clear case -> {clear}")
    print(f"                     ambiguous U/V -> {ambiguous}")
    assert clear[0] is False and ambiguous[0] is True, "gate should only fire when unsure"
    print("   -> Fires only on the ambiguous pair, stays quiet when confident.\n")

    print("All self-checks passed. NOTE: this validates the geometry only — not")
    print("real-world recognition accuracy, which needs camera capture + evaluation.")
