# Lesson 1: Introduction & Greetings — Sign Descriptions

**Lesson title:** `Lesson 1: Introduction`
**Category:** `vocabulary` (greetings / politeness)
**Order index:** `1`
**Signs:** Hello, Goodbye, Please, Thank You
**Difficulty:** `1` / 5 for all signs (matches Lesson Overview, Screen 6)
**Status:** DRAFT — sign articulation to be confirmed against a SASL reference + Deaf advisor before seeding (see "Verification")

---

## How this file maps to the code

This is the source of truth Nuvaran seeds the database from (`seed.py` → `lessons` + `curriculum_items`).

- The lesson becomes **one `lessons` row**; each sign becomes **one `curriculum_items` row**.
- **`CurriculumItem.description`** is the single string the API returns (`GET /curriculum/lessons/{id}`) and Screen 6 shows — that's the **Description** line under each sign below.
- **`expected_landmarks`** stays `null` until the reference-capture work (WBS 2.2.2).
- **`regional_variant`** defaults to `"general"` (the model default) until a specific variant is chosen.
- The **four-channel breakdown** below is *reference material*, not DB fields. It documents what the feedback engine scores. Channel names use the exact `Feedback.dimension_scores` keys so nothing has to be renamed later: `handshape`, `movement`, `spatial_placement`, `non_manual`.

> **Language family:** SASL's lexicon has mixed roots (including British/BANZSL influence), but its manual alphabet is **one-handed** (per the Wits SASL alphabet book used for Lesson 2), not the two-handed British system. So don't assume either British or American forms — verify each sign against a SASL source (realsasl.com / a Deaf advisor).

---

## Sign 1 — Hello

**Description (DB field):** Raise an open, flat hand to the side of your head and give a small outward wave or salute. Keep eye contact and a friendly expression.

| Channel (`dimension_scores` key) | What good looks like |
|---|---|
| `handshape` | Open flat hand, fingers roughly together, palm facing forward/outward. |
| `movement` | Single controlled outward "salute" from the temple, or a relaxed side-to-side wave. Not a repeated frantic wave. |
| `spatial_placement` | Upper signing space, about head height, dominant-hand side. |
| `non_manual` | Warm expression, direct eye contact, slight smile / raised brows as a greeting. |

**Common errors → `suggestion_text`:**
- Hand too low → *"Raise your hand toward head height."*
- No facial engagement → *"Add eye contact and a friendly expression — greetings are non-manual too."*
- Looks like Goodbye → *"Hello opens near the head; Goodbye waves in neutral space."*

**Regional variant:** wave vs. salute both occur — confirm the most-used form on realsasl.com.

---

## Sign 2 — Goodbye

**Description (DB field):** With an open, flat hand held in front of you, wave side to side (or flex the fingers down toward the palm). Keep a friendly expression and eye contact.

| Channel (`dimension_scores` key) | What good looks like |
|---|---|
| `handshape` | Open flat hand, palm facing forward toward the addressee. |
| `movement` | Side-to-side wave, or fingers flexing down toward the palm and back ("bye-bye"). |
| `spatial_placement` | Neutral space in front of the upper body, chest-to-shoulder height. |
| `non_manual` | Friendly, relaxed expression; eye contact held until the wave finishes. |

**Common errors → `suggestion_text`:**
- Static hand, no movement → *"Goodbye needs the wave — hold the shape and add the motion."*
- Wave up at the head (looks like Hello) → *"Bring the wave down into neutral space in front of you."*

**Regional variant:** finger-flex vs. flat side-to-side wave both common — confirm against reference.

---

## Sign 3 — Please

**Description (DB field):** Start an open, flat hand at your chin and move it forward toward the person, with a polite expression.

| Channel (`dimension_scores` key) | What good looks like |
|---|---|
| `handshape` | Open flat hand, fingers together, palm angled toward the body/upward. |
| `movement` | Single smooth movement: from chin/lips contact, forward and slightly down toward the addressee. |
| `spatial_placement` | Starts at chin/mouth, travels into neutral space in front of the signer. |
| `non_manual` | Polite, soft expression; often mouthing "please". |

