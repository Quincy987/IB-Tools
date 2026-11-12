"""Drives the IB Tools demo in a running Revit 2023 (started by build.ps1 -OpenFile <demo model>):
answers the start-up dialogs, puts Revit full screen, starts rec.py, then presses the three IB Tools
ribbon buttons with a real (smoothly moving) mouse and answers their dialogs, while writing a
timeline (cards + captions) for cut.py.  VibeModel (same Revit) is used to read/select elements.

Usage: python drive.py <take_dir> <revit_pid>
Writes <take_dir>/frames/*.jpg, <take_dir>/timeline.json, <take_dir>/drive.log."""
import ctypes, glob, json, os, re, subprocess, sys, time, traceback, urllib.parse, urllib.request
ctypes.windll.shcore.SetProcessDpiAwareness(2)
import win32api, win32clipboard, win32con, win32gui, win32process  # noqa: E402
from pywinauto import Desktop, mouse  # noqa: E402

sys.stdout.reconfigure(errors="replace")          # console may be cp1252; never crash on a dialog text
HERE = os.path.dirname(os.path.abspath(__file__))
TAKE, PID = sys.argv[1], int(sys.argv[2])
FRAMES = os.path.join(TAKE, "frames")
os.makedirs(FRAMES, exist_ok=True)
W, H = 1920, 1200
LOG = open(os.path.join(TAKE, "drive.log"), "a", encoding="utf-8")
TIMELINE = []
UIA = Desktop(backend="uia")
REC = None

CLIP_COORD = "121124,907 486164,311 1,300"      # 2nd corner of the quay (see docs/verify), decimal commas
BUTTONS = {   # label on the ribbon -> PostCommand id (fallback when UIA cannot find the button)
    "Worksets": "CustomCtrl_%CustomCtrl_%IB Tools%Model%IbWorksets",
    "Modelpunt": "CustomCtrl_%CustomCtrl_%IB Tools%Coördinaten%CoordinatePoint",
    "Paalcoördinaten": "CustomCtrl_%CustomCtrl_%IB Tools%Coördinaten%PileCoordinates",
}


def log(s):
    line = time.strftime("%H:%M:%S ") + s
    print(line, flush=True)
    LOG.write(line + "\n"); LOG.flush()


def mark(kind, text="", **extra):
    TIMELINE.append(dict(t=time.time(), kind=kind, text=text, **extra))
    log("[%s] %s" % (kind, text))


# ---------------------------------------------------------------- VibeModel (HTTP in the same Revit)
def vm_server():
    """(port, token) of the VibeModel server inside OUR Revit (servers/*.json carries the pid)."""
    for f in glob.glob(os.path.join(os.environ["LOCALAPPDATA"], "VibeModel", "servers", "*.json")):
        try:
            d = json.load(open(f))
            if int(d.get("pid", -1)) == PID:
                return d["port"], d.get("token")
        except Exception:  # noqa: BLE001
            pass
    return None, None


def vm(cmd, args="", timeout=180):
    port, token = vm_server()
    if not port:
        raise RuntimeError("VibeModel server for pid %d not found" % PID)
    url = "http://127.0.0.1:%d/%s" % (port, cmd)
    if args:
        url += "?args=" + urllib.parse.quote(args)
    req = urllib.request.Request(url, headers={"X-VibeModel-Token": token} if token else {})
    return urllib.request.urlopen(req, timeout=timeout).read().decode("utf-8", "replace")


def vm_exec(code, timeout=180):
    """Runs C# in Revit via VibeModel; on any failure logs and returns "" (the show goes on)."""
    try:
        out = vm("exec", code, timeout)
    except Exception as e:  # noqa: BLE001
        log("exec failed: %s" % e)
        return ""
    if "ERROR" in out[:200]:
        log("exec -> " + out[:400])
    return out


# ---------------------------------------------------------------- windows / dialogs
def revit_windows():
    """All top-level UIA windows of our Revit pid plus their direct child windows (WPF owned)."""
    found, hwnds = [], []

    def cb(h, _):
        if win32gui.IsWindowVisible(h) and win32gui.GetWindowText(h):
            _, pid = win32process.GetWindowThreadProcessId(h)
            if pid == PID:
                hwnds.append(h)
    win32gui.EnumWindows(cb, None)          # UIA.windows(process=) misses some add-in dialogs
    for h in hwnds:
        try:
            t = UIA.window(handle=h)
        except Exception:  # noqa: BLE001
            continue
        found.append(t)
        try:
            found += t.children(control_type="Window")
        except Exception:  # noqa: BLE001
            pass
    return found


