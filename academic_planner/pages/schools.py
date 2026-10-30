"""대학·전공 비교."""

from __future__ import annotations

from datetime import date

import streamlit as st

from academic_planner.db import delete_school, get_school, list_schools, save_school
from academic_planner.ui import consume_pending, flash, queue_pick, show_flash
from academic_planner.util import (
    ADMISSION_ROUTES,
    COUNTRIES,
    SCHOOL_STATUS,
    TRACKS,
    fmt_date,
    options_with_current,
)

PICK_KEY = "school_pick"


def _blank() -> dict:
    return {
        "id": None,
        "name": "",
        "country": "KR",
        "major": "",
        "track": "undecided",
        "admission_route": "",
        "features": "",
        "requirements": "",
        "lab_notes": "",
        "strategy": "",
        "deadline": "",
        "priority": 3,
        "status": "exploring",
    }


def _strategy_label(country: str) -> str:
    if country == "KR":
        return "학생부·전형 전략"
    if country == "US":
        return "지원 전략 (라운드, 에세이, 시험 제출)"
    return "지원 전략"


def render() -> None:
    st.title("대학 비교")
    st.caption("입학 요건과 랩 정보는 직접 확인한 메모입니다. 모집요강을 대신하지 않습니다.")
    show_flash()
    schools = list_schools()

    compare, edit = st.tabs(["비교", "등록·수정"])
    with compare:
        _render_compare(schools)
    with edit:
        _render_editor(schools)


def _render_compare(schools: list[dict]) -> None:
    if not schools:
        st.info("등록된 대학이 없습니다. 등록·수정 탭에서 추가하세요.")
        return

    filters = st.columns(3)
    with filters[0]:
        country = st.selectbox("국가", ["전체", *COUNTRIES.keys()], format_func=lambda key: "전체" if key == "전체" else COUNTRIES[key])
    with filters[1]:
        track = st.selectbox("계열", ["전체", *TRACKS.keys()], format_func=lambda key: "전체" if key == "전체" else TRACKS[key])
    with filters[2]:
        status = st.selectbox(
            "상태",
            ["전체", *SCHOOL_STATUS.keys()],
            format_func=lambda key: "전체" if key == "전체" else SCHOOL_STATUS[key],
        )

    filtered = [
        school
        for school in schools
        if (country == "전체" or school["country"] == country)
        and (track == "전체" or school["track"] == track)
        and (status == "전체" or school["status"] == status)
    ]
    table = [
        {
            "학교": school["name"],
            "국가": COUNTRIES.get(school["country"], school["country"]),
            "전공": school["major"],
            "계열": TRACKS.get(school["track"], school["track"]),
            "전형": school["admission_route"],
            "관심도": school["priority"],
            "상태": SCHOOL_STATUS.get(school["status"], school["status"]),
            "일정": fmt_date(school["deadline"]) if school["deadline"] else "",
        }
        for school in filtered
    ]
    st.dataframe(table, width="stretch", hide_index=True)
    if not filtered:
        return

    labels = {school["id"]: f"{school['name']} · {school['major'] or '전공 미정'}" for school in filtered}
    default_ids = [school["id"] for school in sorted(filtered, key=lambda item: (-item["priority"], item["name"]))][:3]
    picked = st.multiselect(
        "나란히 볼 학교",
        list(labels),
        default=default_ids,
        format_func=lambda school_id: labels[school_id],
        max_selections=3,
        key=f"school_compare_{country}_{track}_{status}",
    )
    if not picked:
        st.caption("학교를 고르면 요건과 전략을 나란히 봅니다.")
        return
    columns = st.columns(len(picked))
    by_id = {school["id"]: school for school in filtered}
    for column, school_id in zip(columns, picked):
        school = by_id[school_id]
        with column:
            with st.container(border=True):
                st.markdown(f"**{school['name']}**")
                st.caption(
                    f"{COUNTRIES.get(school['country'], '')} · {school['major'] or '전공 미정'} · "
                    f"{school['admission_route'] or '전형 미정'}"
                )
                st.markdown("특징")
                st.write(school["features"] or "—")
                st.markdown("확인한 요건")
                st.write(school["requirements"] or "—")
                st.markdown(_strategy_label(school["country"]))
                st.write(school["strategy"] or "—")
                st.markdown("랩 메모")
                st.write(school["lab_notes"] or "—")
                st.caption(f"관심도 {school['priority']} · {fmt_date(school['deadline']) if school['deadline'] else '일정 미정'}")