**Common errors → `suggestion_text`:**
- No forward movement (just taps chin) → *"Carry the hand forward toward the person, not just to the chin."*
- Confused with Thank You → *"Please and Thank You share a start point — watch the expression and mouthing."*
- Missing non-manual → *"Add the polite expression and mouthing; without it the sign reads as neutral."*

**Regional variant:** Please and Thank You are structurally close in BANZSL and vary regionally — **verify both together** so the contrast is taught correctly.

---

## Sign 4 — Thank You

**Description (DB field):** Move an open, flat hand from your chin/lips forward and slightly down toward the person you're thanking, with a grateful expression.

| Channel (`dimension_scores` key) | What good looks like |
|---|---|
| `handshape` | Open flat hand, fingers together, palm toward the body/face. |
| `movement` | From lips/chin, forward and slightly down toward the person being thanked. |
| `spatial_placement` | Chin/lips → forward into neutral space toward the addressee. |
| `non_manual` | Appreciative expression, eye contact, mouthing "thank you"; often a slight nod. |

**Common errors → `suggestion_text`:**
- Confused with Please → *"Thank You is directed at the person you're thanking; check expression and mouthing."*
- Hand doesn't travel outward → *"Move from the chin toward the person — the direction carries the meaning."*
- Blank face → *"Gratitude lives in the expression as much as the hand."*

**Regional variant:** confirm start height (lips vs. chin) and whether a one- or two-handed form is preferred locally.

---

## Seed data (for `seed.py` — matches `Lesson` + `CurriculumItem` exactly)

```yaml
lesson:
  title: "Lesson 1: Introduction"
  category: vocabulary
  order_index: 1
  curriculum_items:
    - sign_name: "Hello"
      description: "Raise an open, flat hand to the side of your head and give a small outward wave or salute. Keep eye contact and a friendly expression."
      difficulty_level: 1
      regional_variant: general
      expected_landmarks: null   # capture during WBS 2.2.2

    - sign_name: "Goodbye"
      description: "With an open, flat hand held in front of you, wave side to side (or flex the fingers down toward the palm). Keep a friendly expression and eye contact."
      difficulty_level: 1
      regional_variant: general
      expected_landmarks: null

    - sign_name: "Please"
      description: "Start an open, flat hand at your chin and move it forward toward the person, with a polite expression."
      difficulty_level: 1
      regional_variant: general
      expected_landmarks: null

    - sign_name: "Thank You"
      description: "Move an open, flat hand from your chin/lips forward and slightly down toward the person you're thanking, with a grateful expression."
      difficulty_level: 1
      regional_variant: general
      expected_landmarks: null
```

---

## Notes for the team

- **Please vs. Thank You** is the one genuine confusability pair in this lesson. Suggest an explicit Milestone 2 test case where a low `non_manual` + `movement`-direction score separates them (feeds Inathi's testing log).
- `expected_landmarks` is deliberately `null` — it can't be filled until reference/motion-capture data exists (Christian / dataset stream, WBS 2.2.2).
- Put this on a `feature/` branch off `develop` and PR into `develop` (per the README branch workflow) — not straight onto `main`.

## Verification (before this is treated as final — required by §8.1, "nothing about us without us")

The articulations above are a structured first draft. Confirm each against a proper SASL source and get a Deaf advisor / SASL instructor sign-off before it trains the model or teaches a learner:

1. **realsasl.com** — community dictionary, searchable by handshape and location; shows most-voted (most-used) variants.
2. **learnsasl.com** — video dictionary including signs approved by the PanSALB National Language Board for SASL.
3. Confirm the **regional variant** to teach; don't frame one dialect as the only "correct" one (§8.2 / Penn, 1993). Update `regional_variant` if you move off `general`.
4. For each sign, replace draft details with the confirmed form and note the reference video used.

**Checklist:**
- [ ] Hello — form confirmed, advisor signed off
- [ ] Goodbye — form confirmed, advisor signed off
- [ ] Please — form confirmed, contrast with Thank You checked
- [ ] Thank You — form confirmed, contrast with Please checked
