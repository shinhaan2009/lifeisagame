"""첫 실행용 예시 데이터. 공식 모집요강이 아니다."""

from __future__ import annotations

import sqlite3

from academic_planner.util import dumps, normalize_checklist, now, shift_days


def apply_seed(conn: sqlite3.Connection) -> None:
    conn.execute(
        """
        UPDATE profile SET
            interests = ?, research_topic = ?, research_status = ?,
            path_preference = 'both', track = 'undecided', updated_at = ?
        WHERE id = 1
        """,
        (
            dumps(["조직공학", "생체재료", "하이드로겔", "Q-mucin"]),
            "Q-mucin과 하이드로겔을 조직공학에 어떻게 연결할 수 있는지",
            "관심 키워드는 정해 두었고, 진행 중인 실험이나 읽은 논문은 아직 적지 않았다.",
            now(),
        ),
    )

    schools = [
        {
            "name": "서울대학교 (예시)",
            "country": "KR",
            "major": "재료·바이오 관련 모집단위",
            "track": "eng",
            "admission_route": "수시 학생부종합",
            "features": "학과 모집단위를 먼저 정한 뒤, 학생부를 보는 전형의 반영 방식만 적는다.",
            "requirements": "공식 요강에서 확인할 것: 학생부 반영 항목, 제출 서류, 전형 일정. 확인한 내용만 이 칸에 남긴다.",
            "lab_notes": "학부 연구 참여가 가능한 생체재료·고분자 랩이 있는지 학과 페이지에서 찾는다.",
            "strategy": "학생부에서 연구·실험 활동을 어떻게 보여줄지 이 칸에 적는다.",
            "deadline": shift_days(120),
            "priority": 4,
            "status": "exploring",
        },
        {
            "name": "KAIST (예시)",
            "country": "KR",
            "major": "바이오및뇌공학",
            "track": "eng",
            "admission_route": "수시 학생부종합",
            "features": "연구 중심 대학. 학부에서 랩 경험을 어떻게 시작하는지 학과 안내를 확인한다.",
            "requirements": "공식 요강에서 확인할 것: 지원 자격, 제출 서류, 면접 여부.",
            "lab_notes": "하이드로겔, 생체재료, 조직공학 키워드로 faculty 목록을 거른다.",
            "strategy": "학생부에서 연구·실험 활동을 어떻게 보여줄지 이 칸에 적는다.",
            "deadline": shift_days(90),
            "priority": 5,
            "status": "match",
        },
        {
            "name": "Johns Hopkins (예시)",
            "country": "US",
            "major": "Biomedical Engineering",
            "track": "eng",
            "admission_route": "RD",
            "features": "바이오메디컬 공학이 별도 학과로 있다. 병원·연구 연결은 학과 소개를 읽고 확인한 사실만 적는다.",
            "requirements": "공식 사이트에서 확인할 것: TOEFL 제출 기준, 에세이 수, 추천서 수, 지원 라운드.",
            "lab_notes": "재생의학, 생체재료 랩 이름을 학과 사람들 페이지에서 찾아 랩 메뉴에 따로 등록한다.",
            "strategy": "ED로 갈지 RD로 갈지, 테스트 제출 여부를 여기 적는다.",
            "deadline": shift_days(200),
            "priority": 4,
            "status": "reach",
        },
        {
            "name": "UC San Diego (예시)",
            "country": "US",
            "major": "Bioengineering",
            "track": "eng",
            "admission_route": "RD",
            "features": "바이오엔지니어링 안에서 생체재료 연구가 넓다. 관심 분과를 하나 고른다.",
            "requirements": "공식 사이트에서 확인할 것: 국제학생 영어 요건, 선수 과목, 지원 마감.",
            "lab_notes": "하이드로겔·약물전달·조직공학 랩을 키워드로 찾는다.",
            "strategy": "학비와 장학금은 확인한 뒤에만 적는다. 추측은 리스크 칸으로 보낸다.",
            "deadline": shift_days(210),
            "priority": 3,
            "status": "exploring",
        },
    ]
    for school in schools:
        conn.execute(
            """
            INSERT INTO schools (
                name, country, major, track, admission_route, features, requirements,
                lab_notes, strategy, deadline, priority, status, created_at
            ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            """,
            (
                school["name"],
                school["country"],
                school["major"],
                school["track"],
                school["admission_route"],
                school["features"],
                school["requirements"],
                school["lab_notes"],
                school["strategy"],
                school["deadline"],
                school["priority"],
                school["status"],
                now(),
            ),
        )

    labs = [
        {
            "name": "하이드로겔 조직공학 랩 (예시)",
            "institution": "소속을 확인한 뒤 학교 이름을 적는다",
            "pi": "교수 이름을 확인한 뒤 적는다",
            "topics": ["조직공학", "하이드로겔", "생체재료"],
            "why_match": "세포가 자랄 환경을 하이드로겔로 만드는 방향이라, 지금 관심 키워드와 맞닿아 있다.",
            "notes": "실제 랩 페이지를 연 뒤 최근 논문 제목으로 이 메모를 바꿔 적는다.",
            "checklist": [
                "최근 논문 1편의 질문과 결론을 다섯 줄로 적는다",
                "내 주제(Q-mucin, 하이드로겔)와 겹치는 문장을 찾는다",
                "학부생이 시작할 수 있는 실험 기술을 목록으로 적는다",
            ],
        },
        {
            "name": "뮤신·점액 생체재료 랩 (예시)",
            "institution": "소속을 확인한 뒤 학교 이름을 적는다",
            "pi": "교수 이름을 확인한 뒤 적는다",
            "topics": ["Q-mucin", "생체재료", "점액 고분자"],
            "why_match": "Q-mucin과 점액 유래 재료를 다루는 랩을 찾을 때 비교 기준으로 둔 메모.",
            "notes": "검색 키워드 예: mucin hydrogel, mucosal biomaterials.",
            "checklist": [
                "뮤신 하이드로겔 리뷰 1편을 고른다",
                "Q-mucin이 그 논문의 재료와 어떻게 다른지 적는다",
                "필요한 화학·생물 선수 과목을 적는다",
            ],
        },
    ]
    for lab in labs:
        conn.execute(
            """
            INSERT INTO labs (
                name, institution, pi, topics, why_match, notes, checklist, created_at
            ) VALUES (?, ?, ?, ?, ?, ?, ?, ?)
            """,
            (
                lab["name"],
                lab["institution"],
                lab["pi"],
                dumps(lab["topics"]),
                lab["why_match"],
                lab["notes"],
                dumps(normalize_checklist([{"text": text, "done": False} for text in lab["checklist"]])),
                now(),
            ),
        )

    tasks = [
        ("관심 논문 2편의 질문과 결론을 적는다", "연구", shift_days(10), "예시 할 일입니다. 읽은 논문 제목으로 바꾸세요."),
        ("TOEFL 목표 점수를 정하고 진단 일정을 적는다", "TOEFL", shift_days(14), "목표 점수를 내 상태에 입력하면 다음 초안부터 그 점수가 제목에 들어갑니다."),
        ("예시 학교 4곳의 요건 칸을 공식 요강으로 채운다", "기타", shift_days(21), "확인하기 전에는 숫자를 새로 만들지 않습니다."),
    ]
    for title, category, due, detail in tasks:
        conn.execute(
            """
            INSERT INTO tasks (title, category, due_date, status, detail, created_at)
            VALUES (?, ?, ?, 'todo', ?, ?)
            """,
            (title, category, due, detail, now()),
        )

    cur = conn.execute(
        """
        INSERT INTO decisions (title, notes, criteria, created_at)
        VALUES (?, ?, ?, ?)
        """,
        (
            "공대 vs 의대 vs 미국 BME (예시)",
            "점수가 높을수록 유리합니다. 예시 점수는 지우고 본인 판단으로 바꾸세요.",
            dumps(
                [
                    {"name": "연구 적합성", "weight": 5},
                    {"name": "입학 현실성", "weight": 4},
                    {"name": "준비 부담이 적음", "weight": 3},
                    {"name": "비용 부담이 적음", "weight": 3},
                    {"name": "장기 커리어", "weight": 5},
                    {"name": "리스크가 감당됨", "weight": 3},
                ]
            ),
            now(),
        ),
    )
    decision_id = cur.lastrowid
    options = [
        {
            "name": "국내 A대학 공대",
            "pros": "생체재료 연구를 학부에서 이어 가기 쉽다.",
            "cons": "학과 선택이 연구 주제와 어긋날 수 있다.",
            "requirements": "내신 목표, 학생부 연구 활동.",
            "risks": "모집단위를 잘못 고르면 관심 랩과 멀어진다.",
            "scores": {
                "연구 적합성": 5,
                "입학 현실성": 3,
                "준비 부담이 적음": 3,
                "비용 부담이 적음": 4,
                "장기 커리어": 4,
                "리스크가 감당됨": 3,
            },
        },
        {
            "name": "국내 B대학 의대",
            "pros": "임상으로 가는 경로가 분명하다.",
            "cons": "지금 하는 재료·하이드로겔 연구와 거리가 멀다.",
            "requirements": "매우 높은 성적과 긴 준비 기간.",
            "risks": "연구 주제를 버려야 할 수 있다.",
            "scores": {
                "연구 적합성": 2,
                "입학 현실성": 2,
                "준비 부담이 적음": 2,
                "비용 부담이 적음": 4,
                "장기 커리어": 5,
                "리스크가 감당됨": 2,
            },
        },
        {
            "name": "미국 C대학 BME",
            "pros": "조직공학 랩 선택 폭이 넓다.",
            "cons": "영어 성적, 에세이, 비용이 동시에 필요하다.",
            "requirements": "TOEFL, 에세이, 추천서, 연구 경험 정리.",
            "risks": "비용과 비자, 점수 일정이 어긋나면 한 해가 밀린다.",
            "scores": {
                "연구 적합성": 5,
                "입학 현실성": 2,
                "준비 부담이 적음": 2,
                "비용 부담이 적음": 2,
                "장기 커리어": 5,
                "리스크가 감당됨": 3,
            },
        },
    ]
    for option in options:
        conn.execute(
            """
            INSERT INTO decision_options (
                decision_id, name, pros, cons, requirements, risks, scores
            ) VALUES (?, ?, ?, ?, ?, ?, ?)
            """,
            (
                decision_id,
                option["name"],
                option["pros"],
                option["cons"],
                option["requirements"],
                option["risks"],
                dumps(option["scores"]),
            ),
        )
