"""안개 그림 위에 얹는 화면 스타일."""

from __future__ import annotations

import streamlit as st

CSS = """
@import url('https://fonts.googleapis.com/css2?family=Fraunces:opsz,wght@9..144,560;9..144,680&family=IBM+Plex+Sans+KR:wght@400;500;600&display=swap');

html, body, [class*="css"] {
  font-family: "IBM Plex Sans KR", "Segoe UI", sans-serif;
}

.stApp {
  background-color: #c5ced3 !important;
  background-image: none !important;
  color: #1c2428;
}
.stApp::before {
  content: "";
  position: fixed;
  inset: 0;
  z-index: 0;
  pointer-events: none;
  background-image: url("/app/static/wanderer.jpg");
  background-repeat: no-repeat;
  background-position: center center;
  background-size: cover;
}
[data-testid="stAppViewContainer"],
[data-testid="stMain"],
section[data-testid="stSidebar"] {
  position: relative;
  z-index: 1;
  background-color: transparent !important;
}

header[data-testid="stHeader"] {
  background: transparent !important;
}
[data-testid="stToolbar"],
[data-testid="stDecoration"],
.stAppDeployButton {
  display: none;
}

section[data-testid="stSidebar"],
section[data-testid="stSidebar"] > div,
[data-testid="stSidebarContent"] {
  background: rgba(226, 232, 235, 0.42) !important;
  backdrop-filter: blur(16px) saturate(1.05);
}
section[data-testid="stSidebar"] {
  border-right: 1px solid rgba(255, 255, 255, 0.38);
}

.block-container {
  max-width: 720px !important;
  margin-left: 0.85rem !important;
  margin-right: auto !important;
  padding: 1.55rem 1.55rem 2.4rem !important;
  background: linear-gradient(180deg, rgba(255, 255, 255, 0.5), rgba(244, 247, 249, 0.34));
  backdrop-filter: blur(3px);
  border: 1px solid rgba(255, 255, 255, 0.42);
  border-radius: 26px;
  box-shadow: none;
}

h1, h2, h3, .hero-title {
  font-family: "Fraunces", Georgia, serif !important;
  letter-spacing: -0.03em;
  color: #1a2428;
}
.kicker {
  color: #6d5840;
  font-size: 0.76rem;
  font-weight: 600;
  letter-spacing: 0.16em;
  text-transform: uppercase;
  margin: 0 0 0.25rem;
}
.hero-title {
  font-size: 2.65rem;
  line-height: 1.12;
  margin: 0 0 0.45rem;
  font-weight: 560;
}
.lede {
  font-size: 1.05rem;
  color: #2c3840;
  margin: 0 0 1rem;
  max-width: 40rem;
}
.soft-card {
  background: rgba(255, 255, 255, 0.62);
  border: 1px solid rgba(255, 255, 255, 0.62);
  border-radius: 18px;
  padding: 16px 18px 14px;
  min-height: 100%;
  box-shadow: inset 0 1px 0 rgba(255, 255, 255, 0.65);
}
.soft-card p { margin: 0.15rem 0; color: #243038; }
.metric {
  font-family: "Fraunces", Georgia, serif;
  font-size: 1.7rem;
  line-height: 1.1;
  margin: 0.15rem 0 0.25rem;
  color: #172228;
}
.muted { color: #4d5a62; font-size: 0.92rem; }
.pill {
  display: inline-block;
  background: rgba(255, 255, 255, 0.55);
  color: #243038;
  border: 1px solid rgba(255, 255, 255, 0.7);
  border-radius: 999px;
  padding: 3px 10px;
  margin: 0 6px 6px 0;
  font-size: 0.86rem;
}
.gap-list { margin: 0.2rem 0 0; padding-left: 1.1rem; }

[data-testid="stForm"],
[data-testid="stExpander"] details,
div[data-testid="stVerticalBlockBorderWrapper"] {
  background: rgba(255, 255, 255, 0.38) !important;
  border: 1px solid rgba(255, 255, 255, 0.5) !important;
  border-radius: 16px !important;
  backdrop-filter: blur(8px);
}
[data-testid="stDataFrame"],
[data-testid="stAlert"] {
  background: rgba(255, 255, 255, 0.5);
  border-radius: 12px;
}
.stTabs [data-baseweb="tab-list"] {
  background: rgba(255, 255, 255, 0.35);
  border-radius: 999px;
  padding: 0.2rem;
  gap: 0.2rem;
}
.stTextInput input,
.stTextArea textarea,
.stNumberInput input,
.stDateInput input {
  background: rgba(255, 255, 255, 0.72) !important;
  border-color: rgba(28, 40, 48, 0.14) !important;
  color: #1c2428 !important;
}
.stButton > button {
  background: rgba(255, 255, 255, 0.5);
  border: 1px solid rgba(28, 40, 48, 0.16);
  color: #1c2428;
}
.stButton > button[kind="primary"],
button[data-testid="stBaseButton-primary"] {
  background: #243038 !important;
  color: #f3f6f7 !important;
  border: none !important;
}
[data-testid="stCaption"] { color: #3d4a52; }
"""


def inject_css() -> None:
    st.markdown(f"<style>{CSS}</style>", unsafe_allow_html=True)
