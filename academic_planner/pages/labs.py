"""랩과 필요한 경험."""

from __future__ import annotations

import streamlit as st

from academic_planner.db import delete_lab, get_lab, list_labs, save_lab, set_lab_checklist
from academic_planner.ui import consume_pending, flash, queue_pick, show_flash
from academic_planner.util import INTEREST_PRESETS, LAB_CHECKLIST_TEMPLATE, merge_tags, options_with_current

PICK_KEY = "lab_pick"


def _blank() -> dict:
    return {
        "id": None,
        "name": "",
        "institution": "",
        "pi": "",
        "topics": [],
        "why_match": "",
        "notes": "",
        "checklist": [],
    }


def render() -> None:
    st.title("랩 매칭")
    st.caption("연구 주제와 가까운 랩을 모아 두고, 그 랩에 가려면 더 필요한 경험을 체크합니다.")
    show_flash()
    labs = list_labs()
    consume_pending(PICK_KEY)
    ids = [None, *[lab["id"] for lab in labs]]
    if PICK_KEY not in st.session_state or st.session_state[PICK_KEY] not in ids:
        st.session_state[PICK_KEY] = ids[1] if len(ids) > 1 else None
    by_id = {lab["id"]: lab for lab in labs}

    def _label(lab_id):
        if lab_id is None:
            return "＋ 새 랩"
        lab = by_id[lab_id]
        return lab["name"]

    topic_pool = sorted({topic for lab in labs for topic in lab["topics"]})
    if topic_pool:
        chosen = st.multiselect("태그로 좁히기", topic_pool)
        if chosen:
            visible_ids = [
                None,
                *[
                    lab["id"]
                    for lab in labs
                    if any(topic in chosen for topic in lab["topics"]) or st.session_state.get(PICK_KEY) == lab["id"]
                ],
            ]
        else:
            visible_ids = ids
    else:
        visible_ids = ids

    if st.session_state.get(PICK_KEY) not in visible_ids:
        st.session_state[PICK_KEY] = visible_ids[0]

    lab_id = st.selectbox("랩", visible_ids, format_func=_label, key=PICK_KEY)
    record = _blank() if lab_id is None else get_lab(lab_id) or _blank()

    if lab_id is not None:
        _render_checklist(record)

    st.subheader("랩 정보" if lab_id else "새 랩")
    _render_form(record)


def _render_checklist(lab: dict) -> None:
    checklist = list(lab["checklist"])
    done_count = sum(1 for item in checklist if item["done"])
    st.subheader("이 랩에 가려면")
    if checklist:
        st.progress(done_count / len(checklist), text=f"경험 {done_count}/{len(checklist)}")
    missing = [item["text"] for item in checklist if not item["done"]]
    if missing:
        st.markdown("**더 필요한 경험**")
        for text in missing:
            st.markdown(f"- {text}")
    elif checklist:
        st.success("적어 둔 경험은 모두 체크했습니다.")
    else:
        st.caption("체크리스트가 비어 있습니다. 템플릿을 넣거나 항목을 추가하세요.")

    updated = []
    changed = False
    for index, item in enumerate(checklist):
        left, right = st.columns([10, 1])
        with left:
            done = st.checkbox(item["text"], value=item["done"], key=f"labchk_{lab['id']}_{index}_{item['text']}")
        with right:
            remove = st.button("삭제", key=f"labdel_{lab['id']}_{index}")
        if remove:
            changed = True
            continue
        if done != item["done"]:
            changed = True
        updated.append({"text": item["text"], "done": done})

    if changed:
        set_lab_checklist(lab["id"], updated)
        st.rerun()

    add_col, template_col = st.columns([3, 1])
    with add_col:
        new_item = st.text_input("경험 추가", key=f"new_chk_{lab['id']}", placeholder="예: 세포 배양 경험을 한 단락으로 정리한다")
    with template_col:
        st.write("")
        use_template = st.button("템플릿 넣기", key=f"tpl_{lab['id']}")
    if st.button("항목 추가", key=f"add_chk_{lab['id']}") and new_item.strip():
        texts = {item["text"] for item in checklist}
        if new_item.strip() not in texts:
            checklist.append({"text": new_item.strip(), "done": False})
            set_lab_checklist(lab["id"], checklist)
        st.rerun()
    if use_template:
        texts = {item["text"] for item in checklist}
        for text in LAB_CHECKLIST_TEMPLATE:
            if text not in texts:
                checklist.append({"text": text, "done": False})
        set_lab_checklist(lab["id"], checklist)
        st.rerun()


def _render_form(record: dict) -> None:
    lab_id = record["id"]
    with st.form(f"lab_form_{lab_id}"):
        name = st.text_input("랩 이름", value=record["name"])
        c1, c2 = st.columns(2)
        with c1:
            institution = st.text_input("학교·기관", value=record["institution"])
        with c2:
            pi = st.text_input("교수", value=record["pi"])
        options = options_with_current(INTEREST_PRESETS, record["topics"])
        topics = st.multiselect(
            "주제 태그",
            options,
            default=[topic for topic in record["topics"] if topic in options],
        )
        extra = st.text_input("태그 추가", placeholder="쉼표로 구분")
        why_match = st.text_area("내 연구와 겹치는 이유", value=record["why_match"], height=90)
        notes = st.text_area("메모", value=record["notes"], height=90)
        submitted = st.form_submit_button("랩 정보 저장", type="primary")

    if submitted:
        if not name.strip():
            st.error("랩 이름을 입력하세요.")
            return
        current = get_lab(lab_id) if lab_id else None
        new_id = save_lab(
            {
                "id": lab_id,
                "name": name,
                "institution": institution,
                "pi": pi,
                "topics": merge_tags(topics, extra),
                "why_match": why_match,
                "notes": notes,
                "checklist": current["checklist"] if current else record["checklist"],
            }
        )
        queue_pick(PICK_KEY, new_id)
        flash("랩을 저장했습니다.")
        st.rerun()

    if lab_id is not None and st.button("이 랩 삭제"):
        delete_lab(lab_id)
        queue_pick(PICK_KEY, None)
        flash("랩을 삭제했습니다.")
        st.rerun()
