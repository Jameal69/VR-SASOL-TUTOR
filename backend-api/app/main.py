"""
Main FastAPI app. Run with: uvicorn app.main:app --reload

First thing to confirm working: GET /api/health -> {"status": "ok"}
"""
from datetime import datetime
from typing import List

from fastapi import FastAPI, Depends, HTTPException
from fastapi.middleware.cors import CORSMiddleware
from sqlalchemy.orm import Session as DBSession
from sqlalchemy import func

from app.database import Base, engine, get_db
from app import models, schemas, auth

# Creates tables if they don't exist yet. Fine for early dev; swap for Alembic
# migrations once the schema stabilises (see database/ folder for migrations later).
Base.metadata.create_all(bind=engine)

app = FastAPI(title="SASL VR Tutor API")

app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],  # tighten before anything resembling production
    allow_methods=["*"],
    allow_headers=["*"],
)


@app.get("/api/health")
def health():
    return {"status": "ok"}


# ---------------- Auth ----------------

@app.post("/api/auth/register", response_model=schemas.TokenResponse, status_code=201)
def register(payload: schemas.RegisterRequest, db: DBSession = Depends(get_db)):
    existing = db.query(models.User).filter(models.User.email == payload.email).first()
    if existing:
        raise HTTPException(status_code=400, detail="Email already registered")

    user = models.User(
        display_name=payload.display_name,
        email=payload.email,
        hashed_password=auth.hash_password(payload.password),
        sasl_experience_level=payload.sasl_experience_level,
        accessibility_preferences=payload.accessibility_preferences,
    )
    db.add(user)
    db.commit()
    db.refresh(user)

    token = auth.create_access_token(user.id)
    return schemas.TokenResponse(user_id=user.id, access_token=token)


@app.post("/api/auth/login", response_model=schemas.TokenResponse)
def login(payload: schemas.LoginRequest, db: DBSession = Depends(get_db)):
    user = db.query(models.User).filter(models.User.email == payload.email).first()
    if not user or not auth.verify_password(payload.password, user.hashed_password):
        raise HTTPException(status_code=401, detail="Invalid email or password")

    token = auth.create_access_token(user.id)
    return schemas.TokenResponse(user_id=user.id, access_token=token)


# ---------------- User ----------------

@app.get("/api/users/me", response_model=schemas.UserResponse)
def get_me(user: models.User = Depends(auth.get_current_user)):
    return schemas.UserResponse(
        user_id=user.id,
        display_name=user.display_name,
        role=user.role,
        sasl_experience_level=user.sasl_experience_level,
        accessibility_preferences=user.accessibility_preferences or {},
        created_at=user.created_at,
    )


@app.patch("/api/users/me", response_model=schemas.UserResponse)
def update_me(
    payload: schemas.UserUpdateRequest,
    user: models.User = Depends(auth.get_current_user),
    db: DBSession = Depends(get_db),
):
    if payload.accessibility_preferences is not None:
        user.accessibility_preferences = payload.accessibility_preferences
    if payload.sasl_experience_level is not None:
        user.sasl_experience_level = payload.sasl_experience_level
    db.commit()
    db.refresh(user)
    return get_me(user)


# ---------------- Curriculum ----------------

@app.get("/api/curriculum/lessons", response_model=schemas.LessonListResponse)
def list_lessons(
    user: models.User = Depends(auth.get_current_user),
    db: DBSession = Depends(get_db),
):
    lessons = db.query(models.Lesson).order_by(models.Lesson.order_index).all()
    result = []
    for lesson in lessons:
        # crude completion check for now: has the user got any session with an end_time for this lesson?
        session = (
            db.query(models.LearningSession)
            .filter(
                models.LearningSession.user_id == user.id,
                models.LearningSession.lesson_id == lesson.id,
                models.LearningSession.end_time.isnot(None),
            )
            .first()
        )
        accuracy = None
        completed = session is not None
        if session and session.performance_summary:
            accuracy = session.performance_summary.get("avg_accuracy")

        result.append(
            schemas.LessonSummary(
                lesson_id=lesson.id,
                title=lesson.title,
                category=lesson.category,
                completed=completed,
                accuracy=accuracy,
            )
        )
    return schemas.LessonListResponse(lessons=result)


@app.get("/api/curriculum/lessons/{lesson_id}", response_model=schemas.LessonDetailResponse)
def get_lesson(lesson_id: str, db: DBSession = Depends(get_db)):
    lesson = db.query(models.Lesson).filter(models.Lesson.id == lesson_id).first()
    if not lesson:
        raise HTTPException(status_code=404, detail="Lesson not found")

    signs = [
        schemas.CurriculumItemDetail(
            curriculum_item_id=item.id,
            sign_name=item.sign_name,
            description=item.description,
            difficulty_level=item.difficulty_level,
            regional_variant=item.regional_variant,
        )
        for item in lesson.curriculum_items
    ]
    return schemas.LessonDetailResponse(lesson_id=lesson.id, title=lesson.title, signs=signs)


