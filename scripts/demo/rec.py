"""Screen recorder for the demo take: grabs a screen region (DirectX views included) at ~8 fps,
writes <dir>/%06d.jpg + index.txt ("frame  unix-time"), stops when <dir>/STOP appears.
Usage: python rec.py <frames_dir> [x y w h]"""
import ctypes, os, sys, time
ctypes.windll.shcore.SetProcessDpiAwareness(2)
from PIL import ImageGrab

OUT = sys.argv[1]
x, y, w, h = [int(v) for v in sys.argv[2:6]] if len(sys.argv) >= 6 else (0, 0, 1920, 1200)
os.makedirs(OUT, exist_ok=True)
stop = os.path.join(OUT, "STOP")
if os.path.exists(stop):
    os.remove(stop)
idx = open(os.path.join(OUT, "index.txt"), "w")
i, period = 0, 1.0 / 8
while not os.path.exists(stop):
    t = time.time()
    try:
        im = ImageGrab.grab(bbox=(x, y, x + w, y + h), all_screens=True)
        im.save(os.path.join(OUT, "%06d.jpg" % i), quality=85)
        idx.write("%d %.3f\n" % (i, t)); idx.flush(); i += 1
    except Exception as e:  # noqa: BLE001
        print("grab failed:", e, flush=True)
    time.sleep(max(0, period - (time.time() - t)))
idx.close()
print("recorded %d frames in %s" % (i, OUT), flush=True)
