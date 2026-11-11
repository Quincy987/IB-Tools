"""Probe a running demo Revit: answers start-up dialogs, then dumps the UIA elements that matter
for drive.py (ribbon tab, our buttons, chat pane) with control type / automation id / rect / parents.
Usage: python probe.py <take_dir> <revit_pid>"""
import os, re, sys, time
sys.argv = [sys.argv[0], sys.argv[1], sys.argv[2]]
import drive  # noqa: E402  (uses argv: take dir, pid)
from drive import UIA, main_window, answer_startup_dialogs, log  # noqa: E402
import win32gui  # noqa: E402

main_window()
if not answer_startup_dialogs(600):
    sys.exit("model did not open")
hwnd = main_window(60)
main = UIA.window(handle=hwnd)
rx = re.compile(r"^(IB Tools|Worksets|Modelpunt|Paalco.rdinaten|VibeModel Chat|Co.rdinaten|Model)$")
t0 = time.time()
for el in main.descendants():
    try:
        name = (el.window_text() or "").strip()
    except Exception:  # noqa: BLE001
        continue
    if not rx.match(name):
        continue
    chain = []
    p = el
    for _ in range(3):
        try:
            p = p.parent()
            chain.append("%s[%s]" % (p.element_info.control_type, (p.window_text() or "")[:30]))
        except Exception:  # noqa: BLE001
            break
    log("UIA %-22s type=%-10s id=%-28s rect=%s  < %s" % (name, el.element_info.control_type,
        el.element_info.automation_id, el.rectangle(), " < ".join(chain)))
log("probe done in %.1fs" % (time.time() - t0))
