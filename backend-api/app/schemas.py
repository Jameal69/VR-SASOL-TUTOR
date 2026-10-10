"""Pydantic schemas — request/response shapes. Mirrors docs/api-contract.md exactly.
If you change a shape here, update api-contract.md in the same PR (or vice versa)."""
from datetime import datetime
from typing import Optional, List, Dict, Any
from pydantic import BaseModel, EmailStr


# ---- Auth ----
class RegisterRequest(BaseModel):
    display_name: str
    email: EmailStr
    password: str
    sasl_experience_level: int = 1
    accessibility_preferences: Dict[str, Any] = {}


class LoginRequest(BaseModel):
    email: EmailStr
    password: str


class TokenResponse(BaseModel):
    user_id: str
    access_token: str
    token_type: str = "bearer"


# ---- User ----
class UserResponse(BaseModel):
    user_id: str
    display_name: str
    role: str
    sasl_experience_level: int
    accessibility_preferences: Dict[str, Any]
    created_at: datetime


class UserUpdateRequest(BaseModel):
    accessibility_preferences: Optional[Dict[str, Any]] = None
    sasl_experience_level: Optional[int] = None


# ---- Curriculum ----
class LessonSummary(BaseModel):
    lesson_id: str
    title: str
    category: str
    completed: bool
    accuracy: Optional[float] = None


class LessonListResponse(BaseModel):
    lessons: List[LessonSummary]


class CurriculumItemDetail(BaseModel):
    curriculum_item_id: str
    sign_name: str
    description: Optional[str] = None
    difficulty_level: int
    regional_variant: str


class LessonDetailResponse(BaseModel):
    lesson_id: str
    title: str
    signs: List[CurriculumItemDetail]


# ---- Sessions ----
class SessionStartRequest(BaseModel):
    lesson_id: str
    device_info: Dict[str, Any] = {}


class SessionStartResponse(BaseModel):
    session_id: str
    start_time: datetime


class SessionEndRequest(BaseModel):
    end_time: datetime
    performance_summary: Dict[str, Any] = {}


# ---- Gesture ----
class LandmarkFrame(BaseModel):
    t: float
    left_hand: List[List[float]] = []
    right_hand: List[List[float]] = []
    # True when the head and both shoulders were clearly in view (sent by
    # landmark_streamer.py). None from older clients, which skips the check.
    body: Optional[bool] = None


class GestureClassifyRequest(BaseModel):
    session_id: str
    curriculum_item_id: str
    landmark_sequence: List[LandmarkFrame]


class GestureClassifyResponse(BaseModel):
    predicted_sign: str
    confidence_score: float
    # Set when the attempt wasn't classified because tracking was too poor
    # ("body_not_visible" / "hand_not_visible" / "too_short"): ask the user to try again
    # instead of showing NO MATCH. None for a normal result.
    retry_reason: Optional[str] = None


class GestureCreateRequest(BaseModel):
    session_id: str
    curriculum_item_id: str
    raw_landmarks: Dict[str, Any]
    classified_sign: str
    confidence_score: float


class GestureCreateResponse(BaseModel):
    gesture_id: str
    timestamp: datetime


# ---- Feedback ----
class FeedbackRequest(BaseModel):
    gesture_id: str


class DimensionScores(BaseModel):
    handshape: int
    movement: int
    spatial_placement: int
    non_manual: int


class FeedbackResponse(BaseModel):
    feedback_id: str
    gesture_id: str
    accuracy_score: float
    dimension_scores: DimensionScores
    suggestion_text: Optional[str] = None


# ---- Progress ----
class ProgressLessonEntry(BaseModel):
    lesson_id: str
    title: str
    completed: bool
    accuracy: Optional[float] = None


class ProgressResponse(BaseModel):
    overall_completion: float
    lessons: List[ProgressLessonEntry]
