"""
Seed the curriculum tables from app.curriculum_data.

This is the link between the curriculum content and the live API: it loads
CURRICULUM into the `lessons` / `curriculum_items` tables, after which the
existing endpoints in app.main return real data:
    GET /api/curriculum/lessons          -> the seeded lessons
    GET /api/curriculum/lessons/{id}     -> that lesson's signs + descriptions

Run from backend-api/ with the DB env vars set (see backend-api/README.md):
    python -m app.seed

Behaviour:
- Idempotent: a lesson already present (matched by title) is skipped, so
  re-running never duplicates rows.
- Gated on curriculum_data.VERIFIED: refuses to load unverified handshapes
  unless you pass --force.

NOTE (lane): the backend README lists "write a seed.py" under Nuvaran's tasks.
This overlaps that — coordinate before committing so ownership is clear.
"""
import argparse

from app.database import SessionLocal, Base, engine
from app import models
from app.curriculum_data import CURRICULUM, VERIFIED


def seed(db, *, force=False):
    """Insert any lessons/items not already present. Returns (lessons, items) created."""
    if not VERIFIED and not force:
        raise SystemExit(
            "Refusing to seed: curriculum_data.VERIFIED is False — handshapes are "
            "not yet confirmed against the Wits book / Deaf advisor. "
            "Re-run with --force to seed the draft content anyway."
        )

    created_lessons = created_items = 0
    for lesson in CURRICULUM:
        exists = (
            db.query(models.Lesson)
            .filter(models.Lesson.title == lesson["title"])
            .first()
        )
        if exists:
            continue  # already seeded — stay idempotent

        lesson_row = models.Lesson(
            title=lesson["title"],
            category=lesson["category"],
            order_index=lesson["order_index"],
        )
        db.add(lesson_row)
        db.flush()  # assigns lesson_row.id for the FK below

        for item in lesson["curriculum_items"]:
            # keys of `item` are exactly CurriculumItem's writable columns,
            # so this unpacks straight in (validated by curriculum_data._validate)
            db.add(models.CurriculumItem(lesson_id=lesson_row.id, **item))
            created_items += 1
        created_lessons += 1

    db.commit()
    return created_lessons, created_items


def main():
    parser = argparse.ArgumentParser(description="Seed SASL curriculum content.")
    parser.add_argument(
        "--force", action="store_true",
        help="seed even though curriculum_data.VERIFIED is False",
    )
    args = parser.parse_args()

    # Dev convenience: create tables if they don't exist yet (mirrors app.main).
    Base.metadata.create_all(bind=engine)

    db = SessionLocal()
    try:
        lessons, items = seed(db, force=args.force)
    finally:
        db.close()

    if lessons == 0:
        print("Nothing to do — curriculum already seeded.")
    else:
        print(f"Seeded {lessons} lesson(s) and {items} curriculum item(s).")
    if not VERIFIED:
        print("Reminder: VERIFIED is False — this is draft content pending sign-off.")


if __name__ == "__main__":
    main()
