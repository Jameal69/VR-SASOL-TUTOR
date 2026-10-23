# VR test session (Jameal + Keegan)

Goal: decide **how signs are captured in VR for the expo**: the webcam (works today) or the
Quest 3's own hand tracking (experiment). Also make the VR demo expo-proof.

Plan about **2 hours**. One person wears the headset, the other sits at the keyboard and fills in
this sheet. Swap halfway so both of you have signed in VR.

---

## 0. Before you start (15 min)

- [ ] Quest 3 charged; Link cable (or Air Link) works; controllers charged
- [ ] Webcam on a stand or tripod (not on the laptop lid; it has to see you standing)
- [ ] In GitHub Desktop: Fetch, switch to **`feature/headset-hands`** (VR scene + probe + headset experiment)
- [ ] `backend-api\.env` exists on this PC (ask Jameal privately if not)
- [ ] Backend starts and `http://localhost:8000/docs` loads:
  ```
  cd backend-api
  venv\Scripts\python.exe -m uvicorn app.main:app --reload
  ```
- [ ] Unity 6000.5.4f1 opens the project; Build Profiles shows **Windows** (not Android)
- [ ] Open `Assets/Scenes/VRClassroom`, then run both menus once:
  - **SASL → Add Hand Tracking Probe**
  - **SASL → Experiments → Add Headset Hand Recognition** (also switches hand tracking on in OpenXR)
- [ ] On the Quest: **Settings → Movement tracking → Hand and body tracking: on**
- [ ] Meta Horizon Link app (PC) running; headset connected through Link

Write down anything that failed or needed a fix:

| Problem | Fix |
|---|---|
| | |
| | |

---

## 1. Baseline: the VR scene itself (15 min)

Start the streamer **with `--vr`** (only checks the shoulders, since the headset covers the face):
```
cd ml-service
py -3.12 landmark_streamer.py --vr
```
Then press Play in `VRClassroom`.

| Check | OK? | Notes |
|---|---|---|
| Headset shows the classroom in **both eyes** (board too) | | |
| Whiteboard text readable from where you stand | | |
| **Y** recentres you in front of the avatar | | |
| **B**: avatar demonstrates the sign | | |
| **Stick**: changes lesson (left/right) and sign (up/down) | | |
| **A**: starts/stops recording; result appears on the board | | |
| Frame rate feels smooth (no stutter when turning your head) | | |

---

## 2. Webcam recognition while wearing the headset (25 min)

Find the camera spot first: **1.5–2 m in front, at chest height, whole upper body and both hands in frame.**
Take a photo of the setup that works.

Camera position used: ______________________

3 attempts per sign. Write **M** (match), **N** (no match), or **R** (asked to retry).

| Sign | 1 | 2 | 3 | Notes |
|---|---|---|---|---|
| Hello | | | | |
| Goodbye | | | | |
| Please | | | | |
| Thank You | | | | |
| **Wrong sign on purpose** (asked for Goodbye, sign Please) | | | | should be N |

---

## 3. Does the headset track hands well enough? (20 min)

Put the **controllers down**. Your hands should appear as **dots** in the headset.
The board shows "Right hand tracked X% (last 3 s)".

- If the board says **"Hand tracking: not available"**:
  1. Check hand tracking is on in the Quest settings and the controllers are down
  2. Meta Horizon Link app → **Settings → Beta → Developer runtime features: on**, restart Link
  3. Still nothing → write it down and skip to section 5 (webcam it is)

Press **H** on the keyboard to start a measurement, sign once, press **H** to stop.
3 measurements per sign. Copy the numbers from the board.

| Sign | Try | Right hand tracked % | Dropouts | Notes (where did it lose the hand?) |
|---|---|---|---|---|
| Hello (at the forehead) | 1 | | | |
| | 2 | | | |
| | 3 | | | |
| Thank You (at the chin) | 1 | | | |
| | 2 | | | |
| | 3 | | | |
| Goodbye | 1 | | | |
| | 2 | | | |
| | 3 | | | |
| Please (at the chest) | 1 | | | |
| | 2 | | | |
| | 3 | | | |

The full numbers are saved to `frontend-unity/SASLTutorVR/Logs/hand_tracking_probe.csv`.

---

## 4. Recognition from headset hand tracking (20 min)

Only if section 3 worked. Pick the sign on the board, press **G**, sign, press **G** again.
The result shows on the board ("Headset hands: ..."). 3 attempts per sign:

| Sign | 1 | 2 | 3 | Notes |
|---|---|---|---|---|
| Hello | | | | |
| Goodbye | | | | |
| Please | | | | |
| Thank You | | | | |

Weak results here are expected: the reference recordings come from a webcam. Every attempt is
saved to `frontend-unity/SASLTutorVR/Logs/headset_attempts/`, and those files can become headset references later.

---

## 5. Decision for the expo

| Question | Answer |
|---|---|
| Webcam in VR: how many of the 12 attempts matched? | ___ / 12 |
| Headset tracking: average right hand tracked % | ___ % |
| Headset recognition: how many of the 12 attempts matched? | ___ / 12 |

**Rule of thumb:**
- Headset tracking **≥ 90%** and headset recognition **≥ 8/12** → try headset references this week (record the 4 signs with G, 6–8 times each)
- Otherwise → **webcam for the expo**, and the headset experiment becomes a "next step"

Decision: ______________________

---

## 6. Expo dry run (15 min)

Shut everything down, then start it all again **from cold** and time it:
backend → streamer → Unity → Quest Link → Play → first match.

| Step | Time |
|---|---|
| Backend running | |
| Streamer running | |
| Unity in Play mode | |
| Headset connected, scene visible | |
| First MATCH | |
| **Total** | |

If the total is over 10 minutes, write down what was slow: ______________________

---

## Bring back to Jameal

- [ ] This sheet, filled in
- [ ] `Logs/hand_tracking_probe.csv`
- [ ] The `Logs/headset_attempts/` folder (zip it)
- [ ] A photo of the camera position that worked
- [ ] Any errors from the Unity Console or the backend terminal (screenshots)

**Don't commit** anything Unity changes by itself on this PC (ProjectSettings, XR settings,
`Plugins/Android`, scenes), unless Jameal asks for it.
