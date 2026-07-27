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
