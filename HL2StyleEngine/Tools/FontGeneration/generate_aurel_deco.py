from __future__ import annotations

import math
import struct
import time
from dataclasses import dataclass
from pathlib import Path


UNITS_PER_EM = 1000
ASCENT = 850
DESCENT = -220
TOP = 735
MID = 365
BASE = 0
STROKE = 66
THIN = 46


@dataclass
class Glyph:
    name: str
    advance: int
    contours: list[list[tuple[int, int]]]

    @property
    def bounds(self) -> tuple[int, int, int, int]:
        points = [point for contour in self.contours for point in contour]
        if not points:
            return 0, 0, 0, 0
        xs = [point[0] for point in points]
        ys = [point[1] for point in points]
        return min(xs), min(ys), max(xs), max(ys)


def u16(value: int) -> bytes:
    return struct.pack(">H", value & 0xFFFF)


def i16(value: int) -> bytes:
    return struct.pack(">h", int(value))


def u32(value: int) -> bytes:
    return struct.pack(">I", value & 0xFFFFFFFF)


def pad4(data: bytes) -> bytes:
    return data + (b"\0" * ((4 - len(data) % 4) % 4))


def checksum(data: bytes) -> int:
    padded = pad4(data)
    total = 0
    for index in range(0, len(padded), 4):
        total = (total + struct.unpack(">I", padded[index:index + 4])[0]) & 0xFFFFFFFF
    return total


def area(points: list[tuple[int, int]]) -> float:
    total = 0
    for index, (x0, y0) in enumerate(points):
        x1, y1 = points[(index + 1) % len(points)]
        total += x0 * y1 - x1 * y0
    return total * 0.5


def orient(points: list[tuple[int, int]], clockwise: bool) -> list[tuple[int, int]]:
    is_clockwise = area(points) < 0
    if is_clockwise != clockwise:
        return list(reversed(points))
    return points


def rect(x0: int, y0: int, x1: int, y1: int) -> list[tuple[int, int]]:
    return orient([(x0, y0), (x0, y1), (x1, y1), (x1, y0)], clockwise=True)


def poly(points: list[tuple[int, int]], clockwise: bool = True) -> list[tuple[int, int]]:
    return orient([(round(x), round(y)) for x, y in points], clockwise)


def stroke(x0: float, y0: float, x1: float, y1: float, width: float = STROKE) -> list[tuple[int, int]]:
    dx = x1 - x0
    dy = y1 - y0
    length = math.hypot(dx, dy)
    if length <= 0:
        return rect(round(x0 - width / 2), round(y0 - width / 2), round(x0 + width / 2), round(y0 + width / 2))

    px = -dy / length * width / 2
    py = dx / length * width / 2
    return poly([
        (x0 + px, y0 + py),
        (x1 + px, y1 + py),
        (x1 - px, y1 - py),
        (x0 - px, y0 - py),
    ])


def oct_ring(cx: int, cy: int, rx: int, ry: int, thickness: int) -> list[list[tuple[int, int]]]:
    outer = []
    inner = []
    for step in range(8):
        angle = math.tau * step / 8 + math.tau / 16
        outer.append((round(cx + math.cos(angle) * rx), round(cy + math.sin(angle) * ry)))
    for step in range(7, -1, -1):
        angle = math.tau * step / 8 + math.tau / 16
        inner.append((round(cx + math.cos(angle) * (rx - thickness)), round(cy + math.sin(angle) * (ry - thickness))))
    return [orient(outer, clockwise=True), orient(inner, clockwise=False)]


def scale_contours(contours: list[list[tuple[int, int]]], sx: float, sy: float, tx: float = 0, ty: float = 0) -> list[list[tuple[int, int]]]:
    return [
        [(round(x * sx + tx), round(y * sy + ty)) for x, y in contour]
        for contour in contours
    ]


def l(width: int = 560) -> int:
    return 62


def r(width: int = 560) -> int:
    return width - 62


