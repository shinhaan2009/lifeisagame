"""가중치를 둔 진로 비교."""

from __future__ import annotations

import altair as alt
import pandas as pd
import streamlit as st

from academic_planner.db import (
    delete_decision,
    delete_option,
    get_decision,
    list_decisions,
    save_decision,
    save_option,
)
from academic_planner.scoring import weighted_score
from academic_planner.ui import consume_pending, flash, queue_pick, show_flash
from academic_planner.util import DEFAULT_CRITERIA

PICK_KEY = "decision_pick"


def render() -> None:
    st.title("의사결정")
    st.caption("모든 점수는 5점에 가까울수록 유리합니다. 저장한 값으로만 순위를 계산합니다.")
    show_flash()
    decisions = list_decisions()
    consume_pending(PICK_KEY)
    ids = [None, *[item["id"] for item in decisions]]
    if PICK_KEY not in st.session_state or st.session_state[PICK_KEY] not in ids:
        st.session_state[PICK_KEY] = ids[1] if len(ids) > 1 else None
    by_id = {item["id"]: item for item in decisions}

    def _label(decision_id):
        if decision_id is None:
            return "＋ 새 비교"
        return by_id[decision_id]["title"]

    decision_id = st.selectbox("비교", ids, format_func=_label, key=PICK_KEY)
    if decision_id is None:
        _create_form()
        return

    decision = get_decision(decision_id)
    if decision is None:
        st.session_state[PICK_KEY] = None
        st.rerun()
    _render_ranking(decision)
    _render_meta(decision)
    _render_criteria(decision)
    _render_options(decision)
    if st.button("이 비교 삭제"):
        delete_decision(decision_id)
        queue_pick(PICK_KEY, None)
        flash("비교를 삭제했습니다.")
        st.rerun()


def _create_form() -> None:
    with st.form("new_decision"):
        title = st.text_input("비교 제목", placeholder="예: A대학 공대 vs B대학 의대 vs 미국 C대학")
        use_defaults = st.checkbox("기본 기준 6개를 넣는다", value=True)
        submitted = st.form_submit_button("만들기", type="primary")
    if submitted:
        if not title.strip():
            st.error("제목을 입력하세요.")
            return
        criteria = [{"name": name, "weight": weight} for name, weight in DEFAULT_CRITERIA] if use_defaults else []
        new_id = save_decision({"title": title, "notes": "", "criteria": criteria})
        queue_pick(PICK_KEY, new_id)
        flash("비교를 만들었습니다. 옵션을 추가하세요.")
        st.rerun()


def _render_ranking(decision: dict) -> None:
    rows = []
    rest = []
    for option in decision["options"]:
        result = weighted_score(decision["criteria"], option["scores"])
        if result["score"] is None:
            rest.append(option["name"])
            continue
        rows.append(
            {
                "옵션": option["name"],
                "점수": result["score"],
                "반영": f"{result['filled']}/{result['total']}",
            }
        )
    if not rows:
        st.info("옵션을 만들고 점수를 저장하면 순위가 나타납니다.")
    else:
        frame = pd.DataFrame(rows).sort_values("점수", ascending=False)
        chart = (
            alt.Chart(frame)
            .mark_bar(color="#243038", cornerRadiusEnd=3)
            .encode(
                x=alt.X("점수:Q", scale=alt.Scale(domain=[0, 5.8]), title="가중 점수"),
                y=alt.Y("옵션:N", sort="-x", title=None),
                tooltip=["옵션", "점수", "반영"],
            )
        )
        labels = chart.mark_text(align="left", dx=4, color="#1a2428").encode(
            text=alt.Text("점수:Q", format=".2f")
        )
        layered = (
            (chart + labels)
            .configure(background="transparent")
            .configure_view(stroke=None)
            .configure_axis(
                labelColor="#243038",
                titleColor="#4d5a62",
                gridColor="rgba(36,48,56,0.12)",
                domainColor="rgba(36,48,56,0.28)",
            )
        )
        st.altair_chart(layered, width="stretch")
        leader = frame.iloc[0]
        st.markdown(f"가장 높은 옵션은 **{leader['옵션']}** ({leader['점수']:.2f}점)입니다.")
    if rest:
        st.caption("점수가 없는 옵션: " + ", ".join(rest))

    if decision["options"]:
        st.subheader("정리")
        ordered = sorted(
            decision["options"],
            key=lambda option: weighted_score(decision["criteria"], option["scores"])["score"] or -1,
            reverse=True,
        )
        for option in ordered:
            result = weighted_score(decision["criteria"], option["scores"])
            score_text = f"{result['score']:.2f} / 5" if result["score"] is not None else "점수 없음"
            with st.container(border=True):
                st.markdown(f"**{option['name']}** · {score_text}")
                a, b = st.columns(2)
                with a:
                    st.markdown("장점")
                    st.write(option["pros"] or "—")
                    st.markdown("필요 조건")
                    st.write(option["requirements"] or "—")
                with b:
                    st.markdown("단점")
                    st.write(option["cons"] or "—")
                    st.markdown("리스크")
                    st.write(option["risks"] or "—")


