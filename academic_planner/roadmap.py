"""프로필에 맞춰 할 일 초안을 만든다."""

from __future__ import annotations

from academic_planner.util import shift_days


def suggest_tasks(profile: dict) -> list[dict]:
    path = profile.get("path_preference") or "both"
    tasks = [
        {
            "title": "지금 연구 진행 상황을 반 페이지로 정리한다",
            "category": "연구",
            "due_date": shift_days(7),
            "status": "todo",
            "detail": "주제, 이미 한 일, 막힌 점을 구분해 적는다.",
        },
        {
            "title": "관심 분야 논문 2편의 질문과 결론을 메모한다",
            "category": "연구",
            "due_date": shift_days(14),
            "status": "todo",
            "detail": "조직공학, 생체재료, 하이드로겔, Q-mucin 중 이번 주에 읽을 키워드를 하나 고른다.",
        },
        {
            "title": "등록한 대학의 요건 칸을 공식 요강으로 채운다",
            "category": "기타",
            "due_date": shift_days(20),
            "status": "todo",
            "detail": "확인하지 않은 컷이나 순위는 적지 않는다.",
        },
    ]

    if path in ("abroad", "both"):
        current = int(profile.get("toefl_current") or 0)
        target = int(profile.get("toefl_target") or 0)
        if target:
            gap = f"{current}점에서 {target}점" if current else f"목표 {target}점"
            toefl_title = f"TOEFL {gap}까지 주간 학습 계획을 적는다"
        else:
            toefl_title = "TOEFL 목표 점수를 정하고 내 상태에 입력한다"
        tasks.extend(
            [
                {
                    "title": toefl_title,
                    "category": "TOEFL",
                    "due_date": shift_days(10),
                    "status": "todo",
                    "detail": "시험 날짜가 정해지면 로드맵 마감일로 옮긴다.",
                },
                {
                    "title": "SAT/ACT를 제출할지 학교별로 메모한다",
                    "category": "SAT/ACT",
                    "due_date": shift_days(18),
                    "status": "todo",
                    "detail": "테스트 옵셔널이면 제출 여부를 학교 카드의 전략 칸에 적는다.",
                },
                {
                    "title": "공통 에세이에서 쓸 경험 세 가지를 고른다",
                    "category": "에세이",
                    "due_date": shift_days(30),
                    "status": "todo",
                    "detail": "연구 이야기 하나만으로 채우지 않고, 과정과 선택이 보이는 경험을 고른다.",
                },
                {
                    "title": "추천서를 부탁할 사람과 시점을 적는다",
                    "category": "추천서",
                    "due_date": shift_days(40),
                    "status": "todo",
                    "detail": "연구 추천과 교과 추천을 나눠 적는다.",
                },
            ]
        )

    if path in ("domestic", "both"):
        tasks.extend(
            [
                {
                    "title": "내신 현재와 목표를 과목별로 적는다",
                    "category": "내신",
                    "due_date": shift_days(10),
                    "status": "todo",
                    "detail": "내 상태의 내신 칸에 한 줄로 요약한 뒤, 과목 상세는 여기 상세에 남긴다.",
                },
                {
                    "title": "국내 대학마다 학생부 전형을 칸에 적는다",
                    "category": "수시",
                    "due_date": shift_days(16),
                    "status": "todo",
                    "detail": "과학고 기준으로 학생부종합처럼 학생부를 보는 전형만 적는다. 한 학교에 전형이 두 개면 카드를 둘로 나눈다.",
                },
            ]
        )

    tasks.append(
        {
            "title": "가장 가까운 랩 체크리스트에서 비어 있는 경험 하나를 끝낸다",
            "category": "랩 경험",
            "due_date": shift_days(21),
            "status": "todo",
            "detail": "랩 매칭 화면의 '더 필요한 경험' 목록에서 하나를 고른다.",
        }
    )
    return tasks
