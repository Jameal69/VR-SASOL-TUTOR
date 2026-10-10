"""
Gesture classification logic, adapted from Christian's ml-service/dtw_recognizer.py
so the backend can import and call it directly, without needing OpenCV, MediaPipe,
or webcam access.

UPDATED: now compares the live attempt against every recorded sign, not just the
target one, and requires the target sign to genuinely be the closest match, not
just "under the threshold" in isolation. This fixes the false-positive issue where
performing Goodbye while checking for Hello still scored 80-100%, since the old
version never even looked at Goodbye's reference data during a Hello check.

UPDATED (after re-recording): tracking dropouts mid-sign are now filled with
the last seen hand, which was the biggest accuracy win on the new recordings.
Wrist path relative to where the hand was first seen was tried and made things
WORSE (the hand enters from the bottom of the frame, so "first seen" is noise).
Optional wrist screen position is available via TRAJECTORY_WEIGHT, off by default.

References are now saved as RAW landmarks (references_raw/, 126 floats per
frame: left hand 21x[x,y,z] then right hand, zeros if a hand wasn't seen), and
features are computed here at load time. Changing the features no longer needs
a re-record. The old references/ folder holds pre-normalized vectors and is no
longer read.
"""
import os
import numpy as np

REFERENCES_DIR = os.path.join(
    os.path.dirname(os.path.abspath(__file__)), "..", "..", "ml-service", "references_raw"
)

# From live tests on 2026-09-25 (shape-only + gap fill): correct attempts were
# <= 1.41 to every reference of their sign, wrong-sign attempts >= ~1.5.
MATCH_THRESHOLD = 1.5

# Tracking-quality gate, checked before classifying. MediaPipe Holistic finds
# the hands via the body pose, so when a shoulder or the head leaves the frame
# the hand landmarks come back distorted and EVERY sign scores far away (seen
# live: all distances ~2.5). Such attempts get "please retry", not NO MATCH.
# Starting values; tune from the debug print below.
MIN_BODY_VISIBLE = 0.8   # fraction of frames with head + both shoulders in view

# How each sign's references are combined into one score.
#   "all"    - average and vote over EVERY reference of the sign (current
#              behaviour; tuned on one signer's recordings).
#   "best_k" - only the BEST_K closest references count. Use this once
#              recordings from several signers are merged: an attempt only needs
#              to resemble the few references made by people who sign like the
#              learner, instead of being outvoted by everyone else's style.
# Switch only after check_references.py --scoring best_k and a live test
# (see docs/volunteer-recordings.md).
SCORING = "all"
BEST_K = 3
MIN_HAND_VISIBLE = 0.3   # fraction of frames with at least one hand detected
# Very short attempts (or ones where the hand was mostly lost) gave unreliable
# results live: on 10 Oct the attempts with 4-5 hand frames failed, while every
# successful one had 8+. Fewer than this many hand frames -> "too_short" retry.
MIN_HAND_FRAMES = 8
VOTE_FRACTION = 0.5

# TEMPORARY: prints real distance numbers for every classification, so we can
# gather real correct/incorrect attempts and pick a proper MATCH_THRESHOLD
# instead of guessing. Set to False once tuning is done.
DEBUG_PRINT_DISTANCES = True

# Drop frames with no hand at the START and END of a sequence only (before the
# hand comes up / after it drops). Gaps in the middle are kept, since removing
# all empty frames made things worse.
TRIM_EMPTY_EDGES = True
# When tracking loses a hand for a few frames mid-sign, carry the last seen
# hand forward instead of leaving all-zero frames (which DTW scores as huge
# differences). On the 2026-09-25 recordings: 18/20 -> 20/20 nearest own sign.
FILL_GAPS = True
# Divide DTW cost by the real length of the warping path instead of (n + m).
# The old way gave sequences of similar length an unfair discount, which
# favoured Goodbye (its recordings are the shortest).
NORMALIZE_BY_PATH = True
# How much the wrist's position on screen counts relative to hand shape.
# 0 = shape only. 1 separated signs slightly better offline, but depends on
# where you sit relative to the camera. Compare values with check_references.py.
TRAJECTORY_WEIGHT = 0.0


def _landmarks_to_vector(points):
    if not points:
        return [0.0] * 63

    pts = np.array(points, dtype=np.float32)
    wrist = pts[0]
    relative = pts - wrist

    scale = np.linalg.norm(pts[9] - wrist)
    if scale < 1e-6:
        scale = 1e-6
    relative = relative / scale

    v1 = relative[9]
    v1_norm = np.linalg.norm(v1)
    if v1_norm < 1e-6:
        v1_norm = 1e-6
    v1 = v1 / v1_norm

    v2_raw = relative[5]
    v2 = v2_raw - np.dot(v2_raw, v1) * v1
    v2_norm = np.linalg.norm(v2)
    if v2_norm < 1e-6:
        v2_norm = 1e-6
    v2 = v2 / v2_norm

    v3 = np.cross(v1, v2)

    basis = np.stack([v1, v2, v3])
    normalized = relative @ basis.T
    return normalized.flatten().tolist()