def find_window(title_re=None, auto_id=None, timeout=60):
    rx = re.compile(title_re) if title_re else None
    deadline = time.time() + timeout
    while time.time() < deadline:
        for w in revit_windows():
            try:
                name = w.window_text()
            except Exception:  # noqa: BLE001
                continue
            if rx and not rx.search(name or ""):
                continue
            if auto_id:
                try:
                    if not by_id(w, auto_id):
                        continue
                except Exception:  # noqa: BLE001
                    continue
            return w
        time.sleep(0.5)
    return None


def by_id(el, auto_id):
    """Descendants with this automation id (pywinauto's descendants() cannot filter on it)."""
    return [d for d in el.descendants() if d.element_info.automation_id == auto_id]


def main_window(timeout=600):
    rx = re.compile(r"^Autodesk Revit 2023")
    deadline = time.time() + timeout
    while time.time() < deadline:
        hwnds = []

        def cb(h, _):
            if win32gui.IsWindowVisible(h) and rx.match(win32gui.GetWindowText(h) or ""):
                _, pid = __import__("win32process").GetWindowThreadProcessId(h)
                if pid == PID:
                    hwnds.append(h)
        win32gui.EnumWindows(cb, None)
        if hwnds:
            return hwnds[0]
        handle_startup_dialogs_once()          # "Load Once/Always" comes up before the main window
        time.sleep(1)
    raise RuntimeError("Revit main window not found")


def click_button_in(win, title_re, smooth=True):
    btns = [b for b in win.descendants(control_type="Button") if re.search(title_re, b.window_text() or "")]
    if not btns:
        btns = [b for b in win.descendants() if re.search(title_re, b.window_text() or "")
                and b.element_info.control_type in ("Button", "Hyperlink", "ListItem", "Custom")]
    if not btns:
        return False
    click_element(btns[0], smooth)
    return True


def handle_startup_dialogs_once():
    """Load Once/Always (shown before the main window exists), unresolved references."""
    for w in revit_windows():
        try:
            name = w.window_text() or ""
        except Exception:  # noqa: BLE001
            continue
        if "Unsigned" in name or "Security" in name or "niet-ondertekend" in name:
            if click_button_in(w, r"Always|Altijd", smooth=False):
                log("Startup dialog: " + name + " -> Always Load")
                time.sleep(1)
        elif "Unresolved" in name or "References" in name:
            if click_button_in(w, r"Ignore|Negeer", smooth=False):
                log("Startup dialog: " + name + " -> Ignore")
                time.sleep(1)
        elif name.endswith("Error") or name.endswith("Fout"):      # other add-ins' start-up errors
            if click_button_in(w, r"^(Sluiten|Close|OK)$", smooth=False):
                log("Startup dialog: " + name + " -> closed")
                time.sleep(1)


def answer_startup_dialogs(seconds):
    """Keeps answering start-up dialogs until the demo model is open and Revit is idle."""
    deadline = time.time() + seconds
    seen = 0
    while time.time() < deadline:
        handle_startup_dialogs_once()
        # ready = Revit's title bar names the demo model (no VibeModel needed), seen twice in a row
        titles = [win32gui.GetWindowText(h) for h in top_hwnds()]
        if any("IB Tools demo" in t for t in titles):
            seen += 1
            if seen >= 2:
                log("Model open: " + next(t for t in titles if "IB Tools demo" in t))
                vm_exec('sb.Append(doc.Title);', timeout=120)       # wait until Revit idles
                return True
        else:
            seen = 0
        time.sleep(2)
    return False


def top_hwnds():
    hwnds = []

    def cb(h, _):
        if win32gui.IsWindowVisible(h) and win32gui.GetWindowText(h):
            _, pid = win32process.GetWindowThreadProcessId(h)
            if pid == PID:
                hwnds.append(h)
    win32gui.EnumWindows(cb, None)
    return hwnds


