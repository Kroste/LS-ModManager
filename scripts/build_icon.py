"""
LS-ModManager App-Icon-Generator.

Design: stilisierter Traktor (LS-Farming-Thema) auf abgerundetem dunklem Grund.
Farben: Grün (App-Akzent, Farming) auf Kroste-Surface, ohne Text — funktioniert
auch als 16x16-Favicon.

Erzeugt:
- LSModManager/Assets/lsmodmanager.png   (256x256, master)
- LSModManager/Assets/lsmodmanager.ico   (Windows-Multi-Res)
"""

import os
from PIL import Image, ImageDraw

# Farben — Grün als App-Akzent, Kroste-Dark als Grund
ACCENT   = (76, 175, 80, 255)      # #4CAF50 Farming-Grün
ACCENT_D = (46, 125, 50, 255)      # #2E7D32 dunkler
YELLOW   = (224, 177, 76, 255)     # Kroste-Gold für Felgen
SURFACE  = (22, 28, 35, 255)       # #161C23
BORDER   = (42, 51, 61, 255)       # #2A333D
BLACK    = (12, 15, 18, 255)
WHITE    = (240, 240, 240, 255)
TRANSP   = (0, 0, 0, 0)

CORNER = 48

OUT_DIR = os.path.join(os.path.dirname(__file__), "..", "LSModManager", "Assets")
OUT_DIR = os.path.abspath(OUT_DIR)


def make_icon(size: int) -> Image.Image:
    """Baut das Icon in der angegebenen Kantenlaenge (stilisierter Traktor)."""
    s = size / 256.0
    img = Image.new("RGBA", (size, size), TRANSP)
    d = ImageDraw.Draw(img)

    # Grund: abgerundetes Quadrat
    corner = int(CORNER * s)
    d.rounded_rectangle(
        [(0, 0), (size - 1, size - 1)],
        radius=corner, fill=SURFACE, outline=BORDER,
        width=max(1, int(2 * s)),
    )

    # Vereinfachter Traktor: dickes Hinterrad, kleines Vorderrad, Kabinen-Rumpf.
    # Alle Masse in 256er-Koordinaten, dann skaliert.
    def sc(x): return int(x * s)

    # Hinterrad (gross, hinten links)
    hr_cx, hr_cy, hr_r = sc(85), sc(180), sc(46)
    d.ellipse([(hr_cx - hr_r, hr_cy - hr_r), (hr_cx + hr_r, hr_cy + hr_r)],
              fill=BLACK, outline=None)
    d.ellipse([(hr_cx - sc(24), hr_cy - sc(24)), (hr_cx + sc(24), hr_cy + sc(24))],
              fill=YELLOW, outline=None)
    d.ellipse([(hr_cx - sc(10), hr_cy - sc(10)), (hr_cx + sc(10), hr_cy + sc(10))],
              fill=SURFACE, outline=None)

    # Vorderrad (klein, vorne rechts)
    vr_cx, vr_cy, vr_r = sc(198), sc(198), sc(28)
    d.ellipse([(vr_cx - vr_r, vr_cy - vr_r), (vr_cx + vr_r, vr_cy + vr_r)],
              fill=BLACK, outline=None)
    d.ellipse([(vr_cx - sc(13), vr_cy - sc(13)), (vr_cx + sc(13), vr_cy + sc(13))],
              fill=YELLOW, outline=None)
    d.ellipse([(vr_cx - sc(5), vr_cy - sc(5)), (vr_cx + sc(5), vr_cy + sc(5))],
              fill=SURFACE, outline=None)

    # Rumpf (grüner Body)
    d.rounded_rectangle(
        [(sc(60), sc(120)), (sc(216), sc(178))],
        radius=sc(8), fill=ACCENT, outline=ACCENT_D, width=max(1, sc(2)),
    )

    # Motorhaube (rechts, etwas höher)
    d.rounded_rectangle(
        [(sc(150), sc(140)), (sc(220), sc(180))],
        radius=sc(6), fill=ACCENT_D, outline=None,
    )
    # Auspuff
    d.rectangle([(sc(160), sc(90)), (sc(174), sc(140))], fill=(80, 80, 90, 255))
    d.ellipse([(sc(157), sc(85)), (sc(177), sc(96))], fill=(60, 60, 70, 255))

    # Kabine mit Fenster
    d.rounded_rectangle(
        [(sc(80), sc(70)), (sc(150), sc(140))],
        radius=sc(6), fill=ACCENT, outline=ACCENT_D, width=max(1, sc(2)),
    )
    d.rounded_rectangle(
        [(sc(92), sc(82)), (sc(140), sc(120))],
        radius=sc(4), fill=(160, 210, 235, 255), outline=None,
    )

    # Scheinwerfer vorn (gold)
    d.ellipse([(sc(210), sc(150)), (sc(222), sc(162))], fill=YELLOW, outline=None)

    return img


def main():
    os.makedirs(OUT_DIR, exist_ok=True)

    master_path = os.path.join(OUT_DIR, "lsmodmanager.png")
    ico_path = os.path.join(OUT_DIR, "lsmodmanager.ico")

    master = make_icon(256)
    master.save(master_path, "PNG")
    print(f"Wrote {master_path}")

    sizes = [16, 24, 32, 48, 64, 128, 256]
    icons = [make_icon(s) for s in sizes]
    icons[0].save(
        ico_path,
        format="ICO",
        sizes=[(s, s) for s in sizes],
        append_images=icons[1:],
    )
    print(f"Wrote {ico_path} (multi-res: {sizes})")


if __name__ == "__main__":
    main()
