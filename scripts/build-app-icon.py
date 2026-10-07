#!/usr/bin/env python3
"""Regenerate the shared app icon. Requires Pillow; normal builds use the saved PNG/ICO."""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFilter

root = Path(__file__).resolve().parent.parent
scale = 3
size = 1024
def box(values):
    return tuple(round(v * scale) for v in values)

# A white macOS-style rounded tile with the existing grey ring-and-dot mark.
# Transparency is confined to the outside of the tile and its soft shadow.
image = Image.new('RGBA', (size * scale, size * scale))
shadow = Image.new('RGBA', image.size)
ImageDraw.Draw(shadow).rounded_rectangle(box((96, 108, 928, 940)), radius=184 * scale, fill=(0, 0, 0, 35))
image.alpha_composite(shadow.filter(ImageFilter.GaussianBlur(12 * scale)))
draw = ImageDraw.Draw(image)
draw.rounded_rectangle(box((96, 96, 928, 928)), radius=184 * scale, fill='white')
draw.ellipse(box((245, 245, 779, 779)), fill='#808080')
draw.ellipse(box((279, 279, 745, 745)), fill='white')
draw.ellipse(box((430, 430, 594, 594)), fill='#808080')
image = image.resize((size, size), Image.Resampling.LANCZOS)
(root / 'Assets').mkdir(exist_ok=True)
image.save(root / 'Assets/AppIcon.png')
image.save(root / 'app.ico', sizes=[(n, n) for n in (16, 24, 32, 48, 64, 128, 256)])
print('Updated Assets/AppIcon.png and Windows app.ico')
