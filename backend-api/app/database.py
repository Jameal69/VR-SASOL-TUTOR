"""
Database connection setup.

Decision (see docs/DECISIONS.md): single PostgreSQL instance, no separate MongoDB.
Landmark data is stored as JSONB columns instead of a document store.
"""
import os
from dotenv import load_dotenv
from sqlalchemy import create_engine
from sqlalchemy.orm import sessionmaker, declarative_base

load_dotenv()


DATABASE_URL = os.getenv(
    "DATABASE_URL",
    "postgresql://postgres:password@localhost:5432/sasl_vr_tutor",
)

# Supabase's pooler and campus/home networks silently drop connections that sit
# idle, and reusing a dropped one made a classify request fail or hang.
#   pool_pre_ping: test a pooled connection before using it, reconnect if it's dead
#   pool_recycle:  replace connections older than 5 minutes
#   connect_timeout: give up on a new connection after 10 s instead of hanging
engine = create_engine(
    DATABASE_URL,
    pool_pre_ping=True,
    pool_recycle=300,
    connect_args={"connect_timeout": 10},
)
SessionLocal = sessionmaker(autocommit=False, autoflush=False, bind=engine)
Base = declarative_base()


def get_db():
    """FastAPI dependency — yields a DB session per request, closes it after."""
    db = SessionLocal()
    try:
        yield db
    finally:
        db.close()
