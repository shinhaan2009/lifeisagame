"""화면 사이에서 쓰는 짧은 상태 도우미."""

from __future__ import annotations

import streamlit as st


def consume_pending(widget_key: str) -> None:
    pending = f"_pending_{widget_key}"
    if pending in st.session_state:
        st.session_state[widget_key] = st.session_state.pop(pending)


def queue_pick(widget_key: str, value) -> None:
    st.session_state[f"_pending_{widget_key}"] = value


def flash(message: str) -> None:
    st.session_state["_flash"] = message


def show_flash() -> None:
    message = st.session_state.pop("_flash", None)
    if message:
        st.success(message)
