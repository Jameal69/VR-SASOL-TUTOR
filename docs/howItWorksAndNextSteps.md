# How Everything Actually Works Right Now, and What's Left

Last updated: 20 September 2026, after the first successful live end-to-end tests.

## Part 1: What's actually built, and what each thing you're seeing means

There are currently **two separate, disconnected paths** in the system. Understanding that they're separate is the key to understanding everything else in this document.

### Path A: Raw hand tracking → Unity (proven working tonight)

```
Webcam → MediaPipe (landmark_streamer.py) → socket → Unity (LandmarkReceiver.cs) → Console.Log only
```

When you ran `landmark_streamer.py` and pressed Play in Unity, this is the path you tested. **What it proves**: Unity can receive live hand coordinate data from the camera in real time. **What it does NOT do**: it does not know what sign you're making. It has zero classification logic. It just logs raw numbers. The empty `"right_hand": []` lines you saw are correct, not errors, that's MediaPipe honestly reporting "no hand visible this frame."

### Path B: Full classification → Backend (proven working tonight, separately)

```
Webcam → MediaPipe (test_classify_live.py) → HTTP request → FastAPI backend → gesture_classifier.py → real prediction
```

This is the path that returned `{'predicted_sign': 'hello', 'confidence_score': 1.0}`. **What it proves**: the actual classification logic works, correctly, for a real sign. **What it does NOT do**: it has nothing to do with Unity. It's a standalone Python script, not connected to the game engine at all.

### The gap: nobody has connected A and B yet

Right now, Unity receives raw hand data but never asks "what sign is this?" And the thing that knows how to answer that question has never been asked by Unity, only by a separate test script. Closing this gap is the actual next piece of real, functional work, and it's Unity-side code, but it's not visual, it's plumbing.

---

## Part 2: The new script that closes the gap

Below is `GestureClassifierClient.cs`. It reuses the exact same proven socket connection as `LandmarkReceiver.cs`, but instead of just logging frames, it buffers them while you're "recording," and when you stop, sends the whole sequence straight to the real backend and shows you what it classified.

**Where it goes**: same folder as before, `Assets/Scripts/Gesture/GestureClassifierClient.cs`. You can have both this and `LandmarkReceiver.cs` in the project at once, or replace the GameObject's component with this new one, either works, just don't run both connecting to the same socket at the same time, only one can hold that connection.

**How to use it once it's in**:
1. Attach it to a GameObject the same way you attached `LandmarkReceiver.cs`
2. In the Inspector, paste a real `curriculum_item_id` into the `Curriculum Item Id` field, e.g. Hello's ID
3. Run `landmark_streamer.py` first, same as before
4. Press Play in Unity
5. Press **SPACE** to start recording, perform the sign, press **SPACE** again to stop
6. Watch the Console, it'll show the real backend's response, right there inside Unity

That's the actual missing connection, proven inside the game engine itself, not a separate script pretending to be Unity.

---

## Part 3: Step-by-step, adding a brand new sign

Use this any time a new sign needs to be added beyond the current four.

1. **Content**: write up the sign's handshape, movement, placement, and description (Juan's role, or do it yourself with real SASL sources, never guess)
2. **Add it to the database**: extend `seed.py` with a new `CurriculumItem` entry for this sign, matching the existing pattern exactly, then run `python seed.py` again from `backend-api/`
3. **Record references**: from `ml-service/`, run `py -3.12 dtw_recognizer.py record <new_sign_name>`, perform the sign 5-6 times, SPACE to start/stop each rep
4. **Test it standalone**: `py -3.12 dtw_recognizer.py compare <new_sign_name>`, confirm it says MATCH with good confidence
5. **Test it through the real backend**: get the new sign's `curriculum_item_id` from `/docs`, run `py -3.12 test_classify_live.py <that_id>`, confirm a real prediction comes back
6. **Test it through Unity**: same ID, pasted into `GestureClassifierClient`'s Inspector field, SPACE to record in Play mode, confirm the Console shows a real result

---

## Part 4: Step-by-step, testing an existing sign

Quick version of the same thing, for signs already recorded (Hello, Goodbye, Please, Thank You):

1. Get the sign's `curriculum_item_id` from `GET /api/curriculum/lessons/{lesson_id}` in `/docs`
2. Either test via Python directly: `py -3.12 test_classify_live.py <id>`
3. Or test via Unity: paste the ID into `GestureClassifierClient`, Play, SPACE to record, SPACE to stop

---

## Part 5: What's actually left, now that classification reaches Unity

Once `GestureClassifierClient.cs` is in and tested, here's the honest remaining list, in priority order:

1. **A real avatar and scene must exist** for any of this to mean anything visually. This is still the actual bottleneck, everything above works regardless of whether Keegan's scene exists, but a demo needs something on screen.
2. **Automatic triggering instead of SPACE.** Right now recording starts/stops manually. Eventually this should trigger automatically, e.g., when the avatar finishes demonstrating a sign, prompt the learner, capture their attempt, classify it, show feedback, matching Screen 7 in the wireframes. Not needed for proving the pipeline works, needed for an actual usable lesson flow.
3. **Displaying the result visually**, even just simple on-screen text showing "Hello, 100% confidence" would satisfy the core of Screen 7/8, doesn't need to be polished, just visible to a person watching, not only in the Console.
4. **The four-dimension feedback breakdown** (handshape/movement/placement/non-manual) is still a simplified, uniform placeholder on the backend, that's flagged honestly in the code, real per-channel scoring is later work, not urgent given time left.

Given how little time remains, item 3, some minimal on-screen text showing the classification result, is probably the single highest-value next step once a scene and avatar exist at all. It's the difference between "this works in a Console log" and "this is a demo-able thing."
