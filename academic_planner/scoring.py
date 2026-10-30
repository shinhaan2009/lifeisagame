"""의사결정 가중 점수."""

from __future__ import annotations


def weighted_score(criteria: list[dict], scores: dict | None) -> dict:
    """높을수록 유리한 점수만 평균한다. 비어 있는 항목은 분모에서 뺀다."""
    given = scores or {}
    used = []
    for criterion in criteria or []:
        name = str(criterion.get("name", "")).strip()
        if name:
            used.append((name, float(criterion.get("weight") or 0)))

    total = 0.0
    weight_sum = 0.0
    filled = 0
    for name, weight in used:
        raw = given.get(name)
        if raw is None or weight <= 0:
            continue
        total += float(raw) * weight
        weight_sum += weight
        filled += 1

    score = round(total / weight_sum, 2) if weight_sum else None
    return {"score": score, "filled": filled, "total": len(used)}
