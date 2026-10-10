"""
Streams MediaPipe Holistic hand landmarks to Unity over a local TCP
socket. See README.md for setup and usage.
"""

import json
import os
import socket
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

HOST = "127.0.0.1"
PORT = 5052


def ensure_model():
    """Download the holistic landmarker model bundle if it isn't present yet."""
    if not os.path.exists(MODEL_PATH):
        print(f"Downloading model to {MODEL_PATH} ...")
        urllib.request.urlretrieve(MODEL_URL, MODEL_PATH)
        print("Download complete.")


def landmarks_to_list(landmarks):
    """Convert a MediaPipe landmark list into a plain list of [x, y, z] floats
    so it can be serialized to JSON. Returns an empty list if no hand is
    detected this frame - Unity should treat that as 'hand not visible'."""
    if not landmarks:
        return []
    return [[round(lm.x, 4), round(lm.y, 4), round(lm.z, 4)] for lm in landmarks]


def upper_body_visible(pose_landmarks):
    """True when the nose and both shoulders are detected, confident and inside
    the frame. Holistic finds the hands via the body pose, so if these slip out
    of view the hand landmarks get distorted even when the hand is visible."""
    if not pose_landmarks:
        return False
    for index in (0, 11, 12):  # nose, left shoulder, right shoulder
        lm = pose_landmarks[index]
        if lm.visibility is not None and lm.visibility < 0.5:
            return False
        if not (0.0 <= lm.x <= 1.0 and 0.0 <= lm.y <= 1.0):
            return False
    return True


def wait_for_unity_connection():
    """Open a TCP server socket and block until Unity connects to it.
    Using a single blocking accept() is deliberately simple for this proof
    -of-concept - fine for one Python process talking to one Unity instance."""
    server_socket = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    server_socket.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    server_socket.bind((HOST, PORT))
    server_socket.listen(1)
    print(f"Waiting for Unity to connect on {HOST}:{PORT} ... (Ctrl+C to quit)")

    # A plain blocking accept() never sees Ctrl+C on Windows, so wait in
    # 1-second slices to give KeyboardInterrupt a chance to fire.
    server_socket.settimeout(1.0)
    while True:
        try:
            client_socket, addr = server_socket.accept()
            break
        except socket.timeout:
            continue
    client_socket.settimeout(None)  # back to normal blocking sends
    print(f"Unity connected from {addr}.")
    return server_socket, client_socket


def main():
    ensure_model()

    options = vision.HolisticLandmarkerOptions(
        base_options=BaseOptions(model_asset_path=MODEL_PATH),
        running_mode=vision.RunningMode.VIDEO,
        # Lower than the defaults (0.5) so tracking is less eager to give up.
        # Message format sent to Unity is unchanged.
        min_pose_detection_confidence=0.3,
        min_pose_landmarks_confidence=0.3,
        min_hand_landmarks_confidence=0.3,
    )

    server_socket, client_socket = wait_for_unity_connection()

    cap = cv2.VideoCapture(0)
    if not cap.isOpened():
        raise RuntimeError("Could not open webcam (index 0). Check camera permissions/connection.")

    start_time = time.time()

    try:
        with vision.HolisticLandmarker.create_from_options(options) as landmarker:
            print("Streaming landmarks to Unity. Press Ctrl+C to stop.\n")
            while True:
                ok, frame = cap.read()
                if not ok:
                    print("Failed to read frame from webcam.")
                    break

                rgb_frame = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
                mp_image = mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb_frame)
                timestamp_ms = int((time.time() - start_time) * 1000)

                result = landmarker.detect_for_video(mp_image, timestamp_ms)

                # Build one JSON message per frame. Sending both hands every
                # frame, even if empty, keeps the message shape consistent -
                # easier for Unity to parse than a shape that changes.
                body_ok = upper_body_visible(result.pose_landmarks)
                message = {
                    "t": timestamp_ms,
                    "left_hand": landmarks_to_list(result.left_hand_landmarks),
                    "right_hand": landmarks_to_list(result.right_hand_landmarks),
                    # Backend asks for a retry when this is False too often.
                    "body": body_ok,
                }

                # Newline-delimited JSON: one full JSON object per line, so
                # Unity can read it with a simple ReadLine() on its end.
                payload = (json.dumps(message) + "\n").encode("utf-8")

                try:
                    client_socket.sendall(payload)
                except ConnectionError:
                    # Covers every way Unity can drop the socket (stopping Play
                    # gives WinError 10053 / ConnectionAbortedError on Windows).
                    print("Unity disconnected (Play stopped). Restart this script before pressing Play again.")
                    break

                # Local preview window, same as the spike script - handy for
                # confirming the camera feed while you watch Unity's console
                # in a separate window.
                # Draw what the tracker actually sees, so you can tell when it
                # loses your hands. Circles = hand points. Text is added after
                # mirroring so it stays readable.
                h, w = frame.shape[:2]
                for hand, colour in ((result.left_hand_landmarks, (0, 255, 0)),
                                     (result.right_hand_landmarks, (255, 128, 0))):
                    for lm in hand or []:
                        cv2.circle(frame, (int(lm.x * w), int(lm.y * h)), 3, colour, -1)
                preview = cv2.flip(frame, 1)
                status = [
                    ("HEAD + SHOULDERS OK" if body_ok else "Keep head + both shoulders in view", body_ok),
                    ("LEFT hand" if result.left_hand_landmarks else "left hand lost", bool(result.left_hand_landmarks)),
                    ("RIGHT hand" if result.right_hand_landmarks else "right hand lost", bool(result.right_hand_landmarks)),
                ]
                for i, (label, ok_flag) in enumerate(status):
                    cv2.putText(preview, label, (10, 28 + 28 * i), cv2.FONT_HERSHEY_SIMPLEX, 0.8,
                                (0, 200, 0) if ok_flag else (0, 0, 255), 2)
                cv2.imshow("Landmark streamer (press q to quit)", preview)
                if cv2.waitKey(1) & 0xFF == ord("q"):
                    break
    finally:
        cap.release()
        cv2.destroyAllWindows()
        client_socket.close()
        server_socket.close()


if __name__ == "__main__":
    try:
        main()
    except KeyboardInterrupt:
        print("\nStopped.")