def _render_editor(schools: list[dict]) -> None:
    consume_pending(PICK_KEY)
    ids = [None, *[school["id"] for school in schools]]
    if PICK_KEY not in st.session_state or st.session_state[PICK_KEY] not in ids:
        st.session_state[PICK_KEY] = None
    by_id = {school["id"]: school for school in schools}

    def _label(school_id):
        if school_id is None:
            return "＋ 새 대학"
        school = by_id[school_id]
        return f"{school['name']} · {school['major'] or '전공 미정'}"

    school_id = st.selectbox("편집할 대학", ids, format_func=_label, key=PICK_KEY)
    record = _blank() if school_id is None else get_school(school_id) or _blank()
    countries = list(COUNTRIES)
    country = st.selectbox(
        "국가",
        countries,
        index=countries.index(record["country"]) if record["country"] in COUNTRIES else 0,
        format_func=lambda key: COUNTRIES[key],
        key=f"school_country_{school_id}",
    )
    has_deadline = st.checkbox(
        "전형 일정이 있다",
        value=bool(record["deadline"]),
        key=f"school_has_deadline_{school_id}",
    )

    with st.form(f"school_form_{school_id}"):
        name = st.text_input("학교", value=record["name"])
        major = st.text_input("전공·모집단위", value=record["major"])
        track_keys = list(TRACKS)
        track = st.selectbox(
            "계열",
            track_keys,
            index=track_keys.index(record["track"]) if record["track"] in TRACKS else track_keys.index("undecided"),
            format_func=lambda key: TRACKS[key],
        )
        routes = options_with_current(ADMISSION_ROUTES, [record["admission_route"]] if record["admission_route"] else [])
        route = st.selectbox(
            "전형",
            routes,
            index=routes.index(record["admission_route"]) if record["admission_route"] in routes else 0,
        )
        c1, c2 = st.columns(2)
        with c1:
            priority = st.slider("관심도", 1, 5, int(record["priority"] or 3))
        with c2:
            status_keys = list(SCHOOL_STATUS)
            status = st.selectbox(
                "상태",
                status_keys,
                index=status_keys.index(record["status"]) if record["status"] in SCHOOL_STATUS else 0,
                format_func=lambda key: SCHOOL_STATUS[key],
            )
        deadline_value = ""
        if has_deadline:
            initial = date.today()
            if record["deadline"]:
                try:
                    initial = date.fromisoformat(record["deadline"])
                except ValueError:
                    initial = date.today()
            deadline_value = st.date_input("일정", value=initial).isoformat()
        features = st.text_area("특징", value=record["features"], height=80)
        requirements = st.text_area("확인한 입학 요건", value=record["requirements"], height=100)
        strategy = st.text_area(_strategy_label(country), value=record["strategy"], height=90)
        lab_notes = st.text_area("관련 랩 메모", value=record["lab_notes"], height=80)
        submitted = st.form_submit_button("저장", type="primary")

    if submitted:
        if not name.strip():
            st.error("학교 이름을 입력하세요.")
        else:
            new_id = save_school(
                {
                    "id": school_id,
                    "name": name,
                    "country": country,
                    "major": major,
                    "track": track,
                    "admission_route": route,
                    "features": features,
                    "requirements": requirements,
                    "lab_notes": lab_notes,
                    "strategy": strategy,
                    "deadline": deadline_value,
                    "priority": priority,
                    "status": status,
                }
            )
            queue_pick(PICK_KEY, new_id)
            flash("대학을 저장했습니다.")
            st.rerun()

    if school_id is not None and st.button("이 대학 삭제", type="secondary"):
        delete_school(school_id)
        queue_pick(PICK_KEY, None)
        flash("대학을 삭제했습니다.")
        st.rerun()