def _raw_frame(left_hand, right_hand):
    """One frame of raw landmarks as 126 floats (left then right), zeros for a
    hand that wasn't seen. Same layout the recorder saves references in."""
    left = np.asarray(left_hand, dtype=np.float32).reshape(-1) if left_hand else np.zeros(63, np.float32)
    right = np.asarray(right_hand, dtype=np.float32).reshape(-1) if right_hand else np.zeros(63, np.float32)
    return np.concatenate([left, right])


def _trim_empty_edges(raw):
    present = np.flatnonzero(np.any(raw != 0, axis=1))
    if not len(present):
        return raw[:0]
    return raw[present[0]:present[-1] + 1]


def _fill_gaps(raw):
    """Per hand, carry the last seen landmarks forward over frames where
    tracking lost that hand. Frames before the hand is first seen stay empty."""
    raw = raw.copy()
    for hand in (slice(0, 63), slice(63, 126)):
        last = None
        for i in range(len(raw)):
            if np.any(raw[i, hand]):
                last = raw[i, hand]
            elif last is not None:
                raw[i, hand] = last
    return raw


def _hand_trajectory(hand_raw):
    """(frames, 63) raw landmarks for one hand -> (frames, 2) wrist x/y on
    screen, centred and x5 so a typical move is about 1 unit, comparable to
    shape values. Zeros where the hand is missing."""
    out = np.zeros((len(hand_raw), 2), dtype=np.float32)
    present = np.any(hand_raw != 0, axis=1)
    pts = hand_raw.reshape(len(hand_raw), 21, 3)
    out[present] = (pts[present, 0, :2] - 0.5) * 5
    return out


def _sequence_features(raw):
    """Raw (frames, 126) sequence -> (frames, 130) features: per hand, 63 shape
    values plus 2 weighted wrist-path values."""
    raw = np.asarray(raw, dtype=np.float32).reshape(-1, 126)
    if TRIM_EMPTY_EDGES:
        raw = _trim_empty_edges(raw)
    if FILL_GAPS:
        raw = _fill_gaps(raw)
    parts = []
    for hand in (raw[:, :63], raw[:, 63:]):
        shape = np.array(
            [_landmarks_to_vector(f.reshape(21, 3).tolist() if np.any(f) else []) for f in hand],
            dtype=np.float32,
        ).reshape(len(hand), 63)
        parts += [shape, _hand_trajectory(hand) * TRAJECTORY_WEIGHT]
    return np.concatenate(parts, axis=1)


def _dtw_distance(seq_a, seq_b):
    n, m = len(seq_a), len(seq_b)
    if n == 0 or m == 0:
        return float("inf")
    d = np.linalg.norm(seq_a[:, None, :] - seq_b[None, :, :], axis=2)
    cost = np.full((n + 1, m + 1), np.inf)
    steps = np.zeros((n + 1, m + 1))
    cost[0, 0] = 0.0
    for i in range(1, n + 1):
        for j in range(1, m + 1):
            prev = min(
                (cost[i - 1, j], steps[i - 1, j]),
                (cost[i, j - 1], steps[i, j - 1]),
                (cost[i - 1, j - 1], steps[i - 1, j - 1]),
            )
            cost[i, j] = d[i - 1, j - 1] + prev[0]
            steps[i, j] = prev[1] + 1
    if NORMALIZE_BY_PATH:
        return float(cost[n, m] / steps[n, m])
    return float(cost[n, m] / (n + m))


def _load_all_sign_distances(sequence, allowed_signs=None):
    """Compares the live sequence against every sign that has reference
    recordings on disk, not just one. Returns {sign_name: [distance, ...]}.
    If allowed_signs is given, only those signs are compared (lesson scoping)."""
    all_distances = {}
    if not os.path.isdir(REFERENCES_DIR):
        return all_distances

    for entry in sorted(os.listdir(REFERENCES_DIR)):
        sign_dir = os.path.join(REFERENCES_DIR, entry)
        if not os.path.isdir(sign_dir):
            continue
        if allowed_signs is not None and entry not in allowed_signs:
            continue

        refs = []
        for fname in sorted(os.listdir(sign_dir)):
            if fname.endswith(".npy"):
                ref = _sequence_features(np.load(os.path.join(sign_dir, fname)))
                if len(ref) >= 3:
                    refs.append(ref)

        if refs:
            all_distances[entry] = [_dtw_distance(sequence, ref) for ref in refs]

    return all_distances


