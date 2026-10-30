"""공유 상수와 작은 변환."""

from __future__ import annotations

import json
from datetime import date, datetime, timedelta

INTEREST_PRESETS = [
    "조직공학",
    "생체재료",
    "하이드로겔",
    "Q-mucin",
    "재생의학",
    "약물전달",
    "면역공학",
    "바이오패브리케이션",
    "세포외기질",
    "점액 고분자",
]

PATHS = {
    "domestic": "국내 진학",
    "abroad": "미국 유학",
    "both": "국내와 유학 모두",
}

TRACKS = {
    "med": "의대",
    "eng": "공대",
    "science": "자연대",
    "hybrid": "융합",
    "undecided": "아직 정하지 않음",
}

GRADES = ["고1", "고2", "고3", "졸업/N수", "대학 재학", "기타"]

COUNTRIES = {
    "KR": "국내",
    "US": "미국",
    "OTHER": "그 외",
}

ADMISSION_ROUTES = [
    "수시 학생부교과",
    "수시 학생부종합",
    "수시 논술",
    "ED",
    "EA",
    "RD",
    "기타",
]

SCHOOL_STATUS = {
    "exploring": "탐색 중",
    "reach": "상향",
    "match": "적정",
    "safety": "안정",
    "applying": "지원 예정",
    "chosen": "1순위",
}

TASK_STATUS = {
    "todo": "할 일",
    "doing": "진행 중",
    "done": "완료",
}

TASK_CATEGORIES = [
    "연구",
    "랩 경험",
    "TOEFL",
    "SAT/ACT",
    "에세이",
    "추천서",
    "내신",
    "수시",
    "기타",
]

DEFAULT_CRITERIA = [
    ("연구 적합성", 5),
    ("입학 현실성", 4),
    ("준비 부담이 적음", 3),
    ("비용 부담이 적음", 3),
    ("장기 커리어", 5),
    ("리스크가 감당됨", 3),
]

LAB_CHECKLIST_TEMPLATE = [
    "이 랩 최근 논문 2편을 읽고 한 줄 메모를 남긴다",
    "내 연구 주제와 겹치는 지점을 문단으로 적는다",
    "필요한 선수 과목이나 실험 기술을 적는다",
    "관련 실험이나 프로젝트 경험을 한 가지 이상 정리한다",
    "교수에게 보낼 메일 초안을 쓴다",
    "영어 성적이나 수업 이력이 요건에 맞는지 확인한다",
]


def now() -> str:
    return datetime.now().isoformat(timespec="seconds")


def today() -> date:
    return date.today()


def shift_days(days: int) -> str:
    return (date.today() + timedelta(days=days)).isoformat()


def dumps(value) -> str:
    return json.dumps(value, ensure_ascii=False)


def loads(raw, fallback):
    if raw is None or raw == "":
        return fallback
    if isinstance(raw, (list, dict)):
        return raw
    try:
        return json.loads(raw)
    except (TypeError, json.JSONDecodeError):
        return fallback


def as_int(value) -> int:
    try:
        return int(value or 0)
    except (TypeError, ValueError):
        return 0


def normalize_checklist(items) -> list[dict]:
    cleaned = []
    for item in items or []:
        if isinstance(item, str):
            text, done = item, False
        elif isinstance(item, dict):
            text, done = str(item.get("text", "")), bool(item.get("done", False))
        else:
            continue
        text = text.strip()
        if text:
            cleaned.append({"text": text, "done": done})
    return cleaned


def clip(text: str, limit: int = 180) -> str:
    value = (text or "").strip()
    if len(value) <= limit:
        return value
    return value[: limit - 1] + "…"


def fmt_date(value: str) -> str:
    if not value:
        return "날짜 미정"
    try:
        parsed = date.fromisoformat(value)
    except ValueError:
        return value
    return f"{parsed.year}.{parsed.month:02d}.{parsed.day:02d}"


def merge_tags(selected: list[str], extra: str) -> list[str]:
    tags: list[str] = []
    for source in list(selected) + [part.strip() for part in (extra or "").split(",")]:
        if source and source not in tags:
            tags.append(source)
    return tags


def options_with_current(presets: list[str], current: list[str]) -> list[str]:
    merged = list(presets)
    for item in current:
        if item and item not in merged:
            merged.append(item)
    return merged
