"""Academic Path Planner의 로컬 SQLite 저장소."""

from __future__ import annotations

import sqlite3
from contextlib import contextmanager
from pathlib import Path

from academic_planner.util import as_int, dumps, loads, normalize_checklist, now

ROOT = Path(__file__).resolve().parent.parent
DATA_DIR = ROOT / "data"
DB_PATH = DATA_DIR / "planner.db"

SCHEMA = """
CREATE TABLE IF NOT EXISTS profile (
    id INTEGER PRIMARY KEY CHECK (id = 1),
    name TEXT NOT NULL DEFAULT '',
    grade TEXT NOT NULL DEFAULT '',
    interests TEXT NOT NULL DEFAULT '[]',
    research_topic TEXT NOT NULL DEFAULT '',
    research_status TEXT NOT NULL DEFAULT '',
    naesin_current TEXT NOT NULL DEFAULT '',
    naesin_target TEXT NOT NULL DEFAULT '',
    csat_target TEXT NOT NULL DEFAULT '',
    toefl_current INTEGER NOT NULL DEFAULT 0,
    toefl_target INTEGER NOT NULL DEFAULT 0,
    sat_current INTEGER NOT NULL DEFAULT 0,
    sat_target INTEGER NOT NULL DEFAULT 0,
    path_preference TEXT NOT NULL DEFAULT 'both',
    track TEXT NOT NULL DEFAULT 'undecided',
    notes TEXT NOT NULL DEFAULT '',
    updated_at TEXT NOT NULL DEFAULT ''
);

CREATE TABLE IF NOT EXISTS schools (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL,
    country TEXT NOT NULL DEFAULT 'KR',
    major TEXT NOT NULL DEFAULT '',
    track TEXT NOT NULL DEFAULT 'undecided',
    admission_route TEXT NOT NULL DEFAULT '',
    features TEXT NOT NULL DEFAULT '',
    requirements TEXT NOT NULL DEFAULT '',
    lab_notes TEXT NOT NULL DEFAULT '',
    strategy TEXT NOT NULL DEFAULT '',
    deadline TEXT NOT NULL DEFAULT '',
    priority INTEGER NOT NULL DEFAULT 3,
    status TEXT NOT NULL DEFAULT 'exploring',
    created_at TEXT NOT NULL DEFAULT ''
);

CREATE TABLE IF NOT EXISTS labs (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL,
    institution TEXT NOT NULL DEFAULT '',
    pi TEXT NOT NULL DEFAULT '',
    topics TEXT NOT NULL DEFAULT '[]',
    why_match TEXT NOT NULL DEFAULT '',
    notes TEXT NOT NULL DEFAULT '',
    checklist TEXT NOT NULL DEFAULT '[]',
    created_at TEXT NOT NULL DEFAULT ''
);

CREATE TABLE IF NOT EXISTS tasks (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    title TEXT NOT NULL,
    category TEXT NOT NULL DEFAULT '기타',
    due_date TEXT NOT NULL DEFAULT '',
    status TEXT NOT NULL DEFAULT 'todo',
    detail TEXT NOT NULL DEFAULT '',
    created_at TEXT NOT NULL DEFAULT ''
);

CREATE TABLE IF NOT EXISTS decisions (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    title TEXT NOT NULL,
    notes TEXT NOT NULL DEFAULT '',
    criteria TEXT NOT NULL DEFAULT '[]',
    created_at TEXT NOT NULL DEFAULT ''
);

CREATE TABLE IF NOT EXISTS decision_options (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    decision_id INTEGER NOT NULL,
    name TEXT NOT NULL,
    pros TEXT NOT NULL DEFAULT '',
    cons TEXT NOT NULL DEFAULT '',
    requirements TEXT NOT NULL DEFAULT '',
    risks TEXT NOT NULL DEFAULT '',
    scores TEXT NOT NULL DEFAULT '{}',
    FOREIGN KEY (decision_id) REFERENCES decisions(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS meta (
    key TEXT PRIMARY KEY,
    value TEXT NOT NULL
);
"""


@contextmanager
def connect():
    DATA_DIR.mkdir(parents=True, exist_ok=True)
    conn = sqlite3.connect(DB_PATH)
    conn.row_factory = sqlite3.Row
    conn.execute("PRAGMA foreign_keys = ON")
    try:
        yield conn
        conn.commit()
    except Exception:
        conn.rollback()
        raise
    finally:
        conn.close()


