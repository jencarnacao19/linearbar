"""Generates src/LinearBar/Assets/LinearBar.ico: a dark rounded tile with a mini dock.

Run from the repository root:  python tools/make-icon.py   (requires Pillow)
"""
from PIL import Image, ImageDraw

SIZE = 1024


def draw() -> Image.Image:
    img = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.rounded_rectangle((40, 40, SIZE - 40, SIZE - 40), radius=220, fill=(28, 28, 33, 255))
    # Bar strip
    d.rounded_rectangle((120, 700, SIZE - 120, 800), radius=50, fill=(48, 48, 56, 255))
    # Five dock icons growing towards the centre, like the magnified dock
    sizes = [90, 130, 190, 130, 90]
    colours = [(90, 169, 255), (183, 140, 255), (79, 209, 165), (255, 180, 84), (90, 169, 255)]
    gap = 22
    total = sum(sizes) + gap * (len(sizes) - 1)
    x = (SIZE - total) // 2
    for s, c in zip(sizes, colours):
        d.rounded_rectangle((x, 760 - s, x + s, 760), radius=s // 4, fill=c + (255,))
        x += s + gap
    # Sparkline above
    pts = [(200, 420), (300, 380), (380, 450), (470, 300), (560, 360), (650, 250), (740, 330), (824, 280)]
    d.line(pts, fill=(90, 169, 255, 255), width=34, joint="curve")
    return img


if __name__ == "__main__":
    draw().save(
        "src/LinearBar/Assets/LinearBar.ico",
        sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)],
    )
    print("written src/LinearBar/Assets/LinearBar.ico")
