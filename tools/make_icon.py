"""Draws the application icon (a save disk with a green "convert" badge) and writes a multi-size .ico.

    python tools/make_icon.py --ico SaveStateConverter.Gui/app.ico [--preview preview.png]

Original artwork: no PCSX2 or console logos. Every size is drawn separately from the same
256-unit design, 8x supersampled, with fewer details at small sizes and edges snapped to the
pixel grid up to 48 px. Sizes below 256 are stored as 32-bit bitmaps, 256 as PNG.
"""
import argparse
import io
import math
import struct

from PIL import Image, ImageChops, ImageDraw

SIZES = [16, 20, 24, 32, 40, 48, 64, 256]
SUPER = 8


def rgba(hex_color, alpha=255):
    value = hex_color.lstrip("#")
    return tuple(int(value[i:i + 2], 16) for i in (0, 2, 4)) + (alpha,)


def rounded_outline(x0, y0, x1, y1, radius, chamfer, steps=24):
    """Clockwise outline with rounded corners, the top-right corner cut off by `chamfer`."""
    points = []

    def arc(cx, cy, start, end):
        for i in range(steps + 1):
            angle = math.radians(start + (end - start) * i / steps)
            points.append((cx + radius * math.cos(angle), cy + radius * math.sin(angle)))

    arc(x0 + radius, y0 + radius, 180, 270)
    points += [(x1 - chamfer, y0), (x1, y0 + chamfer)]
    arc(x1 - radius, y1 - radius, 0, 90)
    arc(x0 + radius, y1 - radius, 90, 180)
    return points


def gradient_fill(image, mask, top, bottom, y0, y1):
    """Composites a vertical gradient (top color at y0, bottom color at y1) through `mask`."""
    layer = Image.new("RGBA", image.size)
    draw = ImageDraw.Draw(layer)
    for y in range(image.size[1]):
        t = min(max((y - y0) / max(1.0, y1 - y0), 0.0), 1.0)
        draw.line([(0, y), (image.size[0], y)], fill=tuple(round(a + (b - a) * t) for a, b in zip(top, bottom)))
    image.alpha_composite(Image.composite(layer, Image.new("RGBA", image.size), mask))


def arrow_arc(draw, cx, cy, radius, start, end, stroke, head_length, head_width, color):
    """A clockwise arc from `start` to `end` degrees (0 = 3 o'clock) ending in an arrowhead."""
    head_angle = math.degrees(head_length / radius)
    outer = radius + stroke / 2  # Pillow draws the arc width inward from the box
    draw.arc([cx - outer, cy - outer, cx + outer, cy + outer], start, end - head_angle + 2, fill=color, width=round(stroke))
    base = math.radians(end - head_angle)
    tip = math.radians(end)
    normal = (math.cos(base), math.sin(base))
    center = (cx + radius * normal[0], cy + radius * normal[1])
    draw.polygon([
        (center[0] + normal[0] * head_width / 2, center[1] + normal[1] * head_width / 2),
        (cx + radius * math.cos(tip), cy + radius * math.sin(tip)),
        (center[0] - normal[0] * head_width / 2, center[1] - normal[1] * head_width / 2),
    ], fill=color)


def right_arrow(draw, cx, cy, length, shaft, head_length, head_width, color):
    """A horizontal arrow centred on (cx, cy), pointing right."""
    x0, x1 = cx - length / 2, cx + length / 2
    draw.rectangle([x0, cy - shaft / 2, x1 - head_length + shaft / 2, cy + shaft / 2], fill=color)
    draw.polygon([(x1 - head_length, cy - head_width / 2), (x1, cy), (x1 - head_length, cy + head_width / 2)], fill=color)


