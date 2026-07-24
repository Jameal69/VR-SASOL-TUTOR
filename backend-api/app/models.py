"""
SQLAlchemy models — matches M1 report Section 5.3.

Note: the M1 data model doesn't define a separate "Lesson" entity, only
CurriculumItem grouped implicitly by category. Added Lesson here so
GET /curriculum/lessons (api-contract.md) has something to group items by.
Flag this addition in docs/DECISIONS.md so it's not a silent drift from the M1 doc.
"""
import uuid
from datetime import datetime

from sqlalchemy import (
    Column, String, Integer, Float, ForeignKey, DateTime, Enum as SAEnum
)
from sqlalchemy.dialects.postgresql import UUID, JSONB
from sqlalchemy.orm import relationship

from app.database import Base


def gen_uuid():
    return str(uuid.uuid4())


class User(Base):
    __tablename__ = "users"

    id = Column(UUID(as_uuid=False), primary_key=True, default=gen_uuid)
    display_name = Column(String, nullable=False)
    email = Column(String, unique=True, nullable=False, index=True)
    hashed_password = Column(String, nullable=False)
    role = Column(SAEnum("learner", "administrator", name="user_role"), default="learner")
    sasl_experience_level = Column(Integer, default=1)  # 1-5, per Screen 2
    accessibility_preferences = Column(JSONB, default=dict)
    created_at = Column(DateTime, default=datetime.utcnow)

    sessions = relationship("LearningSession", back_populates="user")
    progress_records = relationship("ProgressTracker", back_populates="user")


class Lesson(Base):
    """Groups CurriculumItems for the Lesson Selection/Overview screens (5 & 6)."""
    __tablename__ = "lessons"

    id = Column(UUID(as_uuid=False), primary_key=True, default=gen_uuid)
    title = Column(String, nullable=False)          # e.g. "Lesson 1: Introduction"
    category = Column(String, nullable=False)         # vocabulary / grammar / conversation
    order_index = Column(Integer, nullable=False)      # curriculum ordering, Screen 5

    curriculum_items = relationship("CurriculumItem", back_populates="lesson")


class CurriculumItem(Base):
    __tablename__ = "curriculum_items"

    id = Column(UUID(as_uuid=False), primary_key=True, default=gen_uuid)
    lesson_id = Column(UUID(as_uuid=False), ForeignKey("lessons.id"), nullable=False)
    sign_name = Column(String, nullable=False)          # e.g. "Hello"
    description = Column(String, nullable=True)          # "how it's produced" text, Juan's content
    difficulty_level = Column(Integer, default=1)          # 1-5, Screen 6
    regional_variant = Column(String, default="general")
    expected_landmarks = Column(JSONB, nullable=True)      # reference landmark template, once available

    lesson = relationship("Lesson", back_populates="curriculum_items")
    gesture_records = relationship("GestureRecord", back_populates="curriculum_item")


class LearningSession(Base):
    """Named LearningSession, not Session, to avoid clashing with SQLAlchemy's own Session class."""
    __tablename__ = "learning_sessions"

    id = Column(UUID(as_uuid=False), primary_key=True, default=gen_uuid)
    user_id = Column(UUID(as_uuid=False), ForeignKey("users.id"), nullable=False)
    lesson_id = Column(UUID(as_uuid=False), ForeignKey("lessons.id"), nullable=False)
    start_time = Column(DateTime, default=datetime.utcnow)
    end_time = Column(DateTime, nullable=True)
    device_info = Column(JSONB, default=dict)              # { "mode": "vr"/"desktop", "headset": "..." }
    performance_summary = Column(JSONB, nullable=True)      # written on session end, Screen 9

    user = relationship("User", back_populates="sessions")
    gesture_records = relationship("GestureRecord", back_populates="session")


class GestureRecord(Base):
    __tablename__ = "gesture_records"

    id = Column(UUID(as_uuid=False), primary_key=True, default=gen_uuid)
    session_id = Column(UUID(as_uuid=False), ForeignKey("learning_sessions.id"), nullable=False)
    curriculum_item_id = Column(UUID(as_uuid=False), ForeignKey("curriculum_items.id"), nullable=False)
    raw_landmarks = Column(JSONB, nullable=False)  # stored as JSONB, not Mongo — see DECISIONS.md
    classified_sign = Column(String, nullable=True)
    confidence_score = Column(Float, nullable=True)
    timestamp = Column(DateTime, default=datetime.utcnow)

    session = relationship("LearningSession", back_populates="gesture_records")
    curriculum_item = relationship("CurriculumItem", back_populates="gesture_records")
    feedback = relationship("Feedback", back_populates="gesture_record", uselist=False)


class Feedback(Base):
    __tablename__ = "feedback"

    id = Column(UUID(as_uuid=False), primary_key=True, default=gen_uuid)
    gesture_id = Column(UUID(as_uuid=False), ForeignKey("gesture_records.id"), nullable=False)
    accuracy_score = Column(Float, nullable=False)
    dimension_scores = Column(JSONB, nullable=False)  # { handshape, movement, spatial_placement, non_manual } 1-5
    suggestion_text = Column(String, nullable=True)

    gesture_record = relationship("GestureRecord", back_populates="feedback")


class ProgressTracker(Base):
    __tablename__ = "progress_tracker"

    id = Column(UUID(as_uuid=False), primary_key=True, default=gen_uuid)
    user_id = Column(UUID(as_uuid=False), ForeignKey("users.id"), nullable=False)
    curriculum_item_id = Column(UUID(as_uuid=False), ForeignKey("curriculum_items.id"), nullable=False)
    attempts = Column(Integer, default=0)
    success_rate = Column(Float, default=0.0)
    last_attempt_date = Column(DateTime, nullable=True)

    user = relationship("User", back_populates="progress_records")