def _scoring_distances(distances):
    """The distances that count for a sign under the current SCORING mode."""
    if SCORING == "best_k":
        return sorted(distances)[:BEST_K]
    return list(distances)


def sign_score(distances):
    """One number per sign: the average of its counted distances (lower = closer)."""
    counted = _scoring_distances(distances)
    return sum(counted) / len(counted)


def tracking_problem(landmark_sequence):
    """Returns "body_not_visible", "hand_not_visible", "too_short", or None if
    tracking was good enough to classify. Frames without a "body" flag (older
    clients) skip the body check."""
    if not landmark_sequence:
        return "hand_not_visible"

    hand_frames = sum(1 for f in landmark_sequence if f.get("left_hand") or f.get("right_hand"))
    hand_pct = hand_frames / len(landmark_sequence)
    body_flags = [f.get("body") for f in landmark_sequence if f.get("body") is not None]
    body_pct = sum(1 for b in body_flags if b) / len(body_flags) if body_flags else None

    if DEBUG_PRINT_DISTANCES:
        body_text = f"{body_pct * 100:.0f}%" if body_pct is not None else "n/a (old client)"
        print(f"[gesture_classifier] tracking: hand visible {hand_pct * 100:.0f}% ({hand_frames} frames), "
              f"head+shoulders visible {body_text}")

    if body_pct is not None and body_pct < MIN_BODY_VISIBLE:
        return "body_not_visible"
    if hand_pct < MIN_HAND_VISIBLE:
        return "hand_not_visible"
    if hand_frames < MIN_HAND_FRAMES:
        return "too_short"
    return None


def classify_sequence(landmark_sequence, sign_label, allowed_signs=None):
    """allowed_signs: optional list of sign labels to compare against, e.g. the
    signs in the target's lesson, so a "2" in the numbers lesson can't lose to
    a "V" from the alphabet. None = compare against every recorded sign."""
    raw = np.array(
        [_raw_frame(frame.get("left_hand", []), frame.get("right_hand", [])) for frame in landmark_sequence],
        dtype=np.float32,
    ).reshape(-1, 126)
    if DEBUG_PRINT_DISTANCES and len(raw):
        left_pct = float(np.mean(np.any(raw[:, :63] != 0, axis=1))) * 100
        right_pct = float(np.mean(np.any(raw[:, 63:] != 0, axis=1))) * 100
        print(f"[gesture_classifier] LIVE hands detected: left={left_pct:.0f}% right={right_pct:.0f}% frames={len(raw)}")
    sequence = _sequence_features(raw)
    if len(sequence) < 3:
        return "unknown", 0.0

    all_sign_distances = _load_all_sign_distances(sequence, allowed_signs)

    if sign_label not in all_sign_distances:
        return "unknown", 0.0

    if DEBUG_PRINT_DISTANCES:
        if allowed_signs is not None:
            print(f"[gesture_classifier] comparing within lesson: {', '.join(sorted(allowed_signs))}")
        mode = f"best {BEST_K}" if SCORING == "best_k" else "all refs"
        print(f"[gesture_classifier] Live attempt, checking against '{sign_label}' (scoring: {mode}):")
        for sign, dists in all_sign_distances.items():
            marker = "  <-- target" if sign == sign_label else ""
            rounded = [round(float(d), 3) for d in dists]
            print(f"    {sign}: avg={sign_score(dists):.3f}  distances={rounded}{marker}")

    target_distances = _scoring_distances(all_sign_distances[sign_label])
    votes = sum(1 for d in target_distances if d <= MATCH_THRESHOLD)
    confidence = votes / len(target_distances)

    target_avg = sign_score(all_sign_distances[sign_label])
    other_averages = [
        sign_score(dists)
        for sign, dists in all_sign_distances.items()
        if sign != sign_label
    ]
    # If there's nothing else recorded yet, this is trivially true, which is
    # correct, there's nothing else it could be confused with.
    is_closest_match = all(target_avg <= other_avg for other_avg in other_averages)

    if DEBUG_PRINT_DISTANCES:
        print(f"    is_closest_match={is_closest_match}  confidence={confidence:.2f}")

    # A different sign being closer means the attempt was that sign, not a weak
    # version of the target, so it earns no credit (feedback scores use this).
    if not is_closest_match:
        confidence = 0.0

    predicted_sign = sign_label if (confidence >= VOTE_FRACTION and is_closest_match) else "unknown"
    return predicted_sign, confidence
