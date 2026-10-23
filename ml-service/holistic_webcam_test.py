"""
Feasibility spike (done, superseded by landmark_streamer.py and
dtw_recognizer.py) - kept for reference. See README.md for setup.

Uses MediaPipe's Tasks API (HolisticLandmarker), not the old
`mp.solutions.holistic`, which has been removed from recent releases.
"""

import os
import time
import urllib.request

import cv2
import mediapipe as mp
from mediapipe.tasks.python import vision
from mediapipe.tasks.python.core.base_options import BaseOptions

MODEL_URL = (
    "https://storage.googleapis.com/mediapipe-models/holistic_landmarker/"
    "holistic_landmarker/float16/1/holistic_landmarker.task"
)
MODEL_PATH = os.path.join(os.path.dirname(os.path.abspath(__file__)), "holistic_landmarker.task")


def ensure_model():
    """Download the holistic landmarker model bundle if it isn't present yet."""
    if not os.path.exists(MODEL_PATH):
        print(f"Downloading model to {MODEL_PATH} ...")
        urllib.request.urlretrieve(MODEL_URL, MODEL_PATH)
        print("Download complete.")


def print_hand_landmarks(hand_label, landmarks):
    """Print the (x, y, z) normalized coordinates for one hand's 21 landmarks."""
    if not landmarks:
        return
    print(f"  {hand_label}:")
    for i, lm in enumerate(landmarks):
        print(f"    [{i:02d}] x={lm.x:.3f} y={lm.y:.3f} z={lm.z:.3f}")


def main():
    ensure_model()

    options = vision.HolisticLandmarkerOptions(
        base_options=BaseOptions(model_asset_path=MODEL_PATH),
        running_mode=vision.RunningMode.VIDEO,
    )

    cap = cv2.VideoCapture(0)
    if not cap.isOpened():
        raise RuntimeError("Could not open webcam (index 0). Check camera permissions/connection.")

    start_time = time.time()

    with vision.HolisticLandmarker.create_from_options(options) as landmarker:
        print("Webcam open. Press 'q' in the preview window to quit.\n")
        while True:
            ok, frame = cap.read()
            if not ok:
                print("Failed to read frame from webcam.")
                break

            # MediaPipe expects RGB.
            rgb_frame = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
            mp_image = mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb_frame)
            timestamp_ms = int((time.time() - start_time) * 1000)

            result = landmarker.detect_for_video(mp_image, timestamp_ms)

            if result.left_hand_landmarks or result.right_hand_landmarks:
                print(f"--- t={timestamp_ms}ms ---")
                print_hand_landmarks("Left hand", result.left_hand_landmarks)
                print_hand_landmarks("Right hand", result.right_hand_landmarks)

            # Small preview window just to confirm the feed is live.
            cv2.imshow("Holistic webcam test (press q to quit)", frame)
            if cv2.waitKey(1) & 0xFF == ord("q"):
                break

    cap.release()
    cv2.destroyAllWindows()


if __name__ == "__main__":
    main()
