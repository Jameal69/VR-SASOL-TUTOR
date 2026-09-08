"""
SASL curriculum content — data only, no DB logic.

Owner: Juan (SASL content). Consumed by app/seed.py, which loads CURRICULUM into
the `lessons` / `curriculum_items` tables; after that the existing endpoints in
app.main (GET /api/curriculum/lessons and /{id}) serve it.

Each dict under "curriculum_items" uses exactly the writable columns of
models.CurriculumItem (sign_name, description, difficulty_level,
regional_variant, expected_landmarks), so it unpacks straight into the model.
Each lesson dict uses exactly the writable columns of models.Lesson
(title, category, order_index).

Sources
-------
- Alphabet & number handshapes: "ABC South African Sign Language", Wits Centre
  for Deaf Studies, 2019 (ISBN 978-0-6399687-0-4), cross-checked against the
  RealSASL Handshape Chart (https://www.realsasl.com/). SASL fingerspelling and
  counting are ONE-HANDED.
- The RealSASL chart also fixes several handshape identities used below:
  I(J), K(P), G(Q) and U(H) each share ONE handshape per pair — movement or
  orientation distinguishes the members — and the letters L and V double as the
  numbers 7 and 2. Note SASL 'T' is listed on the chart separately from
  'AMERICAN T'.

Making handshape first-class (optional, backend owner's call)
------------------------------------------------------------
Right now each sign's canonical handshape name is embedded in its `description`
and validated against HANDSHAPES below. To let the ML/feedback layer compare
against a NAMED handshape without parsing text, add a `primary_handshape` column
to models.CurriculumItem via a migration. That's a shared-model change, so it's
proposed here, not made in this file.

VERIFIED
--------
Still False. The content is now cross-checked against the official RealSASL
handshape inventory, but report section 8.1 requires Deaf-advisor sign-off
before it teaches anyone — that human step is what flips this flag, not a chart
match. A few finger configs still to confirm against the chart image
specifically: numbers 3/4/6/8/9, and letters T, M, N.
"""

VERIFIED = False  # flip to True only after Deaf-advisor sign-off (report §8.1)

SOURCES = {
    "alphabet_numbers": "Wits Centre for Deaf Studies, ABC SASL, 2019, ISBN 978-0-6399687-0-4",
    "handshape_inventory": "RealSASL Handshape Chart, https://www.realsasl.com/",
    "verification": "https://www.realsasl.com/",
}

# Canonical SASL handshape names, taken from the RealSASL Handshape Chart.
# This is the subset our curriculum references (the full chart lists ~70).
# A parenthetical member shares the same handshape: the "I" hand is also J, the
# "L" hand is also 7, the "V" hand is also 2, etc.
HANDSHAPES = {
    # counting hands
    "INDEX(1)", "V(2)", "3 HAND", "4 HAND", "5 HAND", "OPEN-A(6)", "L(7)",
    "8 HAND", "9 HAND",
    # alphabet hands
    "A", "B", "C", "D", "E", "F", "G(Q)", "U(H)", "I(J)", "K(P)",
    "M", "N", "O", "R", "S", "T", "W", "X", "Y",
}

# Kept in sync with models.CurriculumItem / models.Lesson writable columns.
_ITEM_COLUMNS = {"sign_name", "description", "difficulty_level", "regional_variant", "expected_landmarks"}
_LESSON_COLUMNS = {"title", "category", "order_index", "curriculum_items"}


def _item(sign_name, description, difficulty_level=1, handshape=None):
    """Build a CurriculumItem-ready dict (keys == model columns).

    If `handshape` is given it must be a canonical name from HANDSHAPES (the
    RealSASL chart). It's validated here and prefixed onto the description so the
    served content names the handshape.
    """
    if handshape is not None and handshape not in HANDSHAPES:
        raise ValueError(f"{sign_name!r}: unknown handshape {handshape!r} (not in the RealSASL chart)")
    if handshape is not None:
        description = f"Handshape '{handshape}' — {description}"
    return {
        "sign_name": sign_name,
        "description": description,
        "difficulty_level": difficulty_level,
        "regional_variant": "general",   # model default; change if a variant is chosen
        "expected_landmarks": None,       # captured later (WBS 2.2.2)
    }


