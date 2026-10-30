# lifeisagame — Academic Path Planner

지금 상태와 목표를 한곳에 두고, 대학 비교 · 랩 매칭 · 로드맵 · 의사결정까지 진로 준비를 돕는 로컬 Streamlit 앱입니다.

## 기능

| 화면 | 하는 일 |
| --- | --- |
| 대시보드 | 오늘 기준 진행 상황과 가까운 마감일 요약 |
| 내 상태 | 관심 분야, 연구 주제, 내신/TOEFL/SAT 현재-목표 입력 |
| 대학 비교 | 지원 학교의 요건, 전략, 우선순위, 마감일 관리 |
| 랩 매칭 | 관심 랩 등록과 준비 체크리스트 진행 |
| 로드맵 | 프로필 기반 할 일 초안 자동 제안 |
| 의사결정 | 기준과 가중치를 둔 다지선다 비교 (1~5점 가중 점수) |

## 실행 방법

```bash
pip install -r requirements.txt
streamlit run app.py
```

## 데이터와 프라이버시

- 모든 데이터는 이 컴퓨터의 `data/planner.db`(SQLite)에만 저장됩니다. 외부 서버로 전송되지 않습니다.
- `data/` 디렉터리는 git에 올리지 않습니다. 저장소를 처음 받으면 앱이 첫 실행 시 스키마를 만들고 예시 데이터를 채웁니다.
- 백업이 필요하면 `academic_planner/db.py`의 `export_data()` / `import_data()`를 사용하세요.

## 개발

```bash
pip install -r requirements.txt pytest
pytest -q
```

## 라이선스

MIT
