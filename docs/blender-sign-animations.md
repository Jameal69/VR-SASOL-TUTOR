# Hand-animating moving signs in Blender

Static signs (most letters and numbers) are generated automatically from the
reference recordings (Unity menu **SASL → Build Sign Animations**). Signs that
**move** need a hand-made animation:

| Sign | Lesson | RealSASL reference |
|---|---|---|
| hello | 1 | https://www.realsasl.com/?vid=922 |
| goodbye | 1 | https://www.realsasl.com/?vid=1059&tag=BYE%20BYE |
| please | 1 | https://www.realsasl.com/?vid=786 |
| thank_you | 1 | https://www.realsasl.com/?vid=785&tag=THANK%20YOU |
| j | 2 | search "J" on https://www.realsasl.com |
| z | 2 | search "Z" on https://www.realsasl.com |

**Copy the SASL form from RealSASL, not ASL.** If unsure, ask Juan before animating.

## Rules

- Unity **6000.5.4f1** only. Work on a branch `feature/sign-<name>` from `develop`.
- Animate the **right arm and right hand only**. The avatar plays signs on a
  layer that only uses the right arm + fingers, so anything else is ignored.
- One sign per file, named exactly as in the table: `hello.fbx`, `thank_you.fbx`, ...
- 30 fps, about **1–2.5 seconds**. Start from the sign's starting position (the
  avatar blends in from Idle automatically); don't loop.

## Steps

1. **Install Blender** (free): https://www.blender.org/download/
2. **Import the avatar:** File → Import → FBX → `frontend-unity/SASLTutorVR/Assets/Models/X Bot.fbx`.
3. **Animate:** select the armature → Pose Mode. Play the RealSASL video next to
   Blender and key the right shoulder, upper arm, forearm, hand and every finger
   joint at the important moments (start, middle, end). Insert keys with `I`.
   Check it from the front, the way the camera sees the avatar.
4. **Export:** File → Export → FBX:
   - Limit to: **Selected Objects** (select the armature, and the mesh if you like)
   - Armature: **Add Leaf Bones off**
   - Bake Animation: **on**
   - Save as `<sign>.fbx` (see table)
5. **Into Unity:** put the file in `Assets/Animations/Signs/Handmade/`. Select it →
   **Rig** tab: Animation Type **Humanoid**, Avatar Definition **Create From This
   Model** → Apply. **Animation** tab: **Loop Time off** → Apply.
6. **Check:** menu **SASL → Build Sign Animations** (a hand-made clip automatically
   replaces the generated placeholder), then **SASL → Render Sign Previews** and
   Play the scene: select the sign with keys 1–4, press **D** to replay.
   If the arm or fingers look twisted, ask. Usually it's an export setting.

## Done means

- [ ] Looks like the RealSASL video from the front, and **Juan has confirmed it's the SASL form**
- [ ] Plays in Unity with keys 1–4 / D, no twisted joints
- [ ] Pull request into `develop` with **only** `Assets/Animations/Signs/Handmade/<sign>.fbx` and its `.meta`
      (no `ProjectSettings/`, `Packages/` or scene changes), plus a screenshot or short screen recording
