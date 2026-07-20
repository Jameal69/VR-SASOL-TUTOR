# VR-SASOL-TUTOR
An immersive VR application for learning South African Sign Language (SASL) 
through real-time gesture recognition and feedback.

## Repo Structure
- `frontend-unity/` — Unity VR project
- `backend-api/` — FastAPI backend
- `ml-service/` — gesture recognition / ML pipeline
- `database/` — database schema and migrations
- `docs/` — architecture decisions, API contracts
- `meeting-notes/` — weekly check-in notes

## Branch Workflow
- `main` — stable, working code only
- `develop` — active development, branch off this
- `feature/<name>` — one task, branch off `develop`, PR back into `develop`

## Getting Started
See `docs/` for setup instructions per component.