def c(width: int = 560) -> int:
    return width // 2


def cap_shape(ch: str, width: int = 560) -> list[list[tuple[int, int]]]:
    left = l(width)
    right = r(width)
    center = c(width)
    top = TOP
    mid = MID
    base = BASE
    low = 70

    match ch:
        case "A":
            return [
                stroke(left, base, center, top),
                stroke(right, base, center, top),
                rect(left + 100, 305, right - 100, 355),
            ]
        case "B":
            return [
                rect(left, base, left + STROKE, top),
                rect(left, top - STROKE, right - 45, top),
                rect(left, mid - THIN // 2, right - 55, mid + THIN // 2),
                rect(left, base, right - 45, base + STROKE),
                rect(right - STROKE, mid + 10, right, top - 45),
                rect(right - STROKE, base + 45, right, mid - 10),
            ]
        case "C":
            return [
                rect(left + 30, top - STROKE, right, top),
                rect(left, base + STROKE, left + STROKE, top - STROKE),
                rect(left + 30, base, right, base + STROKE),
                rect(right - 75, top - STROKE, right, top - 8),
                rect(right - 75, base + 8, right, base + STROKE),
            ]
        case "D":
            return [
                rect(left, base, left + STROKE, top),
                rect(left, top - STROKE, right - 70, top),
                rect(left, base, right - 70, base + STROKE),
                rect(right - STROKE, base + 70, right, top - 70),
            ]
        case "E":
            return [
                rect(left, base, left + STROKE, top),
                rect(left, top - STROKE, right, top),
                rect(left, mid - THIN // 2, right - 70, mid + THIN // 2),
                rect(left, base, right, base + STROKE),
            ]
        case "F":
            return [
                rect(left, base, left + STROKE, top),
                rect(left, top - STROKE, right, top),
                rect(left, mid - THIN // 2, right - 90, mid + THIN // 2),
            ]
        case "G":
            return [
                rect(left + 30, top - STROKE, right, top),
                rect(left, base + STROKE, left + STROKE, top - STROKE),
                rect(left + 30, base, right, base + STROKE),
                rect(right - STROKE, base + 55, right, mid + 30),
                rect(center, mid - THIN // 2, right, mid + THIN // 2),
            ]
        case "H":
            return [
                rect(left, base, left + STROKE, top),
                rect(right - STROKE, base, right, top),
                rect(left, mid - THIN // 2, right, mid + THIN // 2),
            ]
        case "I":
            return [
                rect(left, top - STROKE, right, top),
                rect(center - THIN // 2, base, center + THIN // 2, top),
                rect(left, base, right, base + STROKE),
            ]
        case "J":
            return [
                rect(left, top - STROKE, right, top),
                rect(right - STROKE, low, right, top),
                rect(left + 55, base, right - 35, base + STROKE),
                rect(left, low, left + STROKE, low + 140),
            ]
        case "K":
            return [
                rect(left, base, left + STROKE, top),
                stroke(left + 40, mid, right, top, STROKE),
                stroke(left + 40, mid, right, base, STROKE),
            ]
        case "L":
            return [
                rect(left, base, left + STROKE, top),
                rect(left, base, right, base + STROKE),
            ]
        case "M":
            return [
                rect(left, base, left + STROKE, top),
                rect(right - STROKE, base, right, top),
                stroke(left + STROKE, top, center, mid + 70, THIN),
                stroke(right - STROKE, top, center, mid + 70, THIN),
            ]
        case "N":
            return [
                rect(left, base, left + STROKE, top),
                rect(right - STROKE, base, right, top),
                stroke(left + 42, top - 10, right - 42, base + 10, THIN),
            ]
        case "O":
            return oct_ring(center, 365, right - left, 370, STROKE)
        case "P":
            return [
                rect(left, base, left + STROKE, top),
                rect(left, top - STROKE, right - 45, top),
                rect(left, mid - THIN // 2, right - 55, mid + THIN // 2),
                rect(right - STROKE, mid + 10, right, top - 45),
            ]
        case "Q":
            return oct_ring(center, 365, right - left, 370, STROKE) + [stroke(center + 75, 120, right + 42, -45, THIN)]
        case "R":
            return cap_shape("P", width) + [stroke(left + 72, mid - 5, right, base, STROKE)]
        case "S":
            return [
                rect(left + 35, top - STROKE, right, top),
                rect(left, mid + 20, left + STROKE, top - STROKE),
                rect(left + 35, mid - THIN // 2, right - 35, mid + THIN // 2),
                rect(right - STROKE, base + STROKE, right, mid - 20),
                rect(left, base, right - 35, base + STROKE),
            ]
        case "T":
            return [
                rect(left, top - STROKE, right, top),
                rect(center - THIN // 2, base, center + THIN // 2, top),
            ]
        case "U":
            return [
                rect(left, low, left + STROKE, top),
                rect(right - STROKE, low, right, top),
                rect(left + 40, base, right - 40, base + STROKE),
            ]
        case "V":
            return [
                stroke(left, top, center, base, STROKE),
                stroke(right, top, center, base, STROKE),
            ]
        case "W":
            return [
                stroke(left, top, left + 105, base, THIN),
                stroke(left + 105, base, center, top - 180, THIN),
                stroke(center, top - 180, right - 105, base, THIN),
                stroke(right - 105, base, right, top, THIN),
            ]
        case "X":
            return [
                stroke(left, base, right, top, STROKE),
                stroke(left, top, right, base, STROKE),
            ]
        case "Y":
            return [
                stroke(left, top, center, mid, STROKE),
                stroke(right, top, center, mid, STROKE),
                rect(center - THIN // 2, base, center + THIN // 2, mid + 30),
            ]
        case "Z":
            return [
                rect(left, top - STROKE, right, top),
                stroke(right - 20, top - 30, left + 20, base + 30, STROKE),
                rect(left, base, right, base + STROKE),
            ]
        case _:
            return [rect(left, base, right, top), rect(left + 80, base + 90, right - 80, top - 90)]


def digit_shape(ch: str, width: int = 500) -> list[list[tuple[int, int]]]:
    left = 62
    right = width - 62
    center = width // 2
    top = TOP
    mid = MID
    base = BASE

    match ch:
        case "0":
            return oct_ring(center, 365, right - left, 370, STROKE)
        case "1":
            return [stroke(center - 80, top - 80, center, top, THIN), rect(center - THIN // 2, base, center + THIN // 2, top), rect(center - 110, base, center + 110, base + STROKE)]
        case "2":
            return [rect(left, top - STROKE, right, top), rect(right - STROKE, mid, right, top), rect(left, mid - THIN // 2, right, mid + THIN // 2), rect(left, base, left + STROKE, mid), rect(left, base, right, base + STROKE)]
        case "3":
            return [rect(left, top - STROKE, right, top), rect(left + 65, mid - THIN // 2, right, mid + THIN // 2), rect(left, base, right, base + STROKE), rect(right - STROKE, base + 40, right, top - 40)]
        case "4":
            return [rect(left, mid, left + STROKE, top), rect(right - STROKE, base, right, top), rect(left, mid - THIN // 2, right, mid + THIN // 2)]
        case "5":
            return [rect(left, top - STROKE, right, top), rect(left, mid, left + STROKE, top), rect(left, mid - THIN // 2, right, mid + THIN // 2), rect(right - STROKE, base, right, mid), rect(left, base, right, base + STROKE)]
        case "6":
            return [rect(left, base + 50, left + STROKE, top - 40), rect(left + 30, top - STROKE, right, top), rect(left, mid - THIN // 2, right, mid + THIN // 2), rect(right - STROKE, base + 40, right, mid), rect(left + 30, base, right - 35, base + STROKE)]
        case "7":
            return [rect(left, top - STROKE, right, top), stroke(right - 20, top - 30, center - 40, base, STROKE)]
        case "8":
            return oct_ring(center, 550, right - left, 185, THIN) + oct_ring(center, 180, right - left, 185, THIN)
        case "9":
            return [rect(right - STROKE, base + 40, right, top - 50), rect(left, top - STROKE, right - 30, top), rect(left, mid - THIN // 2, right, mid + THIN // 2), rect(left, mid, left + STROKE, top - 40), rect(left, base, right - 30, base + STROKE)]
        case _:
            return []


def punctuation_shape(ch: str) -> tuple[int, list[list[tuple[int, int]]]]:
    dot = rect(145, 0, 215, 70)
    match ch:
        case ".":
            return 320, [dot]
        case ",":
            return 320, [dot, stroke(180, 5, 120, -120, THIN)]
        case ":":
            return 320, [dot, rect(145, 470, 215, 540)]
        case ";":
            return 320, [rect(145, 470, 215, 540), dot, stroke(180, 5, 120, -120, THIN)]
        case "!":
            return 320, [rect(150, 165, 205, TOP), dot]
        case "?":
            return 430, [rect(70, TOP - STROKE, 360, TOP), rect(305, 430, 360, TOP - 40), stroke(330, 455, 210, 300, THIN), rect(188, 180, 242, 245), rect(188, 0, 242, 70)]
        case "-":
            return 360, [rect(60, 330, 300, 380)]
        case "_":
            return 420, [rect(40, -80, 380, -35)]
        case "/":
            return 360, [stroke(300, TOP, 60, BASE, THIN)]
        case "\\":
            return 360, [stroke(60, TOP, 300, BASE, THIN)]
        case "'":
            return 220, [rect(90, 520, 145, TOP)]
        case "\"":
            return 320, [rect(80, 520, 130, TOP), rect(190, 520, 240, TOP)]
        case "(":
            return 340, [stroke(250, TOP, 115, MID, THIN), stroke(115, MID, 250, BASE, THIN)]
        case ")":
            return 340, [stroke(90, TOP, 225, MID, THIN), stroke(225, MID, 90, BASE, THIN)]
        case "[":
            return 340, [rect(90, BASE, 145, TOP), rect(90, TOP - 50, 260, TOP), rect(90, BASE, 260, BASE + 50)]
        case "]":
            return 340, [rect(195, BASE, 250, TOP), rect(80, TOP - 50, 250, TOP), rect(80, BASE, 250, BASE + 50)]
        case "{":
            return 370, [stroke(275, TOP, 150, 480, THIN), stroke(150, 480, 245, MID, THIN), stroke(245, MID, 150, 250, THIN), stroke(150, 250, 275, BASE, THIN)]
        case "}":
            return 370, [stroke(95, TOP, 220, 480, THIN), stroke(220, 480, 125, MID, THIN), stroke(125, MID, 220, 250, THIN), stroke(220, 250, 95, BASE, THIN)]
        case "+":
            return 460, [rect(80, 330, 380, 380), rect(205, 200, 255, 510)]
        case "*":
            return 460, [stroke(230, 180, 230, 560, THIN), stroke(80, 270, 380, 470, THIN), stroke(80, 470, 380, 270, THIN)]
        case "=":
            return 460, [rect(80, 430, 380, 480), rect(80, 260, 380, 310)]
        case "<":
            return 440, [stroke(340, 560, 90, MID, THIN), stroke(90, MID, 340, 170, THIN)]
        case ">":
            return 440, [stroke(100, 560, 350, MID, THIN), stroke(350, MID, 100, 170, THIN)]
        case "|":
            return 260, [rect(110, -80, 160, TOP)]
        case "@":
            return 640, oct_ring(320, 350, 285, 330, THIN) + [rect(310, 220, 480, 465), rect(395, 170, 480, 245)]
        case "#":
            return 500, [stroke(175, 60, 230, TOP, THIN), stroke(300, 60, 355, TOP, THIN), rect(70, 245, 430, 295), rect(70, 460, 430, 510)]
        case "$":
            return 500, cap_shape("S", 500) + [rect(225, -60, 275, TOP + 60)]
        case "%":
            return 620, oct_ring(160, 540, 105, 115, 34) + oct_ring(460, 185, 105, 115, 34) + [stroke(500, TOP, 120, BASE, THIN)]
        case "&":
            return 580, oct_ring(250, 510, 150, 150, THIN) + [stroke(330, 410, 500, BASE, THIN), stroke(155, 300, 420, BASE, STROKE)]
        case _:
            return 500, []


def make_glyph_for_char(ch: str) -> Glyph:
    if ch == " ":
        return Glyph("space", 300, [])

    if "A" <= ch <= "Z":
        width = 620 if ch in "MWQ" else 560
        return Glyph(ch, width + 60, cap_shape(ch, width))

    if "a" <= ch <= "z":
        upper = ch.upper()
        width = 620 if upper in "MWQ" else 560
        contours = scale_contours(cap_shape(upper, width), 0.86, 0.78, tx=28, ty=0)
        return Glyph(ch, round(width * 0.88) + 70, contours)

    if "0" <= ch <= "9":
        return Glyph(ch, 560, digit_shape(ch))

    advance, contours = punctuation_shape(ch)
    return Glyph(f"uni{ord(ch):04X}", advance, contours)


def glyph_to_bytes(glyph: Glyph) -> bytes:
    if not glyph.contours:
        return struct.pack(">hhhhh", 0, 0, 0, 0, 0)

    x_min, y_min, x_max, y_max = glyph.bounds
    data = [struct.pack(">hhhhh", len(glyph.contours), x_min, y_min, x_max, y_max)]
    points: list[tuple[int, int]] = []
    end_points = []
    for contour in glyph.contours:
        points.extend(contour)
        end_points.append(len(points) - 1)

    data.append(b"".join(u16(point) for point in end_points))
    data.append(u16(0))
    data.append(bytes([1] * len(points)))

    last_x = 0
    for x, _ in points:
        data.append(i16(x - last_x))
        last_x = x

    last_y = 0
    for _, y in points:
        data.append(i16(y - last_y))
        last_y = y

    return b"".join(data)


def build_glyphs() -> tuple[list[Glyph], dict[int, int]]:
    chars = (
        [" "] +
        list("ABCDEFGHIJKLMNOPQRSTUVWXYZ") +
        list("abcdefghijklmnopqrstuvwxyz") +
        list("0123456789") +
        list(".,:;!?-_/\\'\"()[]{}+*=<>|@#$%&")
    )

    glyphs = [Glyph(".notdef", 600, [rect(80, 0, 520, 735), rect(150, 90, 450, 645)])]
    char_to_gid: dict[int, int] = {}
    seen: set[str] = set()
    for ch in chars:
        if ch in seen:
            continue
        seen.add(ch)
        char_to_gid[ord(ch)] = len(glyphs)
        glyphs.append(make_glyph_for_char(ch))

    return glyphs, char_to_gid


def make_glyf_and_loca(glyphs: list[Glyph]) -> tuple[bytes, bytes]:
    offsets = []
    chunks = []
    offset = 0
    for glyph in glyphs:
        offsets.append(offset)
        glyph_data = pad4(glyph_to_bytes(glyph))
        chunks.append(glyph_data)
        offset += len(glyph_data)
    offsets.append(offset)
    return b"".join(chunks), b"".join(u32(value) for value in offsets)


def make_hmtx(glyphs: list[Glyph]) -> bytes:
    data = []
    for glyph in glyphs:
        x_min, _, _, _ = glyph.bounds
        data.append(u16(glyph.advance))
        data.append(i16(x_min))
    return b"".join(data)


def make_head(glyphs: list[Glyph]) -> bytes:
    points = [point for glyph in glyphs for contour in glyph.contours for point in contour]
    x_min = min((x for x, _ in points), default=0)
    y_min = min((y for _, y in points), default=0)
    x_max = max((x for x, _ in points), default=0)
    y_max = max((y for _, y in points), default=0)
    timestamp = int(time.time()) + 2082844800
    return struct.pack(
        ">IIIIHHQQhhhhHHhhh",
        0x00010000,
        0x00010000,
        0,
        0x5F0F3CF5,
        0x000B,
        UNITS_PER_EM,
        timestamp,
        timestamp,
        x_min,
        y_min,
        x_max,
        y_max,
        0,
        8,
        2,
        1,
        0,
    )


def make_hhea(glyphs: list[Glyph]) -> bytes:
    adv_max = max(glyph.advance for glyph in glyphs)
    min_lsb = min((glyph.bounds[0] for glyph in glyphs), default=0)
    min_rsb = min((glyph.advance - glyph.bounds[2] for glyph in glyphs), default=0)
    x_max_extent = max((glyph.bounds[0] + (glyph.bounds[2] - glyph.bounds[0]) for glyph in glyphs), default=0)
    return (
        struct.pack(">I", 0x00010000) +
        struct.pack(">hhhH", ASCENT, DESCENT, 0, adv_max) +
        struct.pack(">hhhhhhhhhhhH", min_lsb, min_rsb, x_max_extent, 1, 0, 0, 0, 0, 0, 0, 0, len(glyphs))
    )


def make_maxp(glyphs: list[Glyph]) -> bytes:
    max_points = max((sum(len(contour) for contour in glyph.contours) for glyph in glyphs), default=0)
    max_contours = max((len(glyph.contours) for glyph in glyphs), default=0)
    values = [
        len(glyphs),
        max_points,
        max_contours,
        0,
        0,
        2,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
    ]
    return struct.pack(">I", 0x00010000) + b"".join(u16(value) for value in values)


def make_os2(glyphs: list[Glyph], char_to_gid: dict[int, int]) -> bytes:
    avg_width = round(sum(glyph.advance for glyph in glyphs) / len(glyphs))
    first = min(char_to_gid)
    last = max(char_to_gid)
    panose = bytes([2, 11, 5, 3, 3, 2, 2, 2, 2, 4])
    return b"".join([
        u16(0),
        i16(avg_width),
        u16(400),
        u16(3),
        u16(0),
        i16(650),
        i16(600),
        i16(0),
        i16(75),
        i16(650),
        i16(600),
        i16(0),
        i16(350),
        i16(50),
        i16(250),
        i16(0),
        panose,
        u32(0xE00002FF),
        u32(0),
        u32(0),
        u32(0),
        b"HS2 ",
        u16(0x0040),
        u16(first),
        u16(last),
        i16(ASCENT),
        i16(DESCENT),
        i16(0),
        u16(900),
        u16(250),
    ])


def make_cmap(char_to_gid: dict[int, int]) -> bytes:
    groups = b"".join(
        u32(codepoint) + u32(codepoint) + u32(gid)
        for codepoint, gid in sorted(char_to_gid.items())
    )
    subtable = struct.pack(">HHIII", 12, 0, 16 + len(groups), 0, len(char_to_gid)) + groups
    return struct.pack(">HHHHI", 0, 1, 3, 10, 12) + subtable


def make_name() -> bytes:
    names = {
        1: "Aurel Deco",
        2: "Regular",
        3: "Aurel Deco Regular 1.000",
        4: "Aurel Deco Regular",
        5: "Version 1.000",
        6: "AurelDeco-Regular",
        8: "HS2 Style Engine",
        9: "Generated by Codex for HS2 Style Engine",
        13: "Original prototype display font for project-local testing. Not affiliated with ITC Anna.",
    }
    storage = b""
    records = []
    for name_id, value in names.items():
        encoded = value.encode("utf-16-be")
        records.append(struct.pack(">HHHHHH", 3, 1, 0x0409, name_id, len(encoded), len(storage)))
        storage += encoded
    header = struct.pack(">HHH", 0, len(records), 6 + len(records) * 12)
    return header + b"".join(records) + storage


def make_post() -> bytes:
    return struct.pack(">IIhhIIIII", 0x00030000, 0, -75, 50, 0, 0, 0, 0, 0)


def build_ttf() -> bytes:
    glyphs, char_to_gid = build_glyphs()
    glyf, loca = make_glyf_and_loca(glyphs)
    tables = {
        "OS/2": make_os2(glyphs, char_to_gid),
        "cmap": make_cmap(char_to_gid),
        "glyf": glyf,
        "head": make_head(glyphs),
        "hhea": make_hhea(glyphs),
        "hmtx": make_hmtx(glyphs),
        "loca": loca,
        "maxp": make_maxp(glyphs),
        "name": make_name(),
        "post": make_post(),
    }

    tags = sorted(tables)
    num_tables = len(tags)
    max_power = 2 ** int(math.log2(num_tables))
    search_range = max_power * 16
    entry_selector = int(math.log2(max_power))
    range_shift = num_tables * 16 - search_range

    offset = 12 + num_tables * 16
    directory = []
    body = []
    table_offsets: dict[str, int] = {}
    for tag in tags:
        data = tables[tag]
        table_offsets[tag] = offset
        directory.append(tag.encode("ascii") + u32(checksum(data)) + u32(offset) + u32(len(data)))
        padded = pad4(data)
        body.append(padded)
        offset += len(padded)

    font = (
        struct.pack(">IHHHH", 0x00010000, num_tables, search_range, entry_selector, range_shift) +
        b"".join(directory) +
        b"".join(body)
    )

    adjustment = (0xB1B0AFBA - checksum(font)) & 0xFFFFFFFF
    head_adjustment_offset = table_offsets["head"] + 8
    font = font[:head_adjustment_offset] + u32(adjustment) + font[head_adjustment_offset + 4:]
    return font


def make_specimen_html() -> str:
    return """<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <title>Aurel Deco Specimen</title>
  <style>
    @font-face {
      font-family: "Aurel Deco";
      src: url("AurelDeco-Regular.ttf") format("truetype");
    }

    body {
      margin: 0;
      min-height: 100vh;
      background: #101515;
      color: #e9dfc9;
      font-family: "Aurel Deco", sans-serif;
      display: grid;
      place-items: center;
    }

    main {
      width: min(920px, calc(100vw - 64px));
    }

    h1 {
      margin: 0 0 28px;
      font-size: 76px;
      font-weight: 400;
      letter-spacing: 0;
    }

    p {
      margin: 14px 0;
      font-size: 34px;
      line-height: 1.25;
    }

    .small {
      color: #9fbaa8;
      font-size: 23px;
    }
  </style>
</head>
<body>
  <main>
    <h1>Aurel Deco</h1>
    <p>Inventory  Archive Key  Crank Handle</p>
    <p>Use  Examine  Combine  Split  Discard</p>
    <p>0123456789  E / X: Confirm</p>
    <p class="small">Original project font prototype. Tall, narrow, Deco UI lettering.</p>
  </main>
</body>
</html>
"""


def main() -> None:
    root = Path(__file__).resolve().parents[2]
    output_dir = root / "Game" / "Content" / "UI" / "Fonts"
    output_dir.mkdir(parents=True, exist_ok=True)

    font_path = output_dir / "AurelDeco-Regular.ttf"
    font_path.write_bytes(build_ttf())

    specimen_path = output_dir / "AurelDeco-Specimen.html"
    specimen_path.write_text(make_specimen_html(), encoding="utf-8")

    print(font_path)
    print(specimen_path)


if __name__ == "__main__":
    main()
