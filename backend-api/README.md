# Backend — SASL VR Tutor API

FastAPI backend. Single PostgreSQL database (landmarks stored as JSONB — see `docs/DECISIONS.md`
for why we dropped the Mongo+Postgres hybrid from the M1 report).

## Setup

1. Install PostgreSQL locally (or use a free-tier hosted instance — Supabase/Neon both work and
   save you from managing a local DB server, worth considering given everything else going on).
2. Create a database: `createdb sasl_vr_tutor`
3. From `backend/`:
   ```
   python -m venv venv
   source venv/bin/activate        # Windows: venv\Scripts\activate
   pip install -r requirements.txt
   ```
4. Set environment variables (create a `.env` file, don't commit it):
   ```
   DATABASE_URL=postgresql://postgres:yourpassword@localhost:5432/sasl_vr_tutor
   JWT_SECRET_KEY=some-random-string-for-local-dev
   ```
5. Run it:
   ```
   uvicorn app.main:app --reload
   ```
6. Confirm it works: open `http://localhost:8000/api/health` — should return `{"status": "ok"}`.
   Also check `http://localhost:8000/docs` — FastAPI auto-generates interactive API docs there,
   useful for testing endpoints without needing Unity or Postman.

## What's actually implemented vs. placeholder

Working: auth (register/login/JWT), user profile, curriculum listing, sessions, gesture record
storage, progress aggregation.

**Placeholder, not real yet:**
- `POST /gestures/classify` — returns a dummy prediction. Wire this up once Christian's classifier
  exists, or confirm with him whether this endpoint is even needed (see the open question at the
  bottom of `docs/api-contract.md` — on-device inference in Unity may skip this entirely).
- `POST /feedback` — dimension scores are hardcoded placeholders, not derived from anything real yet.
- No lessons/curriculum items are seeded in the DB yet — `GET /curriculum/lessons` will return an
  empty list until Juan's Lesson 1 content (`docs/curriculum-lesson1.md`) gets turned into actual
  database rows. Worth writing a small `seed.py` script once that content exists.

## Next steps for Nuvaran

1. Get the health check running locally — that's the whole first goal.
2. Once Juan's `docs/curriculum-lesson1.md` exists, write a `seed.py` to load Lesson 1 + its signs
   into the `lessons` / `curriculum_items` tables.
3. Swap `Base.metadata.create_all()` in `main.py` for real Alembic migrations once the schema
   feels stable — fine to skip this while things are still moving fast.
4. Talk to Jason about whether `/gestures/classify` survives as a real endpoint or gets removed
   once the classify-on-device question is settled.

## Curriculum scope & testing status (Lessons 1–3)

**Content included**
- Lesson 1 — Greetings (Hello, Goodbye, Please, Thank You): content + recognition tested (reference recordings exist).
- Lesson 2 — Alphabet A–Z: content only. Recognition not yet tested — no reference recordings captured.
- Lesson 3 — Numbers 1–9: content only. Recognition not yet tested — no reference recordings captured.

**Not tested / out of scope**
- Recognition of the alphabet and numbers: the lessons appear in the curriculum, but the classifier cannot yet identify these signs — reference recordings are still to be captured.
- Advanced handshape variants (e.g. Small C, Closed Small C, Flat B, and other non-core handshapes) are not included or tested.
- Visually similar letters (U/V, M/N, K/P, I/J, D/F/O) are expected to be hard for the current recognizer to distinguish even once references exist; treat as untested until validated.

**Verification**
- `VERIFIED = False` in `app/curriculum_data.py`: the alphabet/number articulations are drafted and cross-checked against the RealSASL handshape chart, but not yet confirmed by a Deaf advisor (Milestone report §8.1). A few entries (letters M, N, T; numbers 3, 4, 6, 8, 9) still need confirming against the chart image.
## Recognition work-in-progress (experimental) & sign scoring

This branch adds exploratory recognition material. It is **experimental and does
not change the live recogniser** — `gesture_classifier.py`, `/api/gestures/classify`,
and `references_raw/` are untouched, so the working demo is unaffected.

**What's here:**
- `ml-service/experimental/handshape_features.py` — a standalone, self-tested
  feature module implementing the approaches for the hard signs: finger-geometry
  features (curl / spread — separates U/V, M/N), a head-anchored pointing
  direction (separates the orientation pairs K/P, G/Q, U/H), and a
  confidence-gated "palm-check" trigger (asks for a clean re-read only when
  unsure). Run it with `python handshape_features.py` to see the geometry proven
  on synthetic hands. It is **not wired into the recogniser** — doing that, with
  recorded references and real testing, is the next step.
- `docs/alphabet-recognition-proposal.md` — the design and reasoning: the two
  categories of confusion and the four complementary fixes, with honest limits.
- `docs/sign-confidence-scoring.md` — every sign scored for learner **difficulty**
  and **identification confidence** (now, and projected with the fix), plus the
  list of signs that stay low-confidence.

**Current state of recognition (see the scoring doc for per-sign detail):**
- Confident today: the 4 greetings only.
- Easy to add (references only, no code change): distinct signs — B, L, Y, W, I;
  numbers 1, 4, 5, 7.
- Needs the fix: orientation/spread set — K, P, G, Q, H, U, V.
- Still hard regardless (occlusion/depth): E, M, N, T, R (and fist/ambiguous-number
  stragglers).

### Approach: attempt every sign, flag confidence, refine later

The recogniser already compares against every sign that has reference recordings,
so making the camera *attempt* a sign is a matter of recording its references —
not a code change. The plan is to record references for **all** signs (see
`docs/recording-plan.md`), let the camera do its best on each, and surface the
per-sign confidence (reliable vs best-guess) rather than excluding the hard ones.
Distinct signs work now; orientation signs need the fix; the occluded ones stay
low-confidence — fine-tune from there. Per-sign detail is in
`docs/sign-confidence-scoring.md`.