# --------------------------------------------------------------------------
# Lesson 1 — Introduction & Greetings (lexical signs; movement-led, so no single
# handshape tag). Descriptions match docs/curriculum-lesson1.md.
# --------------------------------------------------------------------------
LESSON_1 = {
    "title": "Lesson 1: Introduction",
    "category": "vocabulary",
    "order_index": 1,
    "curriculum_items": [
        _item("Hello",
              "Raise an open, flat hand to the side of your head and give a small "
              "outward wave or salute. Keep eye contact and a friendly expression."),
        _item("Goodbye",
              "With an open, flat hand held in front of you, wave side to side (or "
              "flex the fingers down toward the palm). Keep a friendly expression "
              "and eye contact."),
        _item("Please",
              "Start an open, flat hand at your chin and move it forward toward the "
              "person, with a polite expression."),
        _item("Thank You",
              "Move an open, flat hand from your chin/lips forward and slightly down "
              "toward the person you're thanking, with a grateful expression."),
    ],
}


# --------------------------------------------------------------------------
# Lesson 2 — Alphabet & Fingerspelling (one-handed; handshape names per the
# RealSASL chart). J and Z add movement (difficulty 2).
# --------------------------------------------------------------------------
LESSON_2 = {
    "title": "Lesson 2: Alphabet & Fingerspelling",
    "category": "vocabulary",
    "order_index": 2,
    "curriculum_items": [
        _item("A", "closed fist, thumb resting along the side of the index finger. Palm forward.", handshape="A"),
        _item("B", "flat hand, fingers together, thumb across the palm. Palm forward.", handshape="B"),
        _item("C", "fingers and thumb curved into a 'C'. Palm to the side.", handshape="C"),
        _item("D", "index up; thumb and other fingertips meet in a round 'd'. Palm forward.", handshape="D"),
        _item("E", "fingers curl down to the thumb in a small closed shape. Palm forward.", handshape="E"),
        _item("F", "thumb and index make a circle; the other three fingers point up. Palm forward.", handshape="F"),
        _item("G", "index points to the side, thumb alongside; other fingers closed. (Shares its hand with Q.)", handshape="G(Q)"),
        _item("H", "index and middle held together, pointing to the side. (Uses the U hand.)", handshape="U(H)"),
        _item("I", "little finger up; the rest form a fist. Palm forward.", handshape="I(J)"),
        _item("J", "little finger up, then trace a 'J' in the air. (Same hand as I, plus movement.)", difficulty_level=2, handshape="I(J)"),
        _item("K", "index up, middle up and apart, thumb to the base of the middle finger.", handshape="K(P)"),
        _item("L", "index up and thumb out in an 'L'. Palm forward. (Same hand as the number 7.)", handshape="L(7)"),
        _item("M", "SASL 'M' — confirm the exact finger arrangement against the chart image.", handshape="M"),
        _item("N", "SASL 'N' — confirm the exact finger arrangement against the chart image.", handshape="N"),
        _item("O", "all fingers and thumb curve to touch in an 'O'. Palm forward.", handshape="O"),
        _item("P", "the K hand rotated to point downward. (Shares its hand with K.)", handshape="K(P)"),
        _item("Q", "the G hand rotated to point downward. (Shares its hand with G.)", handshape="G(Q)"),
        _item("R", "index and middle crossed, pointing up. Palm forward.", handshape="R"),
        _item("S", "closed fist, thumb across the front of the fingers. Palm forward.", handshape="S"),
        _item("T", "SASL 'T' — the chart lists this separately from 'AMERICAN T'; confirm the form against the chart image.", handshape="T"),
        _item("U", "index and middle up, held together. (Same hand as H.)", handshape="U(H)"),
        _item("V", "index and middle up, spread into a 'V'. (Same hand as the number 2.)", handshape="V(2)"),
        _item("W", "index, middle and ring up and spread apart. Palm forward.", handshape="W"),
        _item("X", "index up, bent into a hook; other fingers closed. Palm forward.", handshape="X"),
        _item("Y", "thumb and little finger out, other fingers closed (the 'hang-loose' hand).", handshape="Y"),
        _item("Z", "index points out and traces a 'Z' in the air. (Index hand, plus movement.)", difficulty_level=2, handshape="INDEX(1)"),
    ],
}