def dismiss_revit_warnings():
    """Closes Revit's own warning pop-up (e.g. 'identical instances') if one is up."""
    for w in revit_windows():
        try:
            name = w.window_text() or ""
        except Exception:  # noqa: BLE001
            continue
        if name.startswith("Autodesk Revit 2023") and len(name) < 25:
            continue
        if name in ("Autodesk Revit 2023", "Revit") or "Warning" in name or "Waarschuwing" in name:
            if click_button_in(w, r"^(OK|Close|Sluiten)$", smooth=False):
                log("Revit warning dismissed: " + name)
                time.sleep(0.5)


# ---------------------------------------------------------------- mouse
def smooth_move(x, y, dur=0.7):
    x0, y0 = win32api.GetCursorPos()
    steps = max(8, int(dur * 60))
    for i in range(1, steps + 1):
        s = i / steps
        s = s * s * (3 - 2 * s)          # ease in/out
        win32api.SetCursorPos((int(x0 + (x - x0) * s), int(y0 + (y - y0) * s)))
        time.sleep(dur / steps)


def click_element(el, smooth=True, hover=0.35):
    r = el.rectangle()
    x, y = (r.left + r.right) // 2, (r.top + r.bottom) // 2
    if smooth:
        smooth_move(x, y)
        time.sleep(hover)
    else:
        win32api.SetCursorPos((x, y))
    mouse.click(coords=(x, y))
    time.sleep(0.3)


