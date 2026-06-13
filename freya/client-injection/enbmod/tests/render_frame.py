# SPDX-License-Identifier: MIT
# Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
# License: LICENSES/Freya
#
# render_frame.py -- rasterize a dumped overlay frame (JSON from
# mock_enb.dump_frame) into a PNG, approximating overlay.cpp's draw semantics:
# vertical per-pixel gradients, triangle-fan rounded corners (rendered here as
# true rounded rects), alpha blending over a dark "space" background. The
# output is for VISUAL verification of mod UIs without launching the client.
#
# Usage: render_frame.py <frame.json> <out.png>

import json
import sys

from PIL import Image, ImageDraw, ImageFont


def rgb_tuple(rgb):
    return ((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF)


def load_font(size=13):
    for path in (
        "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
        "/usr/share/fonts/TTF/DejaVuSans.ttf",
    ):
        try:
            return ImageFont.truetype(path, size)
        except OSError:
            continue
    return ImageFont.load_default()


def gradient_layer(w, h, top, bot, alpha):
    """Vertical per-row lerp, like overlay.cpp's per-vertex gradient quads."""
    layer = Image.new("RGBA", (max(w, 1), max(h, 1)))
    px = layer.load()
    t, b = rgb_tuple(top), rgb_tuple(bot)
    for y in range(layer.height):
        f = y / max(layer.height - 1, 1)
        row = tuple(round(t[i] + (b[i] - t[i]) * f) for i in range(3)) + (alpha,)
        for x in range(layer.width):
            px[x, y] = row
    return layer


def rounded_mask(w, h, radius):
    mask = Image.new("L", (max(w, 1), max(h, 1)), 0)
    ImageDraw.Draw(mask).rounded_rectangle(
        [0, 0, mask.width - 1, mask.height - 1], radius=radius, fill=255)
    return mask


def render(frame, out_path):
    w, h = frame["w"], frame["h"]
    base = Image.new("RGBA", (w, h))
    # dark space-like backdrop with a subtle vertical falloff so alpha is visible
    bg = gradient_layer(w, h, 0x0A0E18, 0x02030A, 255)
    base.paste(bg, (0, 0))
    font = load_font()

    for c in frame["cmds"]:
        kind = c["kind"]
        if kind == "text":
            ov = Image.new("RGBA", (w, h), (0, 0, 0, 0))
            ImageDraw.Draw(ov).text((c["x"], c["y"]), c["text"],
                                    font=font, fill=rgb_tuple(c["rgb"]) + (255,))
            base = Image.alpha_composite(base, ov)
        elif kind == "rect":
            ov = Image.new("RGBA", (w, h), (0, 0, 0, 0))
            d = ImageDraw.Draw(ov)
            box = [c["x"], c["y"], c["x"] + c["w"] - 1, c["y"] + c["h"] - 1]
            col = rgb_tuple(c["rgb"]) + (c["alpha"],)
            if c.get("filled", True):
                d.rectangle(box, fill=col)
            else:
                d.rectangle(box, outline=col)
            base = Image.alpha_composite(base, ov)
        elif kind == "line":
            ov = Image.new("RGBA", (w, h), (0, 0, 0, 0))
            ImageDraw.Draw(ov).line([c["x0"], c["y0"], c["x1"], c["y1"]],
                                    fill=rgb_tuple(c["rgb"]) + (c["alpha"],))
            base = Image.alpha_composite(base, ov)
        elif kind in ("rect_grad", "rrect_grad"):
            layer = gradient_layer(c["w"], c["h"], c["rgb"], c["rgb2"], c["alpha"])
            if kind == "rrect_grad":
                grad_a = layer.getchannel("A")
                mask = rounded_mask(c["w"], c["h"], c["radius"])
                layer.putalpha(Image.composite(
                    grad_a, Image.new("L", mask.size, 0), mask))
            ov = Image.new("RGBA", (w, h), (0, 0, 0, 0))
            ov.paste(layer, (c["x"], c["y"]))
            base = Image.alpha_composite(base, ov)
        elif kind == "rrect":
            ov = Image.new("RGBA", (w, h), (0, 0, 0, 0))
            d = ImageDraw.Draw(ov)
            box = [c["x"], c["y"], c["x"] + c["w"] - 1, c["y"] + c["h"] - 1]
            col = rgb_tuple(c["rgb"]) + (c["alpha"],)
            if c.get("filled", True):
                d.rounded_rectangle(box, radius=c["radius"], fill=col)
            else:
                d.rounded_rectangle(box, radius=c["radius"], outline=col)
            base = Image.alpha_composite(base, ov)
        elif kind == "image":
            pass  # file-backed textures are not part of headless verification
        else:
            raise ValueError(f"unknown cmd kind: {kind}")

    base.convert("RGB").save(out_path)


def main():
    if len(sys.argv) != 3:
        sys.exit("usage: render_frame.py <frame.json> <out.png>")
    with open(sys.argv[1]) as f:
        frame = json.load(f)
    render(frame, sys.argv[2])
    print(f"rendered {sys.argv[2]} ({frame['w']}x{frame['h']}, "
          f"{len(frame['cmds'])} cmds)")


if __name__ == "__main__":
    main()
