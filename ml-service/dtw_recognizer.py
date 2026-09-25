"""
DTW sign classifier - proof of concept. See README.md for setup and usage.

Placeholder gesture until lessons are added

UPDATED: recordings are now saved as RAW landmarks in references_raw/ (126
floats per frame: left hand 21x[x,y,z] then right hand, zeros if a hand wasn't
seen). Normalization and comparison live in ONE place,
backend-api/app/gesture_classifier.py, which 'compare' below imports, so the
recorder and the backend can't drift apart. Changing the features there no
longer needs a re-record.
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
REFERENCES_DIR = os.path.join(SCRIPT_DIR, "references_raw")
BACKEND_DIR = os.path.join(SCRIPT_DIR, "..", "backend-api")


def ensure_model():
    import urllib.request
    if not os.path.exists(MODEL_PATH):
        print(f"Downloading model to {MODEL_PATH} ...")
        urllib.request.urlretrieve(MODEL_URL, MODEL_PATH)
        print("Download complete.")


def make_options():
    # Same detection thresholds as landmark_streamer.py, so references and
    # live attempts through Unity see hands the same way.
    return vision.HolisticLandmarkerOptions(
        base_options=BaseOptions(model_asset_path=MODEL_PATH),
        running_mode=vision.RunningMode.VIDEO,
        min_pose_detection_confidence=0.3,
        min_pose_landmarks_confidence=0.3,
        min_hand_landmarks_confidence=0.3,
    )


def frame_to_raw_vector(result):
    """Both hands' raw x/y/z into one 126-number vector (left then right),
    zeros for a hand that wasn't seen. No normalization here on purpose."""
    def flat(landmarks):
        if not landmarks:
            return [0.0] * 63
        return [c for lm in landmarks for c in (lm.x, lm.y, lm.z)]
    return np.array(flat(result.left_hand_landmarks) + flat(result.right_hand_landmarks), dtype=np.float32)


def capture_sequence(cap, landmarker, start_time, window_title):
    """Show the webcam feed. Press SPACE to start recording frames, SPACE
    again to stop. Returns the recorded sequence as a list of raw frame
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
            frames.append(frame_to_raw_vector(result))
            cv2.putText(frame, f"RECORDING ({len(frames)} frames)", (10, 30),
                        cv2.FONT_HERSHEY_SIMPLEX, 0.8, (0, 0, 255), 2)
        else:
            cv2.putText(frame, "Press SPACE to start", (10, 30),
                        cv2.FONT_HERSHEY_SIMPLEX, 0.8, (0, 255, 0), 2)

        # Which hand slot is filled - references must use the same slot as
        # live attempts (the streamer preview shows the same labels).
        for i, (label, seen) in enumerate((("LEFT hand", result.left_hand_landmarks),
                                           ("RIGHT hand", result.right_hand_landmarks))):
            cv2.putText(frame, label if seen else label.lower() + " lost", (10, 60 + 28 * i),
                        cv2.FONT_HERSHEY_SIMPLEX, 0.8, (0, 200, 0) if seen else (0, 0, 255), 2)

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

    cap = cv2.VideoCapture(0)
    start_time = time.time()

    rep_index = len(existing)
    with vision.HolisticLandmarker.create_from_options(make_options()) as landmarker:
        while True:
            sequence = capture_sequence(cap, landmarker, start_time,
                                         f"Recording '{label}' - SPACE to start/stop, q to quit")
            if sequence is None:
                break
            if len(sequence) < 3:
                print("Recording too short, discarded - try again.")
                continue

            arr = np.array(sequence)
            out_path = os.path.join(label_dir, f"rep_{rep_index}.npy")
            np.save(out_path, arr)
            left_pct = float(np.mean(np.any(arr[:, :63] != 0, axis=1))) * 100
            right_pct = float(np.mean(np.any(arr[:, 63:] != 0, axis=1))) * 100
            print(f"Saved {out_path} ({len(sequence)} frames, left={left_pct:.0f}% right={right_pct:.0f}%)")
            rep_index += 1

    cap.release()
    cv2.destroyAllWindows()


def run_compare(label):
    """Record one attempt and run it through the backend's real classifier
    (compared against ALL signs), without needing the server running."""
    sys.path.insert(0, BACKEND_DIR)
    from app import gesture_classifier

    ensure_model()
    if not os.path.isdir(os.path.join(REFERENCES_DIR, label)):
        print(f"No references found for '{label}'. Record some first:")
        print(f"    py -3.12 dtw_recognizer.py record {label}")
        return

    cap = cv2.VideoCapture(0)
    start_time = time.time()

    with vision.HolisticLandmarker.create_from_options(make_options()) as landmarker:
        sequence = capture_sequence(cap, landmarker, start_time,
                                     f"Testing against '{label}' - SPACE to start/stop, q to quit")

    cap.release()
    cv2.destroyAllWindows()

    if not sequence or len(sequence) < 3:
        print("No usable attempt captured.")
        return

    # Same shape of data the backend gets from Unity.
    frames = [{"left_hand": np.asarray(f[:63]).reshape(21, 3).tolist() if np.any(f[:63]) else [],
               "right_hand": np.asarray(f[63:]).reshape(21, 3).tolist() if np.any(f[63:]) else []}
              for f in sequence]
    gesture_classifier.DEBUG_PRINT_DISTANCES = True
    predicted, confidence = gesture_classifier.classify_sequence(frames, label)

    print(f"\nConfidence: {confidence:.0%}")
    if predicted == label:
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
