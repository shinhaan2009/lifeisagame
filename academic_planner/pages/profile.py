"""프로필과 현재 상태."""

from __future__ import annotations

import streamlit as st

from academic_planner.db import get_profile, save_profile
from academic_planner.ui import flash, show_flash
from academic_planner.util import (
    GRADES,
    INTEREST_PRESETS,
    PATHS,
    TRACKS,
    merge_tags,
    now,
    options_with_current,
)


def render() -> None:
    st.title("내 상태")
    st.caption("로드맵 초안과 대시보드 빈칸은 이 입력을 기준으로 만들어집니다.")
    show_flash()
    profile = get_profile()

    with st.form("profile_form"):
        name_col, grade_col = st.columns(2)
        with name_col:
            name = st.text_input("이름", value=profile["name"])
        with grade_col:
            grades = [""] + options_with_current(GRADES, [profile["grade"]] if profile["grade"] else [])
            grade = st.selectbox(
                "학년",
                grades,
                index=grades.index(profile["grade"]) if profile["grade"] in grades else 0,
                format_func=lambda value: "선택 안 함" if value == "" else value,
            )

        path = st.radio(
            "선호하는 방향",
            list(PATHS),
            index=list(PATHS).index(profile["path_preference"])
            if profile["path_preference"] in PATHS
            else 2,
            format_func=lambda key: PATHS[key],
            horizontal=True,
        )
        track_keys = list(TRACKS)
        track = st.radio(
            "계열",
            track_keys,
            index=track_keys.index(profile["track"]) if profile["track"] in TRACKS else track_keys.index("undecided"),
            format_func=lambda key: TRACKS[key],
            horizontal=True,
        )

        interest_options = options_with_current(INTEREST_PRESETS, profile["interests"])
        interests = st.multiselect(
            "관심 분야",
            interest_options,
            default=[item for item in profile["interests"] if item in interest_options],
        )
        extra_interests = st.text_input("목록에 없는 분야", placeholder="쉼표로 구분")

        research_topic = st.text_input("연구 주제", value=profile["research_topic"])
        research_status = st.text_area(
            "지금 어디까지 왔는지",
            value=profile["research_status"],
            height=120,
            placeholder="읽은 논문, 해본 실험, 막힌 점",
        )

        st.markdown("**내신**")
        n1, n2 = st.columns(2)
        with n1:
            naesin_current = st.text_input("내신 현재", value=profile["naesin_current"])
        with n2:
            naesin_target = st.text_input("내신 목표", value=profile["naesin_target"])

        t1, t2, t3, t4 = st.columns(4)
        with t1:
            toefl_current = st.number_input(
                "TOEFL 현재", min_value=0, max_value=120, step=1, value=int(profile["toefl_current"])
            )
        with t2:
            toefl_target = st.number_input(
                "TOEFL 목표", min_value=0, max_value=120, step=1, value=int(profile["toefl_target"])
            )
        with t3:
            sat_current = st.number_input(
                "SAT 현재", min_value=0, max_value=1600, step=10, value=int(profile["sat_current"])
            )
        with t4:
            sat_target = st.number_input(
                "SAT 목표", min_value=0, max_value=1600, step=10, value=int(profile["sat_target"])
            )
        st.caption("0은 아직 없음입니다. SAT는 미국 지원에서 필요할 때만 채웁니다.")

        notes = st.text_area("메모", value=profile["notes"], height=90)
        submitted = st.form_submit_button("저장", type="primary")

    if submitted:
        save_profile(
            {
                "name": name,
                "grade": grade,
                "interests": merge_tags(interests, extra_interests),
                "research_topic": research_topic,
                "research_status": research_status,
                "naesin_current": naesin_current,
                "naesin_target": naesin_target,
                "csat_target": "",
                "toefl_current": toefl_current,
                "toefl_target": toefl_target,
                "sat_current": sat_current,
                "sat_target": sat_target,
                "path_preference": path,
                "track": track,
                "notes": notes,
                "updated_at": now(),
            }
        )
        flash("저장했습니다.")
        st.rerun()

    if profile.get("updated_at"):
        st.caption(f"마지막 저장 {profile['updated_at']}")
