"""대시보드."""

from __future__ import annotations

import html
import json

import streamlit as st

from academic_planner.db import (
    export_data,
    get_profile,
    import_data,
    list_decisions,
    list_labs,
    list_schools,
    list_tasks,
)
from academic_planner.scoring import weighted_score
from academic_planner.util import PATHS, TRACKS, clip, fmt_date


def _gaps(profile: dict) -> list[str]:
    missing = []
    if not profile.get("interests"):
        missing.append("관심 분야")
    if not (profile.get("research_topic") or profile.get("research_status")):
        missing.append("연구 주제와 진행 상황")
    path = profile.get("path_preference")
    if path in ("abroad", "both") and int(profile.get("toefl_target") or 0) <= 0:
        missing.append("TOEFL 목표 점수")
    if path in ("domestic", "both") and not profile.get("naesin_target"):
        missing.append("내신 목표")
    return missing


def _score_line(toefl_current: int, toefl_target: int) -> str:
    if toefl_target <= 0 and toefl_current <= 0:
        return "아직 없음"
    current = str(toefl_current) if toefl_current else "—"
    target = str(toefl_target) if toefl_target else "—"
    return f"{current} → {target}"


def render() -> None:
    profile = get_profile()
    schools = list_schools()
    labs = list_labs()
    tasks = list_tasks()
    decisions = list_decisions()

    st.markdown('<p class="kicker">Academic Path Planner</p>', unsafe_allow_html=True)
    st.markdown('<h1 class="hero-title">지금 상태에서 목표까지.</h1>', unsafe_allow_html=True)
    st.markdown(
        '<p class="lede">관심 분야, 연구, 성적, 가고 싶은 대학을 입력하면 비교와 할 일 목록이 같은 데이터 위에서 갱신됩니다.</p>',
        unsafe_allow_html=True,
    )

    done = sum(1 for task in tasks if task["status"] == "done")
    open_tasks = [task for task in tasks if task["status"] != "done" and task["due_date"]]
    open_tasks.sort(key=lambda task: task["due_date"])

    c1, c2, c3, c4 = st.columns(4)
    cards = [
        (c1, "방향", PATHS.get(profile["path_preference"], "미정"), TRACKS.get(profile["track"], "")),
        (c2, "TOEFL", _score_line(profile["toefl_current"], profile["toefl_target"]), "현재 → 목표"),
        (c3, "할 일", f"{done}/{len(tasks)}" if tasks else "0", "완료 / 전체"),
        (c4, "저장", f"{len(schools)} · {len(labs)}", "대학 · 랩"),
    ]
    for column, label, value, note in cards:
        with column:
            st.markdown(
                f"""
                <div class="soft-card">
                  <p class="kicker">{html.escape(label)}</p>
                  <p class="metric">{html.escape(str(value))}</p>
                  <p class="muted">{html.escape(note)}</p>
                </div>
                """,
                unsafe_allow_html=True,
            )

    st.write("")
    left, right = st.columns([1.15, 1])
    with left:
        st.subheader("관심 분야와 연구")
        if profile["interests"]:
            pills = "".join(f'<span class="pill">{html.escape(tag)}</span>' for tag in profile["interests"])
            st.markdown(pills, unsafe_allow_html=True)
        topic = profile["research_topic"] or "연구 주제가 아직 없습니다."
        status = clip(profile["research_status"] or "진행 상황을 내 상태에 적어 두세요.")
        st.markdown(f"**{html.escape(topic)}**")
        st.caption(status)
        gaps = _gaps(profile)
        if gaps:
            items = "".join(f"<li>{html.escape(gap)}</li>" for gap in gaps)
            st.markdown(
                f'<p class="muted">아직 비어 있는 입력</p><ul class="gap-list">{items}</ul>',
                unsafe_allow_html=True,
            )

    with right:
        st.subheader("다가오는 할 일")
        if not open_tasks:
            st.caption("날짜가 있는 남은 할 일이 없습니다. 로드맵에서 초안을 만들 수 있습니다.")
        for task in open_tasks[:5]:
            st.markdown(
                f"**{html.escape(task['title'])}**  \n{html.escape(task['category'])} · {fmt_date(task['due_date'])}"
            )

    st.subheader("비교 순위")
    if not decisions:
        st.caption("의사결정 메뉴에서 옵션과 가중치를 만들 수 있습니다.")
    for brief in decisions[:3]:
        from academic_planner.db import get_decision

        full = get_decision(brief["id"])
        if not full:
            continue
        ranked = []
        for option in full["options"]:
            result = weighted_score(full["criteria"], option["scores"])
            if result["score"] is not None:
                ranked.append((result["score"], option["name"], result["filled"], result["total"]))
        ranked.sort(reverse=True)
        if not ranked:
            st.caption(f"{full['title']} — 저장된 점수가 없습니다.")
            continue
        score, name, filled, total = ranked[0]
        st.markdown(
            f"**{html.escape(full['title'])}**  \n"
            f"현재 1위 {html.escape(name)} · {score:.2f} / 5 · {filled}/{total} 항목"
        )

    st.divider()
    st.caption("이름에 (예시)가 있는 항목은 처음 실행 때 넣은 샘플입니다. 입학 요건은 공식 요강을 확인한 뒤에만 적습니다.")
    st.caption("데이터는 이 컴퓨터의 SQLite 파일에만 있습니다.")

    backup, restore = st.columns(2)
    with backup:
        payload = json.dumps(export_data(), ensure_ascii=False, indent=2)
        st.download_button(
            "JSON으로 백업",
            data=payload,
            file_name="academic-path-backup.json",
            mime="application/json",
            width="stretch",
        )
    with restore:
        with st.expander("백업에서 복원"):
            upload = st.file_uploader("JSON 파일", type="json", label_visibility="collapsed")
            confirm = st.checkbox("지금 데이터를 지우고 이 파일로 바꿉니다")
            if st.button("복원", disabled=upload is None or not confirm):
                try:
                    import_data(json.loads(upload.getvalue().decode("utf-8")))
                except (UnicodeDecodeError, json.JSONDecodeError, ValueError, KeyError) as exc:
                    st.error(f"복원하지 못했습니다. {exc}")
                else:
                    st.rerun()
