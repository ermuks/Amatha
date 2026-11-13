import sys
from PIL import Image, ImageFilter

# 사용법: make_icon.py 원본.png 출력.ico 미리보기.png [작은아이콘.png]
src, out_ico, preview = sys.argv[1], sys.argv[2], sys.argv[3]
small_src = sys.argv[4] if len(sys.argv) > 4 else None
full = Image.open(src).convert("RGBA")


def square_around(img, box):
    l, t, r, b = box
    side = max(r - l, b - t)
    cx, cy = (l + r) / 2, (t + b) / 2
    box = (round(cx - side / 2), round(cy - side / 2), round(cx + side / 2), round(cy + side / 2))
    out = Image.new("RGBA", (box[2] - box[0], box[3] - box[1]), (0, 0, 0, 0))
    out.paste(img.crop(box), (0, 0))  # 원본 밖은 투명
    return out.resize((1024, 1024), Image.LANCZOS)


# 큰 아이콘: 캐릭터 전체 (투명 여백만 잘라 냄)
l, t, r, b = full.split()[3].getbbox()
pad = round(max(r - l, b - t) * 0.03)
large = square_around(full, (l - pad, t - pad, r + pad, b + pad))

# 작은 아이콘: 얼굴 클로즈업 (왕관·올린 손·눈·입, 보내 주신 시안과 같은 구도)
if small_src:
    small = Image.open(small_src).convert("RGBA")
    small = small.resize((1024, 1024), Image.LANCZOS)
else:
    small = square_around(full, (86, -18, 408, 304))  # 아래 팔·몸통 선(y≥308)은 빼고


def thicken(img, radius):
    """작은 크기에서 선이 사라지지 않도록 검은 선을 굵게."""
    if radius <= 0:
        return img
    k = radius * 2 + 1
    rgb = img.convert("RGB").filter(ImageFilter.MinFilter(k))
    alpha = img.split()[3].filter(ImageFilter.MaxFilter(k))
    out = rgb.convert("RGBA")
    out.putalpha(alpha)
    return out


# (원본 그림, 1024 기준 선 굵히기 반경)
plan = {
    16: (small, 4), 20: (small, 4), 24: (small, 3), 32: (small, 2), 40: (small, 1), 48: (small, 0),
    64: (large, 4), 128: (large, 2), 256: (large, 0),
}
frames = {s: thicken(img, rad).resize((s, s), Image.LANCZOS) for s, (img, rad) in plan.items()}
sizes = sorted(frames)
frames[256].save(out_ico, format="ICO", sizes=[(s, s) for s in sizes],
                 append_images=[frames[s] for s in sizes if s != 256])

# 미리보기: 1행 실제 크기(밝은/어두운 바탕), 2행 확대
pad_cell = 300
sheet = Image.new("RGBA", (sum(max(s, 64) + 24 for s in sizes) + 24, 380), (255, 255, 255, 255))
x = 24
for s in sizes:
    for i, bg in enumerate([(241, 240, 236, 255), (32, 32, 32, 255)]):
        tile = Image.new("RGBA", (s + 8, s + 8), bg)
        tile.alpha_composite(frames[s], (4, 4))
        sheet.paste(tile, (x, 12 + i * (s + 14) if s <= 64 else 12 + i * 140))
    zoom = max(1, 128 // s)
    big = frames[s].resize((s * zoom, s * zoom), Image.NEAREST) if s < 128 else frames[s].resize((128, 128), Image.LANCZOS)
    tile = Image.new("RGBA", (big.width, big.height), (32, 32, 32, 255))
    tile.alpha_composite(big)
    sheet.paste(tile, (x, 236))
    x += max(s, 64) + 24
sheet.save(preview)
print("ok", sizes)
