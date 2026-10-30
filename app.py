"""Academic Path Planner 실행 진입점."""

import streamlit as st

from academic_planner.db import init_db
from academic_planner.pages.dashboard import render as render_dashboard
from academic_planner.pages.decisions import render as render_decisions
from academic_planner.pages.labs import render as render_labs
from academic_planner.pages.profile import render as render_profile
from academic_planner.pages.schools import render as render_schools
from academic_planner.pages.timeline import render as render_timeline
from academic_planner.theme import inject_css

st.set_page_config(
    page_title="Academic Path Planner",
    page_icon="◈",
    layout="wide",
    initial_sidebar_state="expanded",
)

init_db()
inject_css()

ROUTES = {
    "대시보드": render_dashboard,
    "내 상태": render_profile,
    "대학 비교": render_schools,
    "랩 매칭": render_labs,
    "로드맵": render_timeline,
    "의사결정": render_decisions,
}

st.sidebar.markdown('<p class="kicker">Academic Path</p>', unsafe_allow_html=True)
st.sidebar.markdown("## Planner")
st.sidebar.caption("지금 상태와 목표를 한곳에 두고, 로드맵과 비교를 만든다.")
page = st.sidebar.radio("메뉴", list(ROUTES), label_visibility="collapsed")
st.sidebar.divider()
st.sidebar.caption("이 컴퓨터에만 저장됩니다.")
st.sidebar.caption("data/planner.db")

ROUTES[page]()