# ---------------- Sessions ----------------

@app.post("/api/sessions", response_model=schemas.SessionStartResponse, status_code=201)
def start_session(
    payload: schemas.SessionStartRequest,
    user: models.User = Depends(auth.get_current_user),
    db: DBSession = Depends(get_db),
):
    session = models.LearningSession(
        user_id=user.id,
        lesson_id=payload.lesson_id,
        device_info=payload.device_info,
    )
    db.add(session)
    db.commit()
    db.refresh(session)
    return schemas.SessionStartResponse(session_id=session.id, start_time=session.start_time)


@app.patch("/api/sessions/{session_id}")
def end_session(
    session_id: str,
    payload: schemas.SessionEndRequest,
    db: DBSession = Depends(get_db),
):
    session = db.query(models.LearningSession).filter(models.LearningSession.id == session_id).first()
    if not session:
        raise HTTPException(status_code=404, detail="Session not found")
    session.end_time = payload.end_time
    session.performance_summary = payload.performance_summary
    db.commit()
    return {"session_id": session.id, "end_time": session.end_time}


# ---------------- Gestures ----------------

@app.post("/api/gestures/classify", response_model=schemas.GestureClassifyResponse)
def classify_gesture(payload: schemas.GestureClassifyRequest):
    """
    PLACEHOLDER — no real classifier wired in yet. Returns a dummy result so
    Unity/frontend work can proceed against this endpoint without waiting on
    Christian's model. Replace the body of this function once ml-service/
    exposes something callable (or confirm this endpoint moves on-device —
    see the open question in docs/api-contract.md).
    """
    return schemas.GestureClassifyResponse(predicted_sign="unknown", confidence_score=0.0)


@app.post("/api/gestures", response_model=schemas.GestureCreateResponse, status_code=201)
def create_gesture_record(
    payload: schemas.GestureCreateRequest,
    db: DBSession = Depends(get_db),
):
    record = models.GestureRecord(
        session_id=payload.session_id,
        curriculum_item_id=payload.curriculum_item_id,
        raw_landmarks=payload.raw_landmarks,
        classified_sign=payload.classified_sign,
        confidence_score=payload.confidence_score,
    )
    db.add(record)
    db.commit()
    db.refresh(record)
    return schemas.GestureCreateResponse(gesture_id=record.id, timestamp=record.timestamp)


# ---------------- Feedback ----------------

@app.post("/api/feedback", response_model=schemas.FeedbackResponse)
def create_feedback(payload: schemas.FeedbackRequest, db: DBSession = Depends(get_db)):
    gesture = db.query(models.GestureRecord).filter(models.GestureRecord.id == payload.gesture_id).first()
    if not gesture:
        raise HTTPException(status_code=404, detail="Gesture record not found")

    # PLACEHOLDER scoring — replace once there's a real basis for per-dimension scores.
    dimension_scores = schemas.DimensionScores(handshape=3, movement=3, spatial_placement=3, non_manual=3)
    feedback = models.Feedback(
        gesture_id=gesture.id,
        accuracy_score=gesture.confidence_score or 0.0,
        dimension_scores=dimension_scores.model_dump(),
        suggestion_text="Placeholder feedback — real scoring logic not implemented yet.",
    )
    db.add(feedback)
    db.commit()
    db.refresh(feedback)

    return schemas.FeedbackResponse(
        feedback_id=feedback.id,
        gesture_id=feedback.gesture_id,
        accuracy_score=feedback.accuracy_score,
        dimension_scores=dimension_scores,
        suggestion_text=feedback.suggestion_text,
    )


# ---------------- Progress ----------------

@app.get("/api/progress", response_model=schemas.ProgressResponse)
def get_progress(
    user: models.User = Depends(auth.get_current_user),
    db: DBSession = Depends(get_db),
):
    lessons = db.query(models.Lesson).order_by(models.Lesson.order_index).all()
    entries = []
    completed_count = 0
    for lesson in lessons:
        session = (
            db.query(models.LearningSession)
            .filter(
                models.LearningSession.user_id == user.id,
                models.LearningSession.lesson_id == lesson.id,
                models.LearningSession.end_time.isnot(None),
            )
            .first()
        )
        completed = session is not None
        accuracy = session.performance_summary.get("avg_accuracy") if (session and session.performance_summary) else None
        if completed:
            completed_count += 1
        entries.append(
            schemas.ProgressLessonEntry(
                lesson_id=lesson.id, title=lesson.title, completed=completed, accuracy=accuracy
            )
        )

    overall = completed_count / len(lessons) if lessons else 0.0
    return schemas.ProgressResponse(overall_completion=overall, lessons=entries)