def _render_meta(decision: dict) -> None:
    with st.expander("제목과 메모"):
        with st.form(f"decision_meta_{decision['id']}"):
            title = st.text_input("제목", value=decision["title"])
            notes = st.text_area("메모", value=decision["notes"], height=80)
            submitted = st.form_submit_button("저장")
        if submitted and title.strip():
            save_decision(
                {
                    "id": decision["id"],
                    "title": title,
                    "notes": notes,
                    "criteria": decision["criteria"],
                }
            )
            flash("비교 정보를 저장했습니다.")
            st.rerun()


def _render_criteria(decision: dict) -> None:
    st.subheader("기준과 가중치")
    st.caption("중요도는 1에서 5입니다. 이름을 바꾼 뒤에는 옵션 점수를 다시 저장하세요.")
    criteria = list(decision["criteria"])
    with st.form(f"criteria_{decision['id']}"):
        edited = []
        for index, criterion in enumerate(criteria):
            name_col, weight_col = st.columns([3, 1])
            with name_col:
                name = st.text_input(
                    "기준",
                    value=criterion.get("name", ""),
                    key=f"crit_name_{decision['id']}_{index}",
                    label_visibility="collapsed",
                    placeholder="기준 이름",
                )
            with weight_col:
                weight = st.slider(
                    "중요도",
                    1,
                    5,
                    int(criterion.get("weight") or 3),
                    key=f"crit_weight_{decision['id']}_{index}",
                )
            edited.append({"name": name.strip(), "weight": int(weight)})
        submitted = st.form_submit_button("기준 저장")
    if submitted:
        names = [item["name"] for item in edited if item["name"]]
        if len(names) != len(set(names)):
            st.error("기준 이름이 겹칩니다.")
        else:
            save_decision(
                {
                    "id": decision["id"],
                    "title": decision["title"],
                    "notes": decision["notes"],
                    "criteria": [item for item in edited if item["name"]],
                }
            )
            flash("기준을 저장했습니다.")
            st.rerun()

    add_col, default_col = st.columns(2)
    with add_col:
        if st.button("기준 추가"):
            existing = {item.get("name") for item in criteria}
            name = "새 기준"
            number = 2
            while name in existing:
                name = f"새 기준 {number}"
                number += 1
            criteria.append({"name": name, "weight": 3})
            save_decision({**decision, "criteria": criteria})
            st.rerun()
    with default_col:
        if st.button("비어 있는 기본 기준 채우기"):
            existing = {item.get("name") for item in criteria}
            for name, weight in DEFAULT_CRITERIA:
                if name not in existing:
                    criteria.append({"name": name, "weight": weight})
            save_decision({**decision, "criteria": criteria})
            st.rerun()
    if criteria and st.button("마지막 기준 삭제"):
        criteria.pop()
        save_decision({**decision, "criteria": criteria})
        st.rerun()


def _render_options(decision: dict) -> None:
    st.subheader("옵션")
    with st.form(f"new_option_{decision['id']}"):
        name = st.text_input("새 옵션", placeholder="예: KAIST 바이오및뇌공학")
        submitted = st.form_submit_button("옵션 추가")
    if submitted:
        if not name.strip():
            st.error("옵션 이름을 입력하세요.")
        else:
            save_option(
                {
                    "decision_id": decision["id"],
                    "name": name,
                    "pros": "",
                    "cons": "",
                    "requirements": "",
                    "risks": "",
                    "scores": {},
                }
            )
            flash("옵션을 추가했습니다.")
            st.rerun()

    criteria = [item for item in decision["criteria"] if str(item.get("name", "")).strip()]
    for option in decision["options"]:
        with st.expander(option["name"], expanded=False):
            with st.form(f"option_{option['id']}"):
                name = st.text_input("이름", value=option["name"])
                a, b = st.columns(2)
                with a:
                    pros = st.text_area("장점", value=option["pros"], height=80)
                    requirements = st.text_area("필요 조건", value=option["requirements"], height=80)
                with b:
                    cons = st.text_area("단점", value=option["cons"], height=80)
                    risks = st.text_area("리스크", value=option["risks"], height=80)
                scores = {}
                if criteria:
                    st.markdown("**점수**")
                    for criterion in criteria:
                        label = criterion["name"]
                        current = option["scores"].get(label, 3)
                        try:
                            current = int(current)
                        except (TypeError, ValueError):
                            current = 3
                        current = min(5, max(1, current))
                        scores[label] = st.slider(
                            f"{label} · 중요도 {int(criterion.get('weight') or 1)}",
                            1,
                            5,
                            current,
                            key=f"score_{option['id']}_{label}",
                        )
                save = st.form_submit_button("이 옵션 저장")
            if save:
                if not name.strip():
                    st.error("옵션 이름을 입력하세요.")
                else:
                    save_option(
                        {
                            "id": option["id"],
                            "decision_id": decision["id"],
                            "name": name,
                            "pros": pros,
                            "cons": cons,
                            "requirements": requirements,
                            "risks": risks,
                            "scores": scores,
                        }
                    )
                    flash("옵션을 저장했습니다.")
                    st.rerun()
            if st.button("옵션 삭제", key=f"del_opt_{option['id']}"):
                delete_option(option["id"])
                flash("옵션을 삭제했습니다.")
                st.rerun()