def park_mouse():
    smooth_move(W // 2, H - 60, 0.4)


# ---------------------------------------------------------------- ribbon
RIBBON_IDS = {   # label -> automation id of the ribbon Button (probe.py); plain "Worksets" also matches Revit's own
    "Hide": "CustomCtrl_%CustomCtrl_%IB Tools%Weergave%ToggleProjection",
    "Remmingwerk": "CustomCtrl_%CustomCtrl_%IB Tools%Kademuren%Remmingwerk",
    "Worksets": "CustomCtrl_%CustomCtrl_%IB Tools%Model%IbWorksets",
    "Modelpunt": "CustomCtrl_%CustomCtrl_%IB Tools%Coördinaten%CoordinatePoint",
    "Paalcoördinaten": "CustomCtrl_%CustomCtrl_%IB Tools%Coördinaten%PileCoordinates",
}


def find_ribbon_buttons(hwnd, attempts=3):
    """Click the IB Tools ribbon tab (a Button with automation id 'IB Tools') and return
    {label: button element} for the buttons on it, found by automation id inside the tab's panel bar.
    UIA on Revit's main window sometimes throws a transient COM error (event cannot invoke
    subscribers): retry with a fresh window object. Empty dict = press() falls back to PostCommand."""
    for attempt in range(1, attempts + 1):
        try:
            main = UIA.window(handle=hwnd)
            tabs = [t for t in main.descendants(control_type="Button") if t.element_info.automation_id == "IB Tools"]
            if tabs:
                click_element(tabs[0])
                time.sleep(1.2)
            else:
                log("Ribbon tab 'IB Tools' not found via UIA (attempt %d)" % attempt)
            found = {}
            bars = [e for e in main.descendants() if e.element_info.automation_id == "IB Tools_PanelBarScrollViewer"]
            scope = bars[0] if bars else main
            for b in scope.descendants(control_type="Button"):
                aid = b.element_info.automation_id or ""
                for label, want in RIBBON_IDS.items():
                    if aid == want and label not in found:
                        found[label] = b
            if found:
                log("Ribbon buttons found: " + ", ".join(found))
                if tabs:
                    found["_tab"] = tabs[0]   # press() re-activates the tab (a selection switches Revit to Modify)
                return found
        except Exception as e:  # noqa: BLE001
            log("UIA ribbon lookup failed (attempt %d): %s" % (attempt, str(e)[:160]))
        time.sleep(2.5)
    log("Ribbon buttons NOT found via UIA; buttons will be pressed via PostCommand")
    return {}


def press(label, buttons):
    if label in buttons:
        if "_tab" in buttons:
            click_element(buttons["_tab"], hover=0.3)  # back to IB Tools (Revit shows Modify after a selection)
            time.sleep(1.0)
        click_element(buttons[label], hover=0.9)      # the hover shows Revit's tooltip
    else:
        vm_exec('uiApp.PostCommand(RevitCommandId.LookupCommandId("%s"));' % BUTTONS[label])


def set_clipboard(text):
    win32clipboard.OpenClipboard()
    try:
        win32clipboard.EmptyClipboard()
        win32clipboard.SetClipboardText(text, win32con.CF_UNICODETEXT)
    finally:
        win32clipboard.CloseClipboard()


def read_result(win):
    try:
        summary = by_id(win, "SummaryText")[0].window_text()
    except Exception:  # noqa: BLE001
        summary = ""
    try:
        details = by_id(win, "DetailsBox")[0].get_value()
    except Exception:  # noqa: BLE001
        details = ""
    return summary, details


def close_result(win, read_seconds):
    time.sleep(read_seconds)
    if not click_button_in(win, r"^Sluiten$"):
        win.close()
    time.sleep(0.8)


# ---------------------------------------------------------------- model preparation (not recorded)
CHAT_PANE = "A1B2C3D4-E5F6-7890-ABCD-EF1234567890"     # VibeModel chat pane id (src/VibeModel/UI/ChatPane.cs)
PILE_PARAMS = (("X-coördinaat_funderingspaal", "0e4e2f36-6cf4-46c1-83ce-f5f251e08de5"),
               ("Y-coördinaat_funderingspaal", "0bd0d5fb-13db-4ec4-9a2e-8fe4e221f8f6"))


def prepare_model():
    """Hides the chat pane, gives the piles the IB parameters (as the IB template does), opens the
    3D view 'Kade' and clears the selection. The demo model is a throw-away copy."""
    vm_exec('var p = uiApp.GetDockablePane(new DockablePaneId(new Guid("%s"))); if (p.IsShown()) p.Hide(); sb.Append("ok");' % CHAT_PANE)
    spf = os.path.join(os.path.dirname(TAKE), "IB_demo_shared_parameters.txt")
    if not os.path.exists(spf):
        open(spf, "w").close()
    defs = ", ".join('new[]{"%s","%s"}' % t for t in PILE_PARAMS)
    out = vm_exec(r'''var app = uiApp.Application; app.SharedParametersFilename = @"%s";
      var f = app.OpenSharedParameterFile(); if (f == null) { sb.Append("no spf"); }
      else {
        var g = f.Groups.get_Item("IB") ?? f.Groups.Create("IB");
        var defs = new List<Definition>();
        foreach (var t in new[] { %s }) {
          var d = g.Definitions.get_Item(t[0]);
          if (d == null) { var o = new ExternalDefinitionCreationOptions(t[0], SpecTypeId.Length) { GUID = new Guid(t[1]), UserModifiable = true, Visible = true }; d = g.Definitions.Create(o); }
          defs.Add(d); }
        var cats = app.Create.NewCategorySet(); cats.Insert(doc.Settings.Categories.get_Item(BuiltInCategory.OST_StructuralFoundation));
        int n = 0; foreach (var d in defs) { var b = app.Create.NewInstanceBinding(cats);
          if (doc.ParameterBindings.Contains(d)) doc.ParameterBindings.ReInsert(d, b, BuiltInParameterGroup.PG_DATA); else doc.ParameterBindings.Insert(d, b, BuiltInParameterGroup.PG_DATA); n++; }
        sb.Append("bound " + n); }''' % (spf, defs))
    log("Pile parameters: " + out.strip()[:120])
    try:
        log("View: " + vm("activate", "Kade").splitlines()[2])
    except Exception as e:  # noqa: BLE001
        log("activate Kade failed: %s" % e)
    vm_exec('uiDoc.Selection.SetElementIds(new List<ElementId>()); sb.Append("ok");')
    time.sleep(2)


def hide_chat_pane():
    vm_exec('var p = uiApp.GetDockablePane(new DockablePaneId(new Guid("%s"))); if (p.IsShown()) p.Hide(); sb.Append("ok");' % CHAT_PANE)


def zoom_selection(margin_mm):
    """Zooms the active view to the selected elements (+ margin). A UI zoom only: nothing on the
    undo stack, so a Ctrl+Z afterwards still undoes the placement and not the camera."""
    vm_exec('''var ids = uiDoc.Selection.GetElementIds(); BoundingBoxXYZ bb = null;
      foreach (var id in ids) { var b = doc.GetElement(id).get_BoundingBox(uiDoc.ActiveView); if (b == null) continue;
        if (bb == null) bb = new BoundingBoxXYZ { Min = b.Min, Max = b.Max };
        else { bb.Min = new XYZ(Math.Min(bb.Min.X, b.Min.X), Math.Min(bb.Min.Y, b.Min.Y), Math.Min(bb.Min.Z, b.Min.Z));
               bb.Max = new XYZ(Math.Max(bb.Max.X, b.Max.X), Math.Max(bb.Max.Y, b.Max.Y), Math.Max(bb.Max.Z, b.Max.Z)); } }
      if (bb == null) { sb.Append("no bbox"); } else {
        double m = %s / 304.8; var uv = uiDoc.GetOpenUIViews().First(v => v.ViewId == uiDoc.ActiveView.Id);
        uv.ZoomAndCenterRectangle(bb.Min - new XYZ(m, m, m), bb.Max + new XYZ(m, m, m)); sb.Append("zoomed " + ids.Count); }''' % margin_mm)


def focus_view_and_send(keys):
    """Clicks an empty spot of the drawing area (gives Revit's view the keyboard focus, also when
    another window had it) and sends a shortcut such as ^z."""
    hide_chat_pane()
    smooth_move(1250, 1000, 0.5)      # water, lower right of the Kade view
    mouse.click(coords=(1250, 1000))
    time.sleep(0.8)
    from pywinauto import keyboard
    keyboard.send_keys(keys)


# ---------------------------------------------------------------- the show
def show():
    global REC
    hwnd = main_window()
    log("Revit main window hwnd %d" % hwnd)
    if not answer_startup_dialogs(900):
        raise RuntimeError("demo model did not open (see log)")
    hwnd = main_window(60)                 # the early hwnd can be the splash; take the real one now
    log("Revit main window hwnd %d: %s" % (hwnd, win32gui.GetWindowText(hwnd)))
    win32gui.ShowWindow(hwnd, win32con.SW_RESTORE)
    win32gui.SetWindowPos(hwnd, win32con.HWND_TOP, 0, 0, W, H, 0)
    try:
        win32gui.SetForegroundWindow(hwnd)
    except Exception:  # noqa: BLE001
        pass
    time.sleep(1.5)
    dismiss_revit_warnings()
    main = UIA.window(handle=hwnd)

    prepare_model()
    piles = vm_exec('''var c = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_StructuralFoundation)
        .WhereElementIsNotElementType().ToElements();
      int withPar = c.Count(e => e.LookupParameter("X-coördinaat_funderingspaal") != null);
      sb.Append(c.Count + " " + withPar + " " + (doc.IsWorkshared ? "workshared" : "not-workshared"));''').strip()
    log("Model check (foundations, with X-param, worksharing): " + piles)

    REC = subprocess.Popen([sys.executable, os.path.join(HERE, "rec.py"), FRAMES, "0", "0", str(W), str(H)])
    time.sleep(2.5)
    park_mouse()

    # ---- intro: the ribbon tab
    mark("card", "IB Tools|Revit-add-in van het Ingenieursbureau|Drie knoppen die Dynamo-scripts vervangen")
    mark("caption", "Tabblad IB Tools: Weergave (projectie aan/uit), Coördinaten (Paalcoördinaten, Modelpunt) en Model (Worksets)",
         step="Lint")
    buttons = find_ribbon_buttons(hwnd)
    for label in ("Hide", "Paalcoördinaten", "Modelpunt", "Worksets"):
        if label in buttons:
            r = buttons[label].rectangle()
            smooth_move((r.left + r.right) // 2, (r.top + r.bottom) // 2, 0.8)
            time.sleep(2.2)                        # tooltip
    park_mouse()
    time.sleep(0.8)

    # ---- 1. Worksets
    mark("card", "1 / 3  Worksets|Zet worksharing aan en maakt de IB-standaard worksets 01 t/m 06|Vervangt het Dynamo-script \"Worksets\"")
    mark("caption", "Worksets: één klik. Het model is nog niet workshared, dus IB Tools vraagt eerst om bevestiging.",
         step="1/3 Worksets")
    press("Worksets", buttons)
    ask = find_window(title_re=r"IB Worksets", timeout=60)
    if ask and not by_id(ask, "DetailsBox"):
        time.sleep(2.5)
        mark("caption", "Worksharing aanzetten? Ja.", step="1/3 Worksets")
        if not click_button_in(ask, r"^(Yes|Ja)$"):
            raise RuntimeError("confirm dialog: Yes not found")
    res = find_window(auto_id="DetailsBox", timeout=120)
    if not res:
        raise RuntimeError("Worksets result window not found")
    summary, details = read_result(res)
    log("Worksets result: " + summary + "\n" + details)
    mark("caption", "Klaar: worksharing staat aan, de standaard-worksets zijn hernoemd en 01 t/m 06 zijn aangemaakt.",
         step="1/3 Worksets")
    close_result(res, 6)
    dismiss_revit_warnings()
    park_mouse()

    # ---- 2. Modelpunt op coördinaat
    mark("card", "2 / 3  Modelpunt op coördinaat|Plaatst het IB-coördinaatpunt op een RD-coördinaat en NAP-hoogte|Werkt met de gedeelde coördinaten van het project")
    set_clipboard(CLIP_COORD)
    mark("caption", "Een RD-coördinaat staat op het klembord (bv. uit Excel): %s" % CLIP_COORD, step="2/3 Modelpunt")
    time.sleep(1.5)
    press("Modelpunt", buttons)
    win = find_window(auto_id="XBox", timeout=60)
    if not win:
        raise RuntimeError("Modelpunt window not found")
    time.sleep(1.0)
    mark("caption", "Het venster is al ingevuld vanaf het klembord; het type volgt uit de schaal van de view. Plaatsen.",
         step="2/3 Modelpunt")
    time.sleep(4.0)
    if not click_button_in(win, r"^Plaatsen$"):
        raise RuntimeError("Plaatsen button not found")
    res = find_window(auto_id="DetailsBox", timeout=120)
    if not res:
        raise RuntimeError("Modelpunt result window not found")
    summary, details = read_result(res)
    log("Modelpunt result: " + summary + "\n" + details)
    m = re.search(r"Element-id:\s*(\d+)", details)
    mark("caption", "Resultaat: het punt staat op de gevraagde RD/NAP-positie; de afwijking wordt teruggelezen en gemeld.",
         step="2/3 Modelpunt")
    close_result(res, 6)
    dismiss_revit_warnings()
    if m:
        vm_exec('var id = new ElementId(%s); uiDoc.Selection.SetElementIds(new List<ElementId>{id}); uiDoc.ShowElements(id); sb.Append("ok");' % m.group(1))
        mark("caption", "Het geplaatste coördinaatpunt in het model.", step="2/3 Modelpunt")
        time.sleep(5)
        vm_exec('uiDoc.Selection.SetElementIds(new List<ElementId>()); sb.Append("ok");')
    park_mouse()

    # ---- 3. Paalcoördinaten
    mark("card", "3 / 3  Paalcoördinaten|Schrijft de RD-coördinaten van elke paal in X-/Y-coördinaat_funderingspaal|Vervangt het Dynamo-script \"Funderingspalen_coordinaten\"")
    try:
        vm("activate", "Kade")
    except Exception as e:  # noqa: BLE001
        log("activate Kade failed: %s" % e)
    vm_exec('foreach (var uv in uiDoc.GetOpenUIViews()) if (uv.ViewId == uiDoc.ActiveView.Id) uv.ZoomToFit(); sb.Append("ok");')
    time.sleep(1.5)
    mark("caption", "Niets geselecteerd, dus alle palen in het model worden gedaan (met een selectie: alleen die palen).",
         step="3/3 Paalcoördinaten")
    time.sleep(1.5)
    press("Paalcoördinaten", buttons)
    res = find_window(auto_id="DetailsBox", timeout=300)
    if not res:
        raise RuntimeError("Paalcoördinaten result window not found")
    summary, details = read_result(res)
    log("Paalcoördinaten result: " + summary + "\n" + details[:2000])
    mark("caption", "Het rapport: per paal de RD-coördinaten (m, 3 decimalen) en wat er gewijzigd is. Te kopiëren naar het klembord.",
         step="3/3 Paalcoördinaten")
    close_result(res, 8)
    dismiss_revit_warnings()
    # a pile schedule with the two parameters makes the written values visible to the viewer
    sched = vm_exec('''var s = ViewSchedule.CreateSchedule(doc, new ElementId(BuiltInCategory.OST_StructuralFoundation));
      s.Name = "IB demo - paalcoördinaten";
      var want = new[] { "Family and Type", "X-coördinaat_funderingspaal", "Y-coördinaat_funderingspaal" };
      var fields = s.Definition.GetSchedulableFields();
      foreach (var w in want) { var f = fields.FirstOrDefault(x => x.GetName(doc) == w); if (f == null) continue;
        var sf = s.Definition.AddField(f); sf.GridColumnWidth = w.StartsWith("Family") ? 0.25 : 0.15;   // feet (3" / 1.8"); default 1" truncates
        if (!w.StartsWith("Family")) { var fo = sf.GetFormatOptions(); fo.UseDefault = false; fo.SetUnitTypeId(UnitTypeId.Millimeters); fo.Accuracy = 0.001; sf.SetFormatOptions(fo); } }
      sb.Append(s.Name + " fields " + s.Definition.GetFieldCount());''').strip()
    log("Schedule: " + sched)
    if "fields" in sched:
        try:
            vm("activate", "IB demo - paalcoördinaten")
        except Exception as e:  # noqa: BLE001
            log("activate schedule failed: %s" % e)
        mark("caption", "Een schedule van de palen: X-/Y-coördinaat_funderingspaal zijn gevuld (lengte-parameters in projecteenheden, zoals het Dynamo-script).",
             step="3/3 Paalcoördinaten")
        time.sleep(7)
        try:
            vm("activate", "Kade")
        except Exception as e:  # noqa: BLE001
            log("activate Kade failed: %s" % e)
    park_mouse()
    time.sleep(1.0)
    mark("card", "IB Tools|Worksets · Modelpunt op coördinaat · Paalcoördinaten|Ingenieursbureau Gemeente Amsterdam")
    mark("end")


def show_remmingwerk():
    """Separate, short video for the Remmingwerk button on the same kade model."""
    global REC
    hwnd = main_window()
    if not answer_startup_dialogs(900):
        raise RuntimeError("demo model did not open (see log)")
    hwnd = main_window(60)
    win32gui.ShowWindow(hwnd, win32con.SW_RESTORE)
    win32gui.SetWindowPos(hwnd, win32con.HWND_TOP, 0, 0, W, H, 0)
    try:
        win32gui.SetForegroundWindow(hwnd)
    except Exception:  # noqa: BLE001
        pass
    time.sleep(1.5)
    dismiss_revit_warnings()
    main = UIA.window(handle=hwnd)
    vm_exec('var p = uiApp.GetDockablePane(new DockablePaneId(new Guid("%s"))); if (p.IsShown()) p.Hide(); sb.Append("ok");' % CHAT_PANE)

    # The front of the remmingwerk: a 20 m model line 3 m in front of the quay wall, drawn so that
    # "right of the drawing direction" is the water side (quay wall of the demo model: see
    # VibeModel demo_out/video/build_deel1.py; the street is left of p0->p1).
    line = vm_exec('''var p0 = new XYZ(-8844.8 / 304.8, -5033.9 / 304.8, 0); var p1 = new XYZ(-28313.2 / 304.8, -454.3 / 304.8, 0);
      var d = (p1 - p0).Normalize(); var water = new XYZ(d.Y, -d.X, 0);
      var a = p0 + d * (2000 / 304.8) + water * (3000 / 304.8); var b = a + d * (20000 / 304.8);
      var sp = SketchPlane.Create(doc, Plane.CreateByNormalAndOrigin(XYZ.BasisZ, XYZ.Zero));
      var mc = doc.Create.NewModelCurve(Line.CreateBound(a, b), sp);
      sb.Append(mc.Id.ToString());''').strip().splitlines()[0]
    log("Model line: " + line)
    try:
        vm("activate", "Kade")
    except Exception as e:  # noqa: BLE001
        log("activate Kade failed: %s" % e)
    # The UIA walk over Revit's ribbon can take a minute or two: do it before recording starts.
    buttons = find_ribbon_buttons(hwnd)
    vm_exec('var id = new ElementId(%s); uiDoc.Selection.SetElementIds(new List<ElementId>{id}); sb.Append("ok");' % line)
    zoom_selection(12000)
    hide_chat_pane()
    time.sleep(2)

    REC = subprocess.Popen([sys.executable, os.path.join(HERE, "rec.py"), FRAMES, "0", "0", str(W), str(H)])
    time.sleep(2.5)
    park_mouse()

    mark("card", "Remmingwerk|Plaatst een standaard remmingwerk langs een modellijn|Vervangt het Dynamo-script \"Remmingwerk v1.2\"")
    mark("caption", "De voorzijde is een modellijn vóór de kademuur (hier 20 m, geselecteerd). De tekenrichting bepaalt aan welke kant de palen komen.",
         step="Remmingwerk")
    time.sleep(5)
    mark("caption", "Knop Remmingwerk op het paneel Kademuren.", step="Remmingwerk")
    press("Remmingwerk", buttons)
    win = find_window(auto_id="RowsBox", timeout=60)
    if not win:
        raise RuntimeError("Remmingwerk window not found")
    time.sleep(1.0)
    mark("caption", "Eén venster: NAP-level, de vijf typen uit het project (HEA450, buispaal ø610x12.5, IB-wrijfhout) en de maten van het Dynamo-script.",
         step="Remmingwerk")
    time.sleep(6)
    mark("caption", "Onderin rekent de knop mee: 5 palen à 21,8 m, eerste op 1,500 m. Plaatsen.", step="Remmingwerk")
    time.sleep(4)
    if not click_button_in(win, r"^Plaatsen$"):
        raise RuntimeError("Plaatsen button not found")
    res = find_window(auto_id="DetailsBox", timeout=180)
    if not res:
        raise RuntimeError("Remmingwerk result window not found")
    summary, details = read_result(res)
    log("Remmingwerk result: " + summary + "\n" + details[:1500])
    mark("caption", "Alles staat in één stap in het model: buispalen, stalen stijlen en gordingen, wrijfgordingen en wrijfstijlen. Teruggelezen: afwijking 0,000 mm.",
         step="Remmingwerk")
    close_result(res, 8)
    dismiss_revit_warnings()

    hide_chat_pane()
    zoom_selection(6000)
    mark("caption", "Het remmingwerk in 3D, nog geselecteerd. 21 elementen, alles op de berekende plek.", step="Remmingwerk")
    time.sleep(6)
    vm_exec('uiDoc.Selection.SetElementIds(new List<ElementId>()); sb.Append("ok");')
    time.sleep(3)

    mark("caption", "Eén Ctrl+Z haalt het hele remmingwerk weer weg.", step="Remmingwerk")
    focus_view_and_send("^z")
    time.sleep(5)
    mark("caption", "En Ctrl+Y zet het terug.", step="Remmingwerk")
    focus_view_and_send("^y")
    time.sleep(5)
    park_mouse()
    mark("card", "Remmingwerk|Lijn kiezen, één venster, Plaatsen|Ingenieursbureau Gemeente Amsterdam")
    mark("end")


SCENES = {"ibtools": lambda: show(), "remmingwerk": show_remmingwerk}


def main():
    ok = False
    scene = sys.argv[3] if len(sys.argv) > 3 else "ibtools"
    try:
        SCENES[scene]()
        ok = True
    except Exception:  # noqa: BLE001
        log("FOUT:\n" + traceback.format_exc())
        try:
            from PIL import ImageGrab
            ImageGrab.grab(all_screens=True).save(os.path.join(TAKE, "error.png"))
        except Exception:  # noqa: BLE001
            pass
        mark("end")
    finally:
        if REC:
            open(os.path.join(FRAMES, "STOP"), "w").close()
            try:
                REC.wait(timeout=60)
            except Exception:  # noqa: BLE001
                REC.kill()
        json.dump(TIMELINE, open(os.path.join(TAKE, "timeline.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
        log("DONE ok=%s" % ok)
    sys.exit(0 if ok else 1)


if __name__ == "__main__":
    main()