def init_db() -> None:
    with connect() as conn:
        conn.executescript(SCHEMA)
        exists = conn.execute("SELECT id FROM profile WHERE id = 1").fetchone()
        if exists is None:
            conn.execute(
                "INSERT INTO profile (id, interests, updated_at) VALUES (1, '[]', ?)",
                (now(),),
            )
        seeded = conn.execute("SELECT 1 FROM meta WHERE key = 'seeded'").fetchone()
        if seeded is None:
            from academic_planner.seed import apply_seed

            apply_seed(conn)
            conn.execute("INSERT INTO meta (key, value) VALUES ('seeded', '1')")


def _profile_from_row(row: sqlite3.Row) -> dict:
    item = dict(row)
    item["interests"] = loads(item["interests"], [])
    return item


def get_profile() -> dict:
    with connect() as conn:
        row = conn.execute("SELECT * FROM profile WHERE id = 1").fetchone()
    return _profile_from_row(row)


def _save_profile(conn: sqlite3.Connection, data: dict) -> None:
    conn.execute(
        """
        UPDATE profile SET
            name = ?, grade = ?, interests = ?, research_topic = ?, research_status = ?,
            naesin_current = ?, naesin_target = ?, csat_target = ?,
            toefl_current = ?, toefl_target = ?, sat_current = ?, sat_target = ?,
            path_preference = ?, track = ?, notes = ?, updated_at = ?
        WHERE id = 1
        """,
        (
            (data.get("name") or "").strip(),
            data.get("grade") or "",
            dumps(data.get("interests") or []),
            (data.get("research_topic") or "").strip(),
            (data.get("research_status") or "").strip(),
            (data.get("naesin_current") or "").strip(),
            (data.get("naesin_target") or "").strip(),
            (data.get("csat_target") or "").strip(),
            as_int(data.get("toefl_current")),
            as_int(data.get("toefl_target")),
            as_int(data.get("sat_current")),
            as_int(data.get("sat_target")),
            data.get("path_preference") or "both",
            data.get("track") or "undecided",
            (data.get("notes") or "").strip(),
            data.get("updated_at") or now(),
        ),
    )


def save_profile(data: dict) -> None:
    with connect() as conn:
        _save_profile(conn, data)


def list_schools() -> list[dict]:
    with connect() as conn:
        rows = conn.execute(
            "SELECT * FROM schools ORDER BY priority DESC, name COLLATE NOCASE"
        ).fetchall()
    return [dict(row) for row in rows]


def get_school(school_id: int) -> dict | None:
    with connect() as conn:
        row = conn.execute("SELECT * FROM schools WHERE id = ?", (school_id,)).fetchone()
    return dict(row) if row else None


def _save_school(conn: sqlite3.Connection, data: dict) -> int:
    payload = (
        (data.get("name") or "").strip(),
        data.get("country") or "KR",
        (data.get("major") or "").strip(),
        data.get("track") or "undecided",
        data.get("admission_route") or "",
        (data.get("features") or "").strip(),
        (data.get("requirements") or "").strip(),
        (data.get("lab_notes") or "").strip(),
        (data.get("strategy") or "").strip(),
        data.get("deadline") or "",
        as_int(data.get("priority") or 3),
        data.get("status") or "exploring",
    )
    school_id = data.get("id")
    if school_id:
        conn.execute(
            """
            UPDATE schools SET
                name = ?, country = ?, major = ?, track = ?, admission_route = ?,
                features = ?, requirements = ?, lab_notes = ?, strategy = ?,
                deadline = ?, priority = ?, status = ?
            WHERE id = ?
            """,
            (*payload, school_id),
        )
        return int(school_id)
    created = data.get("created_at") or now()
    cur = conn.execute(
        """
        INSERT INTO schools (
            name, country, major, track, admission_route, features, requirements,
            lab_notes, strategy, deadline, priority, status, created_at
        ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
        """,
        (*payload, created),
    )
    return int(cur.lastrowid)


def save_school(data: dict) -> int:
    with connect() as conn:
        return _save_school(conn, data)


def delete_school(school_id: int) -> None:
    with connect() as conn:
        conn.execute("DELETE FROM schools WHERE id = ?", (school_id,))


