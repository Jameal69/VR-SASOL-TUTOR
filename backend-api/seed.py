from app.database import SessionLocal
from app import models


LESSON_1 = {
    "title": "Lesson 1: Introduction",
    "category": "vocabulary",
    "order_index": 1,
}



LESSON_1_SIGNS = [
    {
        "sign_name": "Hello",
        "description": (
            "Raise an open, flat hand to the side of your head and give a small "
            "outward wave or salute. Keep eye contact and a friendly expression."
        ),
        "difficulty_level": 1,
        "regional_variant": "general",
        "expected_landmarks": None,  # captured it later later, AFTERR Christian's reference data exists
    },
    {
        "sign_name": "Goodbye",
        "description": (
            "With an open, flat hand held in front of you, wave side to side "
            "(or flex the fingers down toward the palm). Keep a friendly "
            "expression and eye contact."
        ),
        "difficulty_level": 1,
        "regional_variant": "general",
        "expected_landmarks": None,
    },

    {
        "sign_name": "Please",
        "description": (
            "Start an open, flat hand at your chin and move it forward toward "
            "the person, with a polite expression."
        ),
        "difficulty_level": 1,
        "regional_variant": "general",
        "expected_landmarks": None,
    },
    {
        "sign_name": "Thank You",
        "description": (
            "Move an open, flat hand from your chin/lips forward and slightly "
            "down toward the person you're thanking, with a grateful expression."
        ),
        "difficulty_level": 1,
        "regional_variant": "general",
        "expected_landmarks": None,
    },
]







def seed_lesson_1(db):
    existing = db.query(models.Lesson).filter(models.Lesson.title == LESSON_1["title"]).first()
    if existing:
        print(f"'{LESSON_1['title']}' already exists (id={existing.id}), skipping seed.")
        return existing

    lesson = models.Lesson(
        title=LESSON_1["title"],
        category=LESSON_1["category"],
        order_index=LESSON_1["order_index"],
    )
    db.add(lesson)
    db.flush()  # assigns lesson.id without needing a full commit yet, so the items below can reference it

    for sign in LESSON_1_SIGNS:
        item = models.CurriculumItem(
            lesson_id=lesson.id,
            sign_name=sign["sign_name"],
            description=sign["description"],
            difficulty_level=sign["difficulty_level"],
            regional_variant=sign["regional_variant"],
            expected_landmarks=sign["expected_landmarks"],
        )
        db.add(item)

    db.commit()
    db.refresh(lesson)
    print(f"Seeded '{lesson.title}' (id={lesson.id}) with {len(LESSON_1_SIGNS)} signs.")
    return lesson


def main():
    db = SessionLocal()
    try:
        seed_lesson_1(db)
    finally:
        db.close()


if __name__ == "__main__":
    main()
