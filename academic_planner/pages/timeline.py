"""타임라인과 할 일."""

from __future__ import annotations

import html
from collections import defaultdict
from datetime import date

import streamlit as st

from academic_planner.db import add_tasks_if_new, delete_task, get_profile, list_tasks, save_task, set_task_status
from academic_planner.roadmap import suggest_tasks
from academic_planner.ui import flash, show_flash
from academic_planner.util import TASK_CATEGORIES, TASK_STATUS, fmt_date, options_with_current


def render() -> None:
    st.title("로드맵")
    st.caption("유학이면 TOEFL·에세이·추천서, 국내면 내신과 학생부 전형이 초안에 들어갑니다.")
    show_flash()
    profile = get_profile()

    action_col, note_col = st.columns([1, 2])
    with action_col:
        if st.button("내 상태로 할 일 초안 추가", type="primary"):
            added = add_tasks_if_new(suggest_tasks(profile))
            flash("새 할 일을 추가했습니다." if added else "같은 제목의 초안이 이미 있습니다.")
            st.rerun()
    with note_col:
        direction = {"domestic": "국내 진학", "abroad": "미국 유학", "both": "국내와 유학"}.get(
            profile["path_preference"], "방향 미정"
        )
        st.caption(f"지금 방향: {direction}. 같은 제목은 다시 넣지 않습니다.")

    with st.expander("할 일 직접 추가"):
        _add_form()

    tasks = list_tasks()
    filter_col, toggle_col = st.columns([2, 1])
    with filter_col:
        categories = st.multiselect("분류", TASK_CATEGORIES, placeholder="전체")
    with toggle_col:
        hide_done = st.toggle("완료 숨기기")

    visible = [
        task
        for task in tasks
        if (not categories or task["category"] in categories)
        and not (hide_done and task["status"] == "done")
    ]
    if not visible:
        st.info("표시할 할 일이 없습니다.")
        return

    groups: dict[str, list[dict]] = defaultdict(list)
    for task in visible:
        key = task["due_date"][:7] if task["due_date"] else "날짜 미정"
        groups[key].append(task)

    for month in sorted(groups, key=lambda key: (key == "날짜 미정", key)):
        label = "날짜 미정" if month == "날짜 미정" else month.replace("-", ".")
        st.subheader(label)
        for task in groups[month]:
            _render_task(task)


def _add_form() -> None:
    with st.form("new_task"):
        title = st.text_input("할 일")
        c1, c2, c3 = st.columns(3)
        with c1:
            category = st.selectbox("분류", TASK_CATEGORIES, index=TASK_CATEGORIES.index("기타"))
        with c2:
            has_date = st.checkbox("마감일을 저장", value=True)
        with c3:
            status = st.selectbox("상태", list(TASK_STATUS), format_func=lambda key: TASK_STATUS[key])
        due = st.date_input("날짜", value=date.today())
        detail = st.text_area("상세", height=70)
        submitted = st.form_submit_button("추가")
    if submitted:
        if not title.strip():
            st.error("할 일을 입력하세요.")
        else:
            save_task(
                {
                    "title": title,
                    "category": category,
                    "due_date": due.isoformat() if has_date else "",
                    "status": status,
                    "detail": detail,
                }
            )
            flash("할 일을 추가했습니다.")
            st.rerun()


def _render_task(task: dict) -> None:
    head, status_col = st.columns([4, 1.3])
    with head:
        style = "text-decoration:line-through;color:#6f675d;" if task["status"] == "done" else ""
        st.markdown(
            f'<p style="margin:0;{style}"><strong>{html.escape(task["title"])}</strong></p>'
            f'<p class="muted" style="margin:0;">{html.escape(task["category"])} · {fmt_date(task["due_date"])}</p>',
            unsafe_allow_html=True,
        )
    with status_col:
        status_keys = list(TASK_STATUS)
        status = st.selectbox(
            "상태",
            status_keys,
            index=status_keys.index(task["status"]) if task["status"] in TASK_STATUS else 0,
            format_func=lambda key: TASK_STATUS[key],
            key=f"task_status_{task['id']}",
            label_visibility="collapsed",
        )
    if status != task["status"]:
        set_task_status(task["id"], status)
        st.rerun()

    with st.expander("수정", expanded=False):
        with st.form(f"edit_task_{task['id']}"):
            title = st.text_input("할 일", value=task["title"])
            categories = options_with_current(TASK_CATEGORIES, [task["category"]])
            category = st.selectbox(
                "분류",
                categories,
                index=categories.index(task["category"]) if task["category"] in categories else 0,
            )
            has_date = st.checkbox("마감일을 저장", value=bool(task["due_date"]))
            initial = date.today()
            if task["due_date"]:
                try:
                    initial = date.fromisoformat(task["due_date"])
                except ValueError:
                    initial = date.today()
            due = st.date_input("날짜", value=initial)
            detail = st.text_area("상세", value=task["detail"], height=70)
            save = st.form_submit_button("저장")
        if save:
            if not title.strip():
                st.error("할 일을 입력하세요.")
            else:
                save_task(
                    {
                        "id": task["id"],
                        "title": title,
                        "category": category,
                        "due_date": due.isoformat() if has_date else "",
                        "status": status,
                        "detail": detail,
                    }
                )
                flash("할 일을 고쳤습니다.")
                st.rerun()
        if st.button("삭제", key=f"delete_task_{task['id']}"):
            delete_task(task["id"])
            flash("할 일을 삭제했습니다.")
            st.rerun()