def _lab_from_row(row: sqlite3.Row) -> dict:
    item = dict(row)
    item["topics"] = loads(item["topics"], [])
    item["checklist"] = normalize_checklist(loads(item["checklist"], []))
    return item


def list_labs() -> list[dict]:
    with connect() as conn:
        rows = conn.execute("SELECT * FROM labs ORDER BY name COLLATE NOCASE").fetchall()
    return [_lab_from_row(row) for row in rows]


def get_lab(lab_id: int) -> dict | None:
    with connect() as conn:
        row = conn.execute("SELECT * FROM labs WHERE id = ?", (lab_id,)).fetchone()
    return _lab_from_row(row) if row else None


def _save_lab(conn: sqlite3.Connection, data: dict) -> int:
    payload = (
        (data.get("name") or "").strip(),
        (data.get("institution") or "").strip(),
        (data.get("pi") or "").strip(),
        dumps(data.get("topics") or []),
        (data.get("why_match") or "").strip(),
        (data.get("notes") or "").strip(),
        dumps(normalize_checklist(data.get("checklist") or [])),
    )
    lab_id = data.get("id")
    if lab_id:
        conn.execute(
            """
            UPDATE labs SET
                name = ?, institution = ?, pi = ?, topics = ?,
                why_match = ?, notes = ?, checklist = ?
            WHERE id = ?
            """,
            (*payload, lab_id),
        )
        return int(lab_id)
    cur = conn.execute(
        """
        INSERT INTO labs (
            name, institution, pi, topics, why_match, notes, checklist, created_at
        ) VALUES (?, ?, ?, ?, ?, ?, ?, ?)
        """,
        (*payload, data.get("created_at") or now()),
    )
    return int(cur.lastrowid)


def save_lab(data: dict) -> int:
    with connect() as conn:
        return _save_lab(conn, data)


def set_lab_checklist(lab_id: int, checklist: list) -> None:
    with connect() as conn:
        conn.execute(
            "UPDATE labs SET checklist = ? WHERE id = ?",
            (dumps(normalize_checklist(checklist)), lab_id),
        )


def delete_lab(lab_id: int) -> None:
    with connect() as conn:
        conn.execute("DELETE FROM labs WHERE id = ?", (lab_id,))


def list_tasks() -> list[dict]:
    with connect() as conn:
        rows = conn.execute(
            """
            SELECT * FROM tasks
            ORDER BY CASE WHEN due_date = '' THEN 1 ELSE 0 END, due_date, id
            """
        ).fetchall()
    return [dict(row) for row in rows]


def _save_task(conn: sqlite3.Connection, data: dict) -> int:
    payload = (
        (data.get("title") or "").strip(),
        data.get("category") or "기타",
        data.get("due_date") or "",
        data.get("status") or "todo",
        (data.get("detail") or "").strip(),
    )
    task_id = data.get("id")
    if task_id:
        conn.execute(
            """
            UPDATE tasks SET title = ?, category = ?, due_date = ?, status = ?, detail = ?
            WHERE id = ?
            """,
            (*payload, task_id),
        )
        return int(task_id)
    cur = conn.execute(
        """
        INSERT INTO tasks (title, category, due_date, status, detail, created_at)
        VALUES (?, ?, ?, ?, ?, ?)
        """,
        (*payload, data.get("created_at") or now()),
    )
    return int(cur.lastrowid)


def save_task(data: dict) -> int:
    with connect() as conn:
        return _save_task(conn, data)


def set_task_status(task_id: int, status: str) -> None:
    with connect() as conn:
        conn.execute("UPDATE tasks SET status = ? WHERE id = ?", (status, task_id))


def delete_task(task_id: int) -> None:
    with connect() as conn:
        conn.execute("DELETE FROM tasks WHERE id = ?", (task_id,))


def add_tasks_if_new(tasks: list[dict]) -> int:
    existing = {item["title"] for item in list_tasks()}
    added = 0
    for task in tasks:
        title = (task.get("title") or "").strip()
        if not title or title in existing:
            continue
        save_task(task)
        existing.add(title)
        added += 1
    return added


def list_decisions() -> list[dict]:
    with connect() as conn:
        rows = conn.execute("SELECT * FROM decisions ORDER BY id DESC").fetchall()
    items = []
    for row in rows:
        item = dict(row)
        item["criteria"] = loads(item["criteria"], [])
        items.append(item)
    return items


