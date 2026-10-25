REFERENCE MANAGER  -  project tool for curating the recogniser's references
===========================================================================

WHERE IT GOES
  Put both files in  ml-service/  (beside references_raw / references_retired):
      ml-service/reference_manager.py
      ml-service/manage.bat

WHAT IT DOES
  - Plays back every reference recording as a hand skeleton so you can SEE it.
  - Shows each one as USED (in references_raw - what the recogniser feeds on)
    or EXCLUDED (in references_retired - parked, ignored).
  - Lets you flip any recording USED <-> EXCLUDED with one key. "Exclude" just
    MOVES the file into references_retired; the recogniser re-reads
    references_raw on every classification, so it stops using it immediately.
    Re-include moves it back. Nothing is deleted.

WHY IT'S SAFE
  - It only MOVES files between references_raw and references_retired.
  - It does NOT import, modify or touch gesture_classifier.py or the /classify
    endpoint. Recognition behaviour is unchanged; only which files are present.
  - Every change is written to reference_manager_log.csv (timestamp, action,
    sign, from, to) so there's an audit trail.

RUN
  Double-click manage.bat  (or:  py -3.12 reference_manager.py)
  Needs Python 3.12 + opencv-python + numpy (same as the recorder/streamer).

CONTROLS
  n / b     next / previous recording
  space     replay
  x         toggle USED <-> EXCLUDED (moves the file)
  f         cycle filter: ALL -> USED only -> EXCLUDED only
  q         quit

NOTE (provenance)
  Excluding/including moves files and renumbers them in the destination folder,
  so SOURCES.csv (which lists file -> signer code) can drift for the moved
  reps. The log records every move. If the team wants SOURCES.csv kept exactly
  in sync, that can be added - say so and it's a small follow-up.
