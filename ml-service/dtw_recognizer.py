"""
DTW sign classifier - proof of concept.

    py -3.12 dtw_recognizer.py record <label>    # save reference examples
    py -3.12 dtw_recognizer.py compare <label>   # test a live attempt

SPACE to start/stop a recording, 'q' to quit.

Each frame's hand landmarks are normalized (position/scale/rotation
invariant) and flattened into a vector. A recording is a sequence of
these, saved as .npy under references/<label>/. Comparing runs DTW
against each saved reference and votes on how many are a close match.

Placeholder gesture until lessons are added
"""

import os
import sys
import time

import cv2
import numpy as np
import mediapipe as mp
from mediapipe.tasks.python import vision
from mediapipe.tasks.python.core.base_options import BaseOptions

MODEL_URL = (
    "https://storage.googleapis.com/mediapipe-models/holistic_landmarker/"
    "holistic_landmarker/float16/1/holistic_landmarker.task"
)
SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
MODEL_PATH = os.path.join(SCRIPT_DIR, "holistic_landmarker.task")
REFERENCES_DIR = os.path.join(SCRIPT_DIR, "references")

# Sits between same-gesture distances across sessions (~0.5-0.8 observed)
# and different-gesture distances (~1.9-2.8 observed). Used per-reference
# below, combined with majority voting rather than trusting a single
# closest match. Revisit once tested against real Lesson 1 signs and more
# people's hands.
MATCH_THRESHOLD = 2.0

# Fraction of references that must be within MATCH_THRESHOLD for the
# overall verdict to be MATCH. 0.5 = majority vote. Using every reference
# rather than just the single closest one is more robust - one unusually
# loose recording shouldn't be able to single-handedly call a match.
VOTE_FRACTION = 0.5


def ensure_model():
    import urllib.request
    if not os.path.exists(MODEL_PATH):
        print(f"Downloading model to {MODEL_PATH} ...")
        urllib.request.urlretrieve(MODEL_URL, MODEL_PATH)
        print("Download complete.")


def landmarks_to_vector(landmarks):
    """21 landmarks -> flat list of 63 floats, normalized so the result
    reflects hand SHAPE only - not where the hand is in frame, how far it
    is from the camera, or which way it's rotated.

    Without this, two very different hand shapes held in roughly the same
    spot on screen look almost identical to DTW, since raw x/y/z are
    dominated by position-in-frame rather than finger configuration - this
    is what caused "different" gestures to score as close matches.

    Three corrections, stacked:
    1. Translation: make every landmark relative to the wrist (landmark 0),
       so moving your hand around the frame doesn't change the vector.
    2. Scale: divide by the wrist-to-middle-knuckle distance, so being
       closer to or further from the camera doesn't change it either.
    3. Rotation: steps 1-2 alone still leave the hand's orientation baked
       into the numbers - the same gesture held at a different angle can
       still look different. Fixed by building a small local coordinate
       system out of the hand itself (v1 pointing toward the middle
       knuckle, v2 toward the index knuckle, v3 perpendicular to both),
       then expressing every landmark in that hand-relative frame instead
       of the camera's frame. Rotate your hand any way you like - these
       three reference vectors rotate with it, so the numbers stay put.
    """
    if not landmarks:
        return [0.0] * 63

    pts = np.array([[lm.x, lm.y, lm.z] for lm in landmarks], dtype=np.float32)
    wrist = pts[0]
    relative = pts - wrist  # translation-invariant

    scale = np.linalg.norm(pts[9] - wrist)  # wrist -> middle finger MCP
    if scale < 1e-6:
        scale = 1e-6  # avoid divide-by-zero on a bad frame
    relative = relative / scale  # scale-invariant

    # Build a local coordinate frame from the hand itself.
    v1 = relative[9]  # wrist -> middle finger MCP, already unit-ish length
    v1_norm = np.linalg.norm(v1)
    if v1_norm < 1e-6:
        v1_norm = 1e-6
    v1 = v1 / v1_norm

    # Second reference direction: wrist -> index finger MCP, orthogonalized
    # against v1 (Gram-Schmidt) so v1/v2 are perpendicular.
    v2_raw = relative[5]
    v2 = v2_raw - np.dot(v2_raw, v1) * v1
    v2_norm = np.linalg.norm(v2)
    if v2_norm < 1e-6:
        v2_norm = 1e-6
    v2 = v2 / v2_norm

    v3 = np.cross(v1, v2)  # perpendicular to both - completes the frame

    basis = np.stack([v1, v2, v3])  # 3x3
    normalized = relative @ basis.T  # re-express every point in this frame

    return normalized.flatten().tolist()


def frame_to_feature_vector(result):
    """Combine both hands into one 126-number vector for this frame."""
    left = landmarks_to_vector(result.left_hand_landmarks)
    right = landmarks_to_vector(result.right_hand_landmarks)
    return np.array(left + right, dtype=np.float32)


def dtw_distance(seq_a, seq_b):
    """Classic DTW: find the cheapest way to align two sequences of
    vectors, where the cost of matching two frames is their Euclidean
    distance. Returns a single number - lower means more similar."""
    n, m = len(seq_a), len(seq_b)
    cost = np.full((n + 1, m + 1), np.inf)
    cost[0, 0] = 0.0

    for i in range(1, n + 1):
        for j in range(1, m + 1):
            dist = np.linalg.norm(seq_a[i - 1] - seq_b[j - 1])
            cost[i, j] = dist + min(cost[i - 1, j], cost[i, j - 1], cost[i - 1, j - 1])

    # Normalize by path length so longer recordings aren't unfairly
    # penalized just for having more frames to sum over.
    return cost[n, m] / (n + m)