def get_decision(decision_id: int) -> dict | None:
    with connect() as conn:
        row = conn.execute("SELECT * FROM decisions WHERE id = ?", (decision_id,)).fetchone()
        if row is None:
            return None
        options = conn.execute(
            "SELECT * FROM decision_options WHERE decision_id = ? ORDER BY id",
            (decision_id,),
        ).fetchall()
    item = dict(row)
    item["criteria"] = loads(item["criteria"], [])
    item["options"] = []
    for option in options:
        parsed = dict(option)
        parsed["scores"] = loads(parsed["scores"], {})
        item["options"].append(parsed)
    return item


def _save_decision(conn: sqlite3.Connection, data: dict) -> int:
    payload = (
        (data.get("title") or "").strip(),
        (data.get("notes") or "").strip(),
        dumps(data.get("criteria") or []),
    )
    decision_id = data.get("id")
    if decision_id:
        conn.execute(
            "UPDATE decisions SET title = ?, notes = ?, criteria = ? WHERE id = ?",
            (*payload, decision_id),
        )
        return int(decision_id)
    cur = conn.execute(
        "INSERT INTO decisions (title, notes, criteria, created_at) VALUES (?, ?, ?, ?)",
        (*payload, data.get("created_at") or now()),
    )
    return int(cur.lastrowid)


def save_decision(data: dict) -> int:
    with connect() as conn:
        return _save_decision(conn, data)


def _save_option(conn: sqlite3.Connection, data: dict) -> int:
    payload = (
        int(data["decision_id"]),
        (data.get("name") or "").strip(),
        (data.get("pros") or "").strip(),
        (data.get("cons") or "").strip(),
        (data.get("requirements") or "").strip(),
        (data.get("risks") or "").strip(),
        dumps(data.get("scores") or {}),
    )
    option_id = data.get("id")
    if option_id:
        conn.execute(
            """
            UPDATE decision_options SET
                decision_id = ?, name = ?, pros = ?, cons = ?,
                requirements = ?, risks = ?, scores = ?
            WHERE id = ?
            """,
            (*payload, option_id),
        )
        return int(option_id)
    cur = conn.execute(
        """
        INSERT INTO decision_options (
            decision_id, name, pros, cons, requirements, risks, scores
        ) VALUES (?, ?, ?, ?, ?, ?, ?)
        """,
        payload,
    )
    return int(cur.lastrowid)


def save_option(data: dict) -> int:
    with connect() as conn:
        return _save_option(conn, data)


def delete_option(option_id: int) -> None:
    with connect() as conn:
        conn.execute("DELETE FROM decision_options WHERE id = ?", (option_id,))


def delete_decision(decision_id: int) -> None:
    with connect() as conn:
        conn.execute("DELETE FROM decisions WHERE id = ?", (decision_id,))


def export_data() -> dict:
    decisions = []
    for item in list_decisions():
        full = get_decision(item["id"])
        if full:
            decisions.append(full)
    return {
        "profile": get_profile(),
        "schools": list_schools(),
        "labs": list_labs(),
        "tasks": list_tasks(),
        "decisions": decisions,
    }


def import_data(payload: dict) -> None:
    if not isinstance(payload, dict):
        raise ValueError("백업 파일 형식이 아닙니다.")
    with connect() as conn:
        conn.execute("DELETE FROM decision_options")
        conn.execute("DELETE FROM decisions")
        conn.execute("DELETE FROM tasks")
        conn.execute("DELETE FROM labs")
        conn.execute("DELETE FROM schools")
        _save_profile(conn, payload.get("profile") or {})
        for school in payload.get("schools") or []:
            item = dict(school)
            item.pop("id", None)
            _save_school(conn, item)
        for lab in payload.get("labs") or []:
            item = dict(lab)
            item.pop("id", None)
            _save_lab(conn, item)
        for task in payload.get("tasks") or []:
            item = dict(task)
            item.pop("id", None)
            _save_task(conn, item)
        for decision in payload.get("decisions") or []:
            item = dict(decision)
            options = item.pop("options", []) or []
            item.pop("id", None)
            decision_id = _save_decision(conn, item)
            for option in options:
                option_item = dict(option)
                option_item.pop("id", None)
                option_item["decision_id"] = decision_id
                _save_option(conn, option_item)
