import json
from datetime import datetime, date
from sqlalchemy import inspect

from app.database import engine, SessionLocal
from app import models
"""
Dumps every row from every table into a single JSON file, as a safety net
in case the Supabase project ever becomes unrecoverable.{it did give me a warning about freezing so this is just a safety precaution}

Run with: python backup.py
(from inside backend-api/, with the venv active, same as running the server)

"""

def default_serializer(obj):
    """Handles datetime objects, which json.dumps can't serialize on its own."""
    if isinstance(obj, (datetime, date)):
        return obj.isoformat()
    raise TypeError(f"Type {type(obj)} not serializable")


def main():
    db = SessionLocal()
    inspector = inspect(engine)
    table_names = inspector.get_table_names()

    backup = {}
    for table_name in table_names:
        # Reflect each table generically, so this works even if new tables
        # get added to models.py later without needing to update this script.
        columns = [col["name"] for col in inspector.get_columns(table_name)]
        rows = db.execute(f'SELECT * FROM "{table_name}"').fetchall()
        backup[table_name] = [dict(zip(columns, row)) for row in rows]
        print(f"  {table_name}: {len(rows)} rows")

    db.close()

    timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
    filename = f"backup_{timestamp}.json"
    with open(filename, "w") as f:
        json.dump(backup, f, indent=2, default=default_serializer)

    print(f"\nBackup saved to {filename}")


if __name__ == "__main__":
    main()
