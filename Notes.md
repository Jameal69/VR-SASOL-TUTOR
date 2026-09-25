PROJECT HANDOFF DOCUMENT
VR SASL Tutor — Complete Context for New Claude Session
Prepared: 2026-09-24
Purpose: Full context handoff so a fresh Claude Code session can pick up exactly where the project stands, without re-litigating settled decisions or losing context.

1. WHAT THIS PROJECT IS
Title: Immersive Virtual Reality Tutor for Learning South African Sign Language (SASL) Through Real-Time Interaction

Goal: A VR application that teaches SASL using:

Virtual Reality (Unity)

AI-based gesture recognition (MediaPipe + DTW classifier)

Real-time corrective feedback

Adaptive learning (track progress, adjust difficulty)

Assessment: University group project (PRJ3x1), 10 team members. Milestone 2 = a video of up to 15 minutes showing current functionality and next steps. It is NOT a finished product. Due mid-October 2026.

The single most important strategic decision: Get HELLO working end-to-end (camera → classifier → backend → Unity → visible feedback) before expanding anything. This is a "vertical slice." Every other feature is secondary to this.

2. TEAM STRUCTURE & REALITY
Person	Original Role	Actual Contribution
Jameal (user)	Coordinator/Backend support	Did backend, gesture recordings, integration, git cleanup, content seeding, Unity work, wrote most code, coordinated everything
Christian	Gesture Recognition (Owner)	Built the MediaPipe + DTW classifier pipeline — genuinely solid work
Markus	Gesture Recognition (Secondary)	Paired with Christian, largely silent otherwise
Nuvaran	Backend & Database (Owner)	Set up initial FastAPI skeleton, then largely quiet
Jason	Backend (Secondary) + Integration Lead	Wrote API contract draft, then largely quiet
Juan	SASL Content	Delivered — wrote Lesson 1 sign descriptions, good quality
Keegan	VR Environment & Avatar	Almost nothing delivered. Biggest gap in the project
Migal	VR Environment (Secondary) + UX	Silent, just re-engaged at the very end
Dylan	Desktop Fallback & UI	Silent, just re-engaged at the very end
Inathi	Testing & QA	Minimal, asked for .env access at one point
Harsh reality: Jameal has done the work of ~5 people. The team has been unresponsive for months. A supervisor (Simba) was emailed about this. Some teammates re-engaged in the last week (Dylan, Migal). Jameal is burnt out.

Do NOT pretend the team is functional. The new Claude session should help Jameal finish the project, not help manage the team.

3. TECH STACK
Layer	Tech	Location
Backend	FastAPI + SQLAlchemy + PostgreSQL (Supabase)	backend-api/
ML/Gesture	Python 3.12 + MediaPipe Holistic + DTW classifier	ml-service/
Frontend	Unity 6.5 (Universal 3D/URP)	frontend-unity/SASLTutorVR/
Database	Supabase free tier (Postgres)	Cloud
Auth	JWT (python-jose + passlib/bcrypt)	backend-api/app/auth.py
Critical version notes:

