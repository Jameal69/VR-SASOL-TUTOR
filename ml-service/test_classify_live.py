"""
Captures ONE live sign attempt via webcam and sends it straight to the real
backend's POST /api/gestures/classify endpoint, the same way Unity eventually
will, just standing in for Unity for this test.

This is the real end-to-end proof: camera -> MediaPipe -> real HTTP request
-> real backend -> real classifier -> real response

Setup (same as the other ml-service scripts):
    py -3.12 -m pip install mediapipe opencv-python requests



Get curriculum_item_id from GET /api/curriculum/lessons/{lesson_id} in /docs
first, e.g. the Hello sign's ID.

Make sure the backend server is already running (uvicorn app.main:app --reload)
before running this.
"""
import os
import sys
import time

import cv2
import mediapipe as mp
import requests
from mediapipe.tasks.python import vision
from mediapipe.tasks.python.core.base_options import BaseOptions

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
MODEL_PATH = os.path.join(SCRIPT_DIR, "holistic_landmarker.task")
BACKEND_URL = "http://localhost:8000/api/gestures/classify"


def landmarks_to_list(landmarks):
    if not landmarks:
        return []
    return [[round(lm.x, 4), round(lm.y, 4), round(lm.z, 4)] for lm in landmarks]


def capture_one_attempt():
    """Same SPACE-to-start/stop capture UX as dtw_recognizer.py, but builds
    raw landmark_sequence JSON instead of pre-normalized feature vectors."""
    options = vision.HolisticLandmarkerOptions(
        base_options=BaseOptions(model_asset_path=MODEL_PATH),
        running_mode=vision.RunningMode.VIDEO,
    )
    cap = cv2.VideoCapture(0)
    start_time = time.time()
    recording = False
    frames = []

    print("Press SPACE to start recording your attempt, SPACE again to stop, 'q' to quit.")
    with vision.HolisticLandmarker.create_from_options(options) as landmarker:
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
                frames.append({
                    "t": timestamp_ms / 1000.0,
                    "left_hand": landmarks_to_list(result.left_hand_landmarks),
                    "right_hand": landmarks_to_list(result.right_hand_landmarks),
                })
                cv2.putText(frame, f"RECORDING ({len(frames)} frames)", (10, 30),
                            cv2.FONT_HERSHEY_SIMPLEX, 0.8, (0, 0, 255), 2)
            else:
                cv2.putText(frame, "Press SPACE to start", (10, 30),
                            cv2.FONT_HERSHEY_SIMPLEX, 0.8, (0, 255, 0), 2)

            cv2.imshow("Live classify test (SPACE to start/stop, q to quit)", frame)
            key = cv2.waitKey(1) & 0xFF

            if key == ord(" "):
                if not recording:
                    recording = True
                    frames = []
                else:
                    cap.release()
                    cv2.destroyAllWindows()
                    return frames
            elif key == ord("q"):
                cap.release()
                cv2.destroyAllWindows()
                return None


def main():
    if len(sys.argv) != 2:
        print("Usage: py -3.12 test_classify_live.py <curriculum_item_id>")
        sys.exit(1)

    curriculum_item_id = sys.argv[1]

    frames = capture_one_attempt()
    if not frames or len(frames) < 3:
        print("No usable attempt captured.")
        return

    print(f"\nCaptured {len(frames)} frames, sending to backend...")

    payload = {
        "session_id": "local-test-script",
        "curriculum_item_id": curriculum_item_id,
        "landmark_sequence": frames,
    }

    response = requests.post(BACKEND_URL, json=payload)

    print(f"\nStatus code: {response.status_code}")
    print("Response:")
    print(response.json())


if __name__ == "__main__":
    main()