def render(size):
    canvas = size * SUPER
    image = Image.new("RGBA", (canvas, canvas), (0, 0, 0, 0))
    small, medium, snap = size <= 24, size <= 40, size <= 48

    def u(value, snapped=snap):
        """Design units (0-256) to canvas pixels, optionally on whole target pixels."""
        pixels = value * size / 256
        return (round(pixels) if snapped else pixels) * SUPER

    def width(units, minimum_px):
        return max(units * size / 256, minimum_px) * SUPER

    draw = ImageDraw.Draw(image)

    # Disk body: dark rim, blue gradient inside.
    x0, y0, x1, y1 = u(14), u(10), u(206), u(202)
    radius, chamfer, rim = u(26, False), u(38, False), width(5, 1.0)
    draw.polygon(rounded_outline(x0, y0, x1, y1, radius, chamfer), fill=rgba("#0A3F8F"))
    mask = Image.new("L", image.size, 0)
    ImageDraw.Draw(mask).polygon(rounded_outline(x0 + rim, y0 + rim, x1 - rim, y1 - rim, radius - rim, chamfer - rim * 0.6), fill=255)
    gradient_fill(image, mask, rgba("#4A9BFF"), rgba("#0058D0"), y0, y1)

    # Metal shutter at the top, with its window from 32 px up.
    sx0, sx1, sy0, sy1 = u(62), u(156), y0 + rim, u(80)
    mask = Image.new("L", image.size, 0)
    shutter = ImageDraw.Draw(mask)
    shutter.rounded_rectangle([sx0, sy0, sx1, sy1], radius=u(7, False), fill=255)
    shutter.rectangle([sx0, sy0, sx1, sy0 + u(8, False)], fill=255)
    gradient_fill(image, mask, rgba("#F2F6FB"), rgba("#C4D2E6"), sy0, sy1)
    draw = ImageDraw.Draw(image)
    if not small:
        draw.rounded_rectangle([u(118), u(24), u(140), u(66)], radius=u(4, False), fill=rgba("#0A3F8F"))

    # Label at the bottom, with two text lines from 48 px up.
    lx0, lx1, ly0, ly1 = u(40), u(180), u(108), y1 - rim
    draw.rounded_rectangle([lx0, ly0, lx1, ly1], radius=u(12, False), fill=rgba("#F8FAFE"))
    draw.rectangle([lx0, ly1 - u(14, False), lx1, ly1], fill=rgba("#F8FAFE"))
    if not medium:
        line = round(width(9, 1.0))
        for y in (134, 160):
            draw.line([(u(60), u(y)), (u(146), u(y))], fill=rgba("#BCCDE6"), width=line)

    # Badge: a transparent gap around it, then a green disc with white arrows.
    cx, cy, badge = (u(184, False), u(184, False), u(70, False)) if small else (u(188, False), u(188, False), u(62, False))
    gap = u(10, False)
    cut = Image.new("L", image.size, 0)
    ImageDraw.Draw(cut).ellipse([cx - badge - gap, cy - badge - gap, cx + badge + gap, cy + badge + gap], fill=255)
    image.putalpha(ImageChops.subtract(image.getchannel("A"), cut))
    draw = ImageDraw.Draw(image)
    draw.ellipse([cx - badge, cy - badge, cx + badge, cy + badge], fill=rgba("#11662E"))
    inner = badge - width(5, 1.0)
    mask = Image.new("L", image.size, 0)
    ImageDraw.Draw(mask).ellipse([cx - inner, cy - inner, cx + inner, cy + inner], fill=255)
    gradient_fill(image, mask, rgba("#5AD27B"), rgba("#1E9446"), cy - inner, cy + inner)
    draw = ImageDraw.Draw(image)
    white = rgba("#FFFFFF")
    if size <= 20:
        # Circular arrows turn into a blob this small: a plain forward arrow instead.
        right_arrow(draw, cx, cy, badge * 1.2, width(20, 1.5), badge * 0.62, badge * 1.05, white)
    else:
        stroke = width(16 if small else 12, 1.4)
        for start, end in ((200, 335), (20, 155)):
            arrow_arc(draw, cx, cy, badge * 0.52, start, end, stroke, badge * (0.42 if small else 0.36), badge * (0.6 if small else 0.52), white)

    # Downsample with premultiplied alpha so edges don't pick up dark fringes.
    return image.convert("RGBa").resize((size, size), Image.Resampling.BOX).convert("RGBA")


def bitmap_entry(image):
    """32-bit BGRA DIB with AND mask, as stored inside .ico files."""
    w, h = image.size
    header = struct.pack("<IiiHHIIiiII", 40, w, h * 2, 1, 32, 0, 0, 0, 0, 0, 0)
    pixels = image.tobytes("raw", "BGRA")
    rows = [pixels[y * w * 4:(y + 1) * w * 4] for y in range(h)]
    alpha = image.getchannel("A").tobytes()
    stride = (w + 31) // 32 * 4
    mask_rows = []
    for y in range(h):
        bits = bytearray(stride)
        for x in range(w):
            if alpha[y * w + x] == 0:
                bits[x // 8] |= 0x80 >> (x % 8)
        mask_rows.append(bytes(bits))
    return header + b"".join(reversed(rows)) + b"".join(reversed(mask_rows))


def write_ico(images, path):
    blobs = []
    for image in images:
        if image.size[0] >= 256:
            buffer = io.BytesIO()
            image.save(buffer, "PNG", optimize=True)
            blobs.append(buffer.getvalue())
        else:
            blobs.append(bitmap_entry(image))
    offset = 6 + 16 * len(images)
    directory = b""
    for image, blob in zip(images, blobs):
        w, h = image.size
        directory += struct.pack("<BBBBHHII", w % 256, h % 256, 0, 0, 1, 32, len(blob), offset)
        offset += len(blob)
    with open(path, "wb") as file:
        file.write(struct.pack("<HHH", 0, 1, len(images)) + directory + b"".join(blobs))


def write_preview(images, path):
    """Every size at 1:1 on dark and light backgrounds, plus 16-48 px enlarged 6x."""
    zoomed = [image for image in images if image.size[0] <= 48]
    width = max(sum(i.size[0] + 24 for i in images) + 24, sum(i.size[0] * 6 + 24 for i in zoomed) + 24)
    height = 2 * (256 + 48) + 2 * (48 * 6 + 48)
    sheet = Image.new("RGBA", (width, height), rgba("#FFFFFF"))
    y = 0
    for background in ("#202020", "#F3F3F3"):
        band = Image.new("RGBA", (width, 256 + 48), rgba(background))
        x = 24
        for image in images:
            band.alpha_composite(image, (x, 24 + 256 - image.size[1]))
            x += image.size[0] + 24
        sheet.alpha_composite(band, (0, y))
        y += 256 + 48
    for background in ("#202020", "#F3F3F3"):
        band = Image.new("RGBA", (width, 48 * 6 + 48), rgba(background))
        x = 24
        for image in zoomed:
            big = image.resize((image.size[0] * 6, image.size[1] * 6), Image.Resampling.NEAREST)
            band.alpha_composite(big, (x, 24 + 48 * 6 - big.size[1]))
            x += big.size[0] + 24
        sheet.alpha_composite(band, (0, y))
        y += 48 * 6 + 48
    sheet.save(path)


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--ico", required=True, help="output .ico path")
    parser.add_argument("--preview", help="optional PNG sheet showing every size")
    args = parser.parse_args()
    images = [render(size) for size in SIZES]
    write_ico(images, args.ico)
    if args.preview:
        write_preview(images, args.preview)
    print(f"wrote {args.ico} ({', '.join(str(s) for s in SIZES)} px)")


if __name__ == "__main__":
    main()