Python 3.12 required for MediaPipe (not 3.14 — MediaPipe doesn't support newest Python)

Unity 6.5 — team must match exactly

bcrypt==4.0.1 pinned (newer bcrypt breaks passlib 1.7.4)

psycopg2-binary unpinned (pinned version has no wheel for Python 3.14)

pydantic>=2.10 (older pinned version needs Rust compiler)

email-validator>=2.2 needed for pydantic[email]

4. REPOSITORY STRUCTURE
text
vr-sasl-tutor/                           ← repo root
├── .github/workflows/keep_alive.yml     ← pings Supabase so it doesn't pause
├── backend-api/                         ← FastAPI backend
│   ├── app/
│   │   ├── main.py                      ← all API endpoints
│   │   ├── models.py                    ← SQLAlchemy models
│   │   ├── schemas.py                   ← Pydantic schemas
│   │   ├── database.py                  ← DB connection (uses load_dotenv!)
│   │   ├── auth.py                      ← JWT auth
│   │   └── gesture_classifier.py        ← DTW classifier wrapper
│   ├── requirements.txt
│   ├── .env                             ← DATABASE_URL, JWT_SECRET_KEY (NOT committed)
│   ├── seed.py                          ← seeds Lesson 1 content
│   ├── backup.py                        ← DB backup script
│   └── check_references.py              ← offline reference-comparison test
├── ml-service/                          ← MediaPipe + DTW
│   ├── landmark_streamer.py             ← streams landmarks to Unity via TCP socket
│   ├── dtw_recognizer.py                ← original DTW classifier + recorder
│   ├── test_classify_live.py            ← live webcam → backend test
│   ├── holistic_landmarker.task         ← MediaPipe model file
│   ├── record_reference.py              ← record tool
│   └── references/                      ← reference recordings (GITIGNORED currently)
│       ├── hello/rep_0.npy ... rep_5.npy
│       ├── goodbye/...
│       ├── please/...
│       └── thank_you/...
├── frontend-unity/
│   └── SASLTutorVR/                     ← THE actual Unity project
│       └── Assets/
│           ├── Scripts/Gesture/
│           │   ├── LandmarkReceiver.cs          ← TCP socket receiver (WORKS)
│           │   └── GestureClassifierClient.cs  ← sends to backend, gets result (WORKS)
│           ├── Scripts/Managers/, UI/, Lessons/, Data/, Utilities/
│           ├── Prefabs/, Scenes/, Materials/, Models/, Animations/
├── docs/
│   ├── curriculum-lesson1.md
│   ├── api-contract.md
│   ├── how-it-works-and-next-steps.md
│   ├── DECISIONS.md
│   └── team-roles.md
└── meeting-notes/
    ├── tracker.md
    └── testing-log.md
Branch convention: develop = active work, main = stable. Feature branches off develop, merged back within days.

5. WHAT ACTUALLY WORKS (PROVEN)
Backend — Fully live
Connected to real Supabase Postgres

Auth (register/login with JWT)

Curriculum endpoints return real data

POST /api/gestures/classify wired to real classifier

POST /api/feedback — recently changed to derive scores from confidence (was hardcoded 3,3,3,3)

Database backed up, keep-alive GitHub Action running

Content — Seeded
Lesson 1: Hello, Goodbye, Please, Thank You with descriptions from Juan

IDs:

Hello: 701d037f-2703-47a5-a09b-5eb3effa08f5

Goodbye: fab6a4b5-d6ef-4166-afad-3aaa8503daed

Please: a99dfe87-1cbd-4700-a4da-ce2947b58ac3

Thank You: 8160b4f7-752e-4e60-b0f6-9565b890c080

Gesture Recognition — Pipeline works, but has issues
MediaPipe Holistic tracks hand landmarks

DTW classifier compares live attempt vs saved references

Proven: test_classify_live.py returned {'predicted_sign': 'hello', 'confidence_score': 1.0} — real end-to-end classification

Proven: All four signs matched at 100% via dtw_recognizer.py compare after recording

Proven: Random arm-waving correctly rejected (20% confidence, NO MATCH)

Proven: Two hands out of frame → "unknown"

Unity Integration — Works
LandmarkReceiver.cs connects to landmark_streamer.py TCP socket (port 5052)

Real landmark data streams into Unity Console

GestureClassifierClient.cs buffers frames, POSTs to /api/gestures/classify, displays result in on-screen panel with MATCH/NO MATCH

The full chain Unity → backend → classifier → on-screen result has been demonstrated

6. THE ACTIVE PROBLEM (MOST IMPORTANT)
Symptom
Classifier gives false positives. Random arm-waving scored ~80%. Goodbye scores 80-100% when target is Hello. Live attempts for "Please" score terribly against Please's own references.

Root cause #1 — FOUND & FIXABLE: Left/right hand slot mismatch
The reference recordings are almost entirely in the "right hand" slot. Live attempts were landing mostly in the "left hand" slot. The classifier was comparing live hand data against empty space.

Evidence:

References: Hello/Goodbye/Thank You all ~0% left, 41-89% right. Please has both (~40-49% left, 50-76% right).

Live attempts: left=48%, right=0% — opposite of what references have.

Immediate test:

Start streamer + Unity, watch the preview window

Raise ONLY your right hand → does the label say RIGHT or LEFT?

Sign with whichever hand the preview labels "RIGHT hand" (that's where references live)

Retry a Hello attempt → LIVE hands detected line should show right ~60%+, left near 0%

The fix: Sign with the hand that fills the "RIGHT" slot in the preview, matching the references. No re-recording needed. Possibly a small code change to ignore hand slot entirely if the mirroring is confusing.

Root cause #2 — STRUCTURAL, needs re-record: normalization discards trajectory
The _landmarks_to_vector function re-centers every frame on that frame's own wrist position. This gives translation invariance but throws away the actual movement path. Since Hello, Goodbye, Please, Thank You all use nearly the same flat hand shape, the only thing distinguishing them is hand position + movement direction — exactly what gets discarded.

Evidence from recent test data:

"goodbye" has lowest avg distance in almost every test regardless of what was performed

"please" has highest avg distance even when performing Please

Goodbye is a "magnet" — it's the shortest sequence and the distance calc divides by n+m

The fix (proposed but not yet fully applied):

Change _landmarks_to_vector to re-center the whole sequence on the first frame's wrist only (preserves trajectory)

OR: filter empty frames + normalize by path length (partially done, made things worse)

This requires re-recording all references because the .npy files store already-normalized vectors

Current state (from check_references.py offline test): 19 of 21 recordings land closest to their own sign. The core classifier logic isn't broken — it's the live-vs-reference mismatch and the trajectory loss that cause live problems.

7. RECENT SESSION SUMMARY (2026-09-23)
A prior Claude session with Jameal established:

Empty-frame filter and path-normalization were tried; made things worse without re-recording

Cross-sign comparison added (each attempt checked against ALL signs, not just target)

Real distances now printed on every classification

The "Goodbye magnet" behavior confirmed — Goodbye has lowest distance to everything

The left/right hand discovery: references are mostly "right," live attempts mostly "left"

hello rep_3.npy is an outlier — should be renamed to .bak to exclude

8. KNOWN BUGS / GOTCHAS
.env filename trap on Windows — Notepad saves as .env.txt. Use notepad .env from cmd to force correct name.

load_dotenv() was missing from database.py originally — now fixed.

Supabase pauses on inactivity — fixed via GitHub Action keep-alive.

Supabase connection string changes after pause/resume — if you get "tenant/user not found", get fresh string from dashboard.

requirements.txt pins caused 5 separate install failures — now loosened.

Retyping Python files by hand introduced indentation bugs — gesture_classifier.py function was indented 2 spaces, making it nested and uncallable. Replaced with known-good version.

ml-service/references/ is gitignored — Jameal needs to un-ignore it (or at least commit the 4 folders) so teammates get the recordings. Currently the work exists only on his machine.

Unity socket only accepts one connection — restart landmark_streamer.py before every Play.

record_reference.py exists but the original dtw_recognizer.py record is what was used for the current recordings.

Supabase password was leaked in a shared chat link — Jameal was told to reset it; confirmation status unknown.

9. BIGGEST REMAINING GAP: VR SCENE + AVATAR
Keegan's task, essentially not done. He sent a furniture asset (living room glb, no character, no rig) by mistake. The actual requirement is:

Any free rigged humanoid character (Mixamo) standing in a scene

One triggerable animation (idle or wave)

Eventually: real sign animations for Hello/Goodbye/Please/Thank You using Juan's descriptions

Jameal started building this himself (Mixamo + LandmarkReceiver.cs in an empty scene). The socket-to-Unity connection was proven working.

Next Unity step: Get a character into the scene, prove one animation plays, then connect the classifier result to drive a visible reaction (e.g., on-screen text or avatar animation).

10. FILE-SPECIFIC NOTES FOR CLAUDE CODE
When you open this project in Claude Code, read in this order:

docs/DECISIONS.md — log of all major decisions

docs/how-it-works-and-next-steps.md — system architecture explanation

meeting-notes/tracker.md — current task status

backend-api/app/gesture_classifier.py — the classifier (most likely file to be edited)

backend-api/app/main.py — endpoints

ml-service/dtw_recognizer.py — original recorder

ml-service/landmark_streamer.py — streamer

frontend-unity/SASLTutorVR/Assets/Scripts/Gesture/GestureClassifierClient.cs — Unity→backend client

frontend-unity/SASLTutorVR/Assets/Scripts/Gesture/LandmarkReceiver.cs — socket receiver

Recent files from the last session (may or may not be in the repo yet):

check_references.py (updated version with left/right hand columns + 4-config comparison)

gesture_classifier.py (cross-sign comparison version)

GestureClassifierClient.cs (with on-screen panel + MATCH/NO MATCH)

11. IMMEDIATE NEXT STEPS (PRIORITIZED)
Priority 1 — Fix the hand-slot mismatch (10 minutes)
Run the preview test described in §6. If live attempts use "left" and references use "right," that alone explains most failures. Fix = sign with correct hand, or patch classifier to ignore hand slot.

Priority 2 — Fix the trajectory-loss problem (2–3 hours)
Replace _landmarks_to_vector with sequence-level re-centering (first-frame wrist only)

Delete old references (built on old normalization)

Re-record all 4 signs (Jameal did this once, ~15 min)

Re-run test_classify_live.py for each sign

Verify false positives now return low confidence

Priority 3 — Unity demo loop (Keegan's gap, 2–4 hours)
Mixamo character in scene

Trigger one animation on classify result

Show MATCH/NO MATCH + detected sign on-screen (already done in GestureClassifierClient.cs)

Record a clean take for the milestone video

Priority 4 — Cleanup / polish
Un-gitignore ml-service/references/ and commit the recordings

Rotate Supabase password if not already done

Update docs/DECISIONS.md with the on-device vs network classification decision (currently network)

Update meeting-notes/tracker.md

12. WHAT NOT TO DO
Don't re-litigate decisions already made (JSONB over MongoDB, simple classifier over Transformer, network classification over on-device, webcam over headset for now)

Don't chase the whole team to contribute. Focus on what Jameal can finish solo.

Don't aim for the full 5-lesson polished system. Aim for ONE sign working end-to-end with a visible result. That's the milestone.

Don't trust summaries of files you can read directly. With Claude Code, read the actual current file.

Don't rename functions or variables in shared files (like gesture_classifier.py) without updating every caller — this already caused one bug.

13. THE HONEST STATUS
What's genuinely banked:

Backend live + tested + connected to real DB

Classifier built + proven working for all 4 signs when hand slot matches

Content written + seeded

Unity socket connection working

Full camera→backend→classifier chain proven end-to-end

All individual connection points tested

What's genuinely at risk:

The classifier gives false positives in live use — this is the single thing that could make the demo look broken

VR scene + avatar still essentially nonexistent

Team has been unreliable; Jameal is doing most work solo

What "success by October" looks like:
A 10-15 minute video showing:

A short demo of Hello working end-to-end (camera → classifier → Unity → visible MATCH result)

Honest "here's what's next" for other signs, VR polish, adaptive learning

Architecture explanation showing the vertical slice is real

This is achievable. The full polished 5-lesson adaptive VR system is NOT achievable and was never going to be with this team.

14. FOR THE NEW CLAUDE SESSION
You are picking up mid-debugging. The user (Jameal) is:

Technically capable but stretched thin

Burnt out from carrying the team

Working under real time pressure (weeks, not months)

Skeptical that any more AI context relay will help — which is why Claude Code is the right move

Your job: Help them finish the vertical slice. Be direct. Read actual files, don't guess. Fix the hand-slot mismatch first, then the normalization problem, then Unity. Do not moralize about the team situation. Do not suggest they "communicate better with the team." Focus on shipping.

The single most useful thing you can do first: Open gesture_classifier.py and dtw_recognizer.py side by side, confirm how references are normalized vs how live data is normalized, and check whether the hand-slot issue is in the vector construction or in the comparison.

