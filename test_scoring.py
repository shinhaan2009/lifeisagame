"""scoring 모듈 단위 테스트. 실행: pytest -q"""

from academic_planner.scoring import weighted_score


def test_empty_criteria_returns_none():
    result = weighted_score([], {})
    assert result == {"score": None, "filled": 0, "total": 0}


def test_missing_scores_excluded_from_denominator():
    criteria = [{"name": "A", "weight": 5}, {"name": "B", "weight": 5}]
    result = weighted_score(criteria, {"A": 4})
    assert result["score"] == 4.0
    assert result["filled"] == 1
    assert result["total"] == 2


def test_weighted_average():
    criteria = [{"name": "A", "weight": 1}, {"name": "B", "weight": 3}]
    result = weighted_score(criteria, {"A": 2, "B": 4})
    assert result["score"] == 3.5


def test_zero_weight_criterion_ignored():
    criteria = [{"name": "A", "weight": 0}, {"name": "B", "weight": 2}]
    result = weighted_score(criteria, {"A": 5, "B": 3})
    assert result["score"] == 3.0
    assert result["filled"] == 1