# --------------------------------------------------------------------------
# Lesson 3 — Numbers 1-9 (one-handed counting hands per the RealSASL chart).
# 10+ is not on the chart, so it's out of scope here until confirmed.
# --------------------------------------------------------------------------
LESSON_3 = {
    "title": "Lesson 3: Numbers",
    "category": "vocabulary",
    "order_index": 3,
    "curriculum_items": [
        _item("1", "index finger up, other fingers and thumb closed. Palm forward.", handshape="INDEX(1)"),
        _item("2", "index and middle spread in a 'V'. (Same hand as the letter V.)", handshape="V(2)"),
        _item("3", "three fingers extended — confirm which fingers against the chart image.", handshape="3 HAND"),
        _item("4", "four fingers extended and spread, thumb folded in. Palm forward.", handshape="4 HAND"),
        _item("5", "all five fingers extended and spread. Palm forward.", handshape="5 HAND"),
        _item("6", "the OPEN-A hand (the chart marks OPEN-A as 6) — confirm against the chart image.", handshape="OPEN-A(6)"),
        _item("7", "index up and thumb out in an 'L'. (Same hand as the letter L.)", handshape="L(7)"),
        _item("8", "the '8 HAND' — confirm the exact finger arrangement against the chart image.", handshape="8 HAND"),
        _item("9", "the '9 HAND' — confirm the exact finger arrangement against the chart image.", handshape="9 HAND"),
    ],
}


CURRICULUM = [LESSON_1, LESSON_2, LESSON_3]


# --------------------------------------------------------------------------
# Self-check: run `python curriculum_data.py` to validate shape before seeding.
# --------------------------------------------------------------------------
def _validate():
    seen_titles, seen_order = set(), set()
    total_items = handshape_items = 0
    for lesson in CURRICULUM:
        assert not (set(lesson) - _LESSON_COLUMNS), f"Unexpected lesson keys in {lesson.get('title')}"
        assert lesson["title"] not in seen_titles, f"Duplicate title: {lesson['title']}"
        assert lesson["order_index"] not in seen_order, f"Duplicate order_index: {lesson['order_index']}"
        seen_titles.add(lesson["title"])
        seen_order.add(lesson["order_index"])
        assert lesson["curriculum_items"], f"{lesson['title']} has no items"
        for item in lesson["curriculum_items"]:
            assert set(item) == _ITEM_COLUMNS, f"Item column mismatch: {set(item) ^ _ITEM_COLUMNS}"
            assert item["sign_name"] and item["description"], f"Empty field on {item.get('sign_name')}"
            assert 1 <= item["difficulty_level"] <= 5, f"Bad difficulty for {item['sign_name']}"
            total_items += 1
            if item["description"].startswith("Handshape '"):
                handshape_items += 1
    return len(CURRICULUM), total_items, handshape_items


if __name__ == "__main__":
    n_lessons, n_items, n_hs = _validate()
    print(f"OK: {n_lessons} lessons, {n_items} curriculum items "
          f"({n_hs} reference a RealSASL handshape), shape matches model columns.")
    print(f"VERIFIED={VERIFIED} — {'safe to seed' if VERIFIED else 'confirm content vs the chart + Deaf advisor first'}.")