def capture_sequence(cap, landmarker, start_time, window_title):
    """Show the webcam feed. Press SPACE to start recording frames, SPACE
    again to stop. Returns the recorded sequence as a list of feature
    vectors, or None if the user quit instead."""
    recording = False
    frames = []

    print("Press SPACE to start recording, SPACE again to stop, 'q' to quit.")
    while True:
        ok, frame = cap.read()
        if not ok:
            print("Failed to read frame from webcam.")
            return None

        rgb_frame = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
        mp_image = mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb_frame)
        timestamp_ms = int((time.time() - start_time) * 1000)
        result = landmarker.detect_for_video(mp_image, timestamp_ms)

        if recording:
            frames.append(frame_to_feature_vector(result))
            cv2.putText(frame, f"RECORDING ({len(frames)} frames)", (10, 30),
                        cv2.FONT_HERSHEY_SIMPLEX, 0.8, (0, 0, 255), 2)
        else:
            cv2.putText(frame, "Press SPACE to start", (10, 30),
                        cv2.FONT_HERSHEY_SIMPLEX, 0.8, (0, 255, 0), 2)

        cv2.imshow(window_title, frame)
        key = cv2.waitKey(1) & 0xFF

        if key == ord(" "):
            if not recording:
                recording = True
                frames = []
            else:
                # Stopped - hand this recording back to the caller.
                return frames
        elif key == ord("q"):
            return None


def run_record(label):
    ensure_model()
    label_dir = os.path.join(REFERENCES_DIR, label)
    os.makedirs(label_dir, exist_ok=True)
    existing = [f for f in os.listdir(label_dir) if f.endswith(".npy")]

    options = vision.HolisticLandmarkerOptions(
        base_options=BaseOptions(model_asset_path=MODEL_PATH),
        running_mode=vision.RunningMode.VIDEO,
    )
    cap = cv2.VideoCapture(0)
    start_time = time.time()

    rep_index = len(existing)
    with vision.HolisticLandmarker.create_from_options(options) as landmarker:
        while True:
            sequence = capture_sequence(cap, landmarker, start_time,
                                         f"Recording '{label}' - SPACE to start/stop, q to quit")
            if sequence is None:
                break
            if len(sequence) < 3:
                print("Recording too short, discarded - try again.")
                continue

            out_path = os.path.join(label_dir, f"rep_{rep_index}.npy")
            np.save(out_path, np.array(sequence))
            print(f"Saved {out_path} ({len(sequence)} frames)")
            rep_index += 1

    cap.release()
    cv2.destroyAllWindows()


def run_compare(label):
    ensure_model()
    label_dir = os.path.join(REFERENCES_DIR, label)
    if not os.path.isdir(label_dir):
        print(f"No references found for '{label}'. Record some first:")
        print(f"    py -3.12 dtw_recognizer.py record {label}")
        return

    references = []
    for fname in sorted(os.listdir(label_dir)):
        if fname.endswith(".npy"):
            references.append(np.load(os.path.join(label_dir, fname)))

    if not references:
        print(f"No reference recordings saved yet for '{label}'.")
        return
    print(f"Loaded {len(references)} reference recording(s) for '{label}'.")

    options = vision.HolisticLandmarkerOptions(
        base_options=BaseOptions(model_asset_path=MODEL_PATH),
        running_mode=vision.RunningMode.VIDEO,
    )
    cap = cv2.VideoCapture(0)
    start_time = time.time()

    with vision.HolisticLandmarker.create_from_options(options) as landmarker:
        sequence = capture_sequence(cap, landmarker, start_time,
                                     f"Testing against '{label}' - SPACE to start/stop, q to quit")

    cap.release()
    cv2.destroyAllWindows()

    if not sequence or len(sequence) < 3:
        print("No usable attempt captured.")
        return

    distances = [dtw_distance(sequence, ref) for ref in references]
    votes = sum(1 for d in distances if d <= MATCH_THRESHOLD)
    vote_ratio = votes / len(distances)

    print("\nDTW distance to each reference:")
    for i, d in enumerate(distances):
        flag = "within threshold" if d <= MATCH_THRESHOLD else ""
        print(f"  rep_{i}: {d:.3f} {flag}")

    print(f"\n{votes}/{len(distances)} references within threshold ({MATCH_THRESHOLD}) "
          f"= {vote_ratio:.0%}")
    if vote_ratio >= VOTE_FRACTION:
        print(f"MATCH - looks like '{label}'")
    else:
        print(f"NO MATCH - doesn't look like '{label}'")


def main():
    if len(sys.argv) != 3 or sys.argv[1] not in ("record", "compare"):
        print("Usage:")
        print("  py -3.12 dtw_recognizer.py record <label>")
        print("  py -3.12 dtw_recognizer.py compare <label>")
        sys.exit(1)

    mode, label = sys.argv[1], sys.argv[2]
    if mode == "record":
        run_record(label)
    else:
        run_compare(label)


if __name__ == "__main__":
    main()
