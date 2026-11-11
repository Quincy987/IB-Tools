"""Frames + timeline -> demo mp4 with title cards, a step badge and a caption bar.
Usage: python cut.py <take_dir> <out.mp4> [stills_dir]
  <take_dir>/frames/index.txt + %06d.jpg (from rec.py), <take_dir>/timeline.json (from drive.py)."""
import json, os, sys, textwrap
from PIL import Image, ImageDraw, ImageFont
import av

TAKE, OUT = sys.argv[1], sys.argv[2]
STILLS = sys.argv[3] if len(sys.argv) > 3 else None
FRAMES = os.path.join(TAKE, "frames")
FPS, W, H = 10, 1600, 1000
CARD_SEC, LEAD_SEC = 3.2, 0.6
BG, ACCENT, INK, SOFT = (18, 32, 51), (236, 0, 20), (255, 255, 255), (190, 200, 212)   # Amsterdam red accent


def font(sz, bold=False):
    for n in (["segoeuib.ttf", "arialbd.ttf"] if bold else ["segoeui.ttf", "arial.ttf"]):
        try:
            return ImageFont.truetype(n, sz)
        except Exception:  # noqa: BLE001
            pass
    return ImageFont.load_default()


F_TITLE, F_SUB, F_SMALL, F_CAP, F_BADGE = font(64, True), font(34), font(26), font(26), font(22, True)

tl = json.load(open(os.path.join(TAKE, "timeline.json"), encoding="utf-8"))
frames = [(int(a), float(b)) for a, b in (l.split() for l in open(os.path.join(FRAMES, "index.txt")) if l.strip())]
if not frames:
    sys.exit("no frames")
t_start, t_end = frames[0][1], frames[-1][1]
ends = [e["t"] for e in tl if e["kind"] == "end"]
if ends:
    t_end = min(t_end, ends[0])


def frame_at(t):
    """Latest recorded frame at or before t (binary search)."""
    lo, hi = 0, len(frames) - 1
    while lo < hi:
        mid = (lo + hi + 1) // 2
        if frames[mid][1] <= t:
            lo = mid
        else:
            hi = mid - 1
    return frames[lo][0]


def card_image(text):
    lines = text.split("|")
    im = Image.new("RGB", (W, H), BG)
    d = ImageDraw.Draw(im)
    d.rectangle([0, 0, 18, H], fill=ACCENT)
    y = H // 2 - 110
    for i, line in enumerate(lines):
        f = F_TITLE if i == 0 else (F_SUB if i == 1 else F_SMALL)
        col = INK if i < 2 else SOFT
        d.text((120, y), line, font=f, fill=col)
        y += (f.size if hasattr(f, "size") else 30) + 28
    d.text((120, H - 70), "IB Tools · Revit 2023 · Ingenieursbureau Gemeente Amsterdam", font=F_SMALL, fill=SOFT)
    return im


def overlay(im, caption, step):
    """Semi-transparent caption bar at the bottom and a step badge top-left."""
    base = im.convert("RGBA")
    ov = Image.new("RGBA", base.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(ov)
    if caption:
        lines = textwrap.wrap(caption, 95)[:2]
        bar_h = 26 + 36 * len(lines)
        d.rectangle([0, H - bar_h, W, H], fill=(10, 20, 35, 215))
        d.rectangle([0, H - bar_h, 12, H], fill=ACCENT + (255,))
        y = H - bar_h + 12
        for line in lines:
            d.text((32, y), line, font=F_CAP, fill=INK)
            y += 36
    if step:
        tw = d.textlength(step, font=F_BADGE)
        d.rounded_rectangle([14, 14, 14 + tw + 28, 54], radius=8, fill=(10, 20, 35, 215))
        d.text((28, 22), step, font=F_BADGE, fill=INK)
    return Image.alpha_composite(base, ov).convert("RGB")


# events in time order; captions persist until the next caption/card
events = sorted((e for e in tl if e["kind"] in ("card", "caption")), key=lambda e: e["t"])

with av.open(OUT, "w") as c:
    st = c.add_stream("libx264", rate=FPS)
    st.width, st.height, st.pix_fmt = W, H, "yuv420p"
    st.options = {"crf": "21", "preset": "medium"}
    n = 0
    cache = {}

    def emit(img):
        global n
        for pkt in st.encode(av.VideoFrame.from_image(img)):
            c.mux(pkt)
        n += 1

    def screen(t):
        fi = frame_at(t)
        if fi not in cache:
            cache.clear()
            cache[fi] = Image.open(os.path.join(FRAMES, "%06d.jpg" % fi)).convert("RGB").resize((W, H), Image.LANCZOS)
        return cache[fi]

    caption, step = "", ""
    t = t_start
    stills = []
    for i, e in enumerate(events):
        nxt = events[i + 1]["t"] if i + 1 < len(events) else t_end
        if e["kind"] == "card":
            img = card_image(e["text"])
            for _ in range(int(CARD_SEC * FPS)):
                emit(img)
            caption = ""
            t = max(t, e["t"])
            continue
        caption, step = e["text"], e.get("step", step)
        t = max(t, e["t"] - LEAD_SEC)
        first = True
        while t < nxt and t <= t_end:
            img = overlay(screen(t), caption, step)
            if first and STILLS:
                stills.append((step, caption, img))
                first = False
            emit(img)
            t += 1.0 / FPS
    for pkt in st.encode():
        c.mux(pkt)

print("%s: %d frames, %.0f s" % (OUT, n, n / FPS))
if STILLS:
    os.makedirs(STILLS, exist_ok=True)
    for k, (step, cap, img) in enumerate(stills):
        p = os.path.join(STILLS, "still-%02d.png" % (k + 1))
        img.save(p)
        print("still", p, "-", step, "-", cap[:60])
