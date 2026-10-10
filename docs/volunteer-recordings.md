# Volunteer sign recordings

**Status: on hold until Mr Simba confirms we may collect recordings from people
outside the team.** Everything below is ready so we can start as soon as he does.

Why: every reference recording so far comes from one or two team members, so the
recogniser is tuned to how *we* sign. Recordings from more people make it work for
more learners.

---

## 1. Consent note (give this to every volunteer before they record)

> **SASL Tutor: sign recording, consent**
>
> We're a group of students building a South African Sign Language learning app
> for our PRJ3x1 university project. We'd like to record you signing a few words,
> letters and numbers so the app can learn to recognise them.
>
> - **What's recorded:** only the *positions of your hand joints* (numbers), taken
>   from your webcam. **No video, photos or audio are saved.**
> - **Your name is not stored with the recordings.** You get a code (e.g. P03).
>   Only Juan keeps the list linking names to codes, and it never goes into the
>   project's public code repository.
> - **What it's used for:** as reference examples inside the app, for this
>   university project only.
> - **Voluntary:** you can stop at any time, and you can ask for your recordings
>   to be removed later. Tell Juan your code and we'll delete them.
>
> By recording, you agree to the above.

---

## 2. Collect (Juan's recorder tool, `sasl-harvester`)

1. Volunteer reads the consent note, then records with `run.bat`.
   **Enter your signer code (e.g. `P03`) when asked for a name, not your real name.**
2. Only SASL forms from RealSASL. If unsure of a sign, skip it.
3. Head and both shoulders in view, right hand for one-handed signs.
4. The volunteer zips `harvested_recordings` and sends it to Juan.

## 3. Review (Juan)

1. Put everyone's folders under `harvested_recordings/<code>/<sign>/`.
2. Run `review.bat`. Watch every recording as a hand skeleton and **reject**
   wrong signs, ASL forms, half-done attempts and anything odd.
3. Export (`s`), which gives `approved_for_project/<sign>/rep_N.npy`.

## 4. Import + check (Jameal, before anything is merged)

Work on a feature branch from `develop`, never directly on `develop`.

```bash
cd ml-service
py -3.12 import_approved.py "C:/…/sasl-harvester/approved_for_project"           # dry run first
py -3.12 import_approved.py "C:/…/sasl-harvester/approved_for_project" --apply
```

- Copies into `references_raw/<sign>/` with the next free numbers (nothing is
  overwritten) and logs **codes only** in `references_raw/SOURCES.csv`.
- The name → code list (`signer_codes_PRIVATE.csv`) stays next to the approved
  folder, **outside the repo. Never commit it.**

Then check, in this order:

| # | Check | Pass if |
|---|---|---|
| 1 | `python check_references.py` (current scoring) | own-sign count no worse than before |
| 2 | `python check_references.py --scoring best_k` | at least as good as #1 |
| 3 | Live test in Unity: each affected sign 3x + 2 wrong signs | correct signs MATCH, wrong signs unknown/0% |
| 4 | Live test by **someone else** (ideally a volunteer) | same as #3 |

If #2 is better than #1, switch the classifier to best-K scoring in
`backend-api/app/gesture_classifier.py`:

```python
SCORING = "best_k"   # was "all"
```

and repeat #3. Why: with "all", a sign needs half of *all* its references to be
close, so once many people's recordings are mixed in, an attempt can be outvoted
by everyone else's signing style. With "best_k", only the few closest references
count.

## 5. Merge

PR into `develop` with: the new `.npy` files, the updated `SOURCES.csv`, and the
`SCORING` change if made. Paste the check results (#1–#4) in the PR description.

## Removing someone's recordings (consent withdrawn)

Find their code in `references_raw/SOURCES.csv`, **delete** those files (don't move
them to `references_retired/`, which is still in the public repo), remove their
rows, and commit. Note that deleted files stay in older git history; if a full
removal is ever required, ask before rewriting history, since everyone's clone is
affected.
