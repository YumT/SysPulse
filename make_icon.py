"""SysPulsar アイコン生成。ダーク背景 + パルス波形(ECG 風)を描き、マルチサイズ .ico にする。"""
from PIL import Image, ImageDraw, ImageFilter
import os

SIZE = 512
BG = (20, 20, 20, 255)        # #141414
PANEL = (30, 30, 30, 255)     # #1e1e1e
PULSE = (79, 195, 247, 255)   # #4fc3f7 (CPU 色)
GRID = (44, 44, 44, 255)      # #2c2c2c

def rounded_rect(draw, box, radius, fill):
    draw.rounded_rectangle(box, radius=radius, fill=fill)

def make_icon() -> Image.Image:
    img = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    m = 14  # 余白
    r = SIZE // 5  # 角丸
    rounded_rect(d, (m, m, SIZE - m, SIZE - m), r, BG)

    # 内側パネル
    p = 56
    rounded_rect(d, (p + m, p + m + 30, SIZE - p - m, SIZE - p - m - 30), 28, PANEL)

    # グリッド線(うっすら)
    gx0, gx1 = p + m + 16, SIZE - p - m - 16
    gy = SIZE // 2
    d.line((gx0, gy, gx1, gy), fill=GRID, width=3)
    for i in range(1, 4):
        x = gx0 + (gx1 - gx0) * i / 4
        d.line((x, gy - 60, x, gy + 60), fill=GRID, width=2)

    # パルス波形(ECG 風)。レイヤーを分けてグローを合成
    pts = [
        (gx0, gy),
        (gx0 + 60, gy),
        (gx0 + 90, gy - 34),
        (gx0 + 120, gy),
        (gx0 + 150, gy + 90),
        (gx0 + 185, gy - 130),
        (gx0 + 220, gy),
        (gx0 + 250, gy + 40),
        (gx0 + 280, gy),
        (gx1, gy),
    ]
    line_layer = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    ld = ImageDraw.Draw(line_layer)
    ld.line(pts, fill=PULSE, width=26, joint="curve")
    # 頂点を丸く
    for x, y in pts:
        ld.ellipse((x - 13, y - 13, x + 13, y + 13), fill=PULSE)

    glow = line_layer.filter(ImageFilter.GaussianBlur(18))
    img.alpha_composite(glow)
    img.alpha_composite(line_layer)
    return img

out_dir = os.path.join(os.path.dirname(__file__), "src", "SysPulsar.App", "Assets")
os.makedirs(out_dir, exist_ok=True)

img = make_icon()
img.save(os.path.join(out_dir, "app.png"))

sizes = [(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)]
img.save(os.path.join(out_dir, "app.ico"), format="ICO", sizes=sizes, append_images=[])
print("written:", out_dir)
