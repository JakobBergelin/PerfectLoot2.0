"""Turn Blender sprite renders into a pixel-art set and a painted set.
Usage: python make_sprites.py <render_dir> <out_dir>
"""
import sys, os, random, math
from PIL import Image, ImageFilter, ImageChops, ImageEnhance, ImageOps
import numpy as np

SRC, OUT = sys.argv[1], sys.argv[2]
PIX_PER_M = 22          # pixel set density (world pixels per meter)
PAINT_RES = 384         # painted set frame size (px)

# dark medieval palette (32 colors)
PALETTE_HEX = """0d0b0e 1c1619 2b2226 3b2f31 4d3f3e 62524d 7b6a60 978373 b7a28b d7c7a6 efe3c6
2a1a14 45261b 64321f 86401f a8521e 3a1210 5c1a16 82231b a8321f
4a1c16 6a281e 8a3628 a8483a c25a44
c8501c e8761e ffa53a ffd36a fff3b8
1c2a2c 24403f 2f5b58 4f8a86 9ad2cf
33363d 555a63""".split()
PALETTE = np.array([[int(h[i:i+2], 16) for i in (0, 2, 4)] for h in PALETTE_HEX], dtype=np.float32)
OUTLINE = (13, 11, 14, 255)
ELITE = (232, 118, 30, 255)

def read_meta(name):
    meta = {}
    for line in open(os.path.join(SRC, name)):
        line = line.strip()
        if not line: continue
        n, s, u, v = line.split('|')
        meta[n] = (float(s), float(u), float(v))
    return meta

def quantize(img):
    a = np.asarray(img).astype(np.float32)
    rgb = a[..., :3]
    w = np.array([0.35, 0.45, 0.2], dtype=np.float32)
    d = (((rgb[:, :, None, :] - PALETTE[None, None]) ** 2) * w).sum(-1)
    idx = d.argmin(-1)
    out = a.copy(); out[..., :3] = PALETTE[idx]
    out[..., 3] = np.where(a[..., 3] > 110, 255, 0)
    return Image.fromarray(out.astype(np.uint8), 'RGBA')

def outline(img, color, width=1):
    a = np.asarray(img).copy()
    alpha = a[..., 3] > 0
    grow = alpha.copy()
    for _ in range(width):
        g = grow.copy()
        g[1:, :] |= grow[:-1, :]; g[:-1, :] |= grow[1:, :]; g[:, 1:] |= grow[:, :-1]; g[:, :-1] |= grow[:, 1:]
        grow = g
    edge = grow & ~alpha
    a[edge] = color
    return Image.fromarray(a, 'RGBA')

def grade(img, sat=0.85, contrast=1.15, bright=0.95):
    rgb, alpha = img.convert('RGB'), img.getchannel('A')
    rgb = ImageEnhance.Color(rgb).enhance(sat)
    rgb = ImageEnhance.Contrast(rgb).enhance(contrast)
    rgb = ImageEnhance.Brightness(rgb).enhance(bright)
    out = rgb.convert('RGBA'); out.putalpha(alpha); return out

def pixel_version(img, size):
    img = grade(img, 0.9, 1.25, 0.95)
    small = img.resize((size, size), Image.BOX)
    q = quantize(small)
    return q

def painted_version(img, seed):
    img = grade(img, 0.8, 1.2, 0.92)
    img = img.resize((PAINT_RES, PAINT_RES), Image.LANCZOS)
    alpha = img.getchannel('A').point(lambda v: 255 if v > 100 else 0)
    rgb = img.convert('RGB')
    # flatten into paint blotches
    rgb = rgb.filter(ImageFilter.ModeFilter(9)).filter(ImageFilter.ModeFilter(5))
    rgb = rgb.quantize(colors=28, method=Image.Quantize.MEDIANCUT).convert('RGB')
    rgb = rgb.filter(ImageFilter.SMOOTH_MORE)
    # darken edges (ink lines inside forms)
    edges = rgb.convert('L').filter(ImageFilter.FIND_EDGES).point(lambda v: 255 if v > 28 else 0).filter(ImageFilter.MaxFilter(3))
    ink = Image.new('RGB', rgb.size, (24, 18, 20))
    rgb = Image.composite(ink, rgb, edges.point(lambda v: int(v * 0.75)))
    # canvas grain
    rnd = np.random.RandomState(seed)
    grain = (rnd.normal(0, 9, (PAINT_RES, PAINT_RES, 1))).astype(np.float32)
    arr = np.clip(np.asarray(rgb).astype(np.float32) + grain, 0, 255).astype(np.uint8)
    rgb = Image.fromarray(arr, 'RGB')
    out = rgb.convert('RGBA'); out.putalpha(alpha)
    return outline(out, (20, 14, 16, 255), width=3)

def make_ground_pixel(path, size=64, seed=1):
    rnd = random.Random(seed)
    base = [PALETTE[i] for i in (2, 3, 3, 4, 4, 4, 5)]
    img = Image.new('RGB', (size, size))
    px = img.load()
    for y in range(size):
        for x in range(size):
            n = (math.sin(x * 0.35 + seed) + math.cos(y * 0.29 + x * 0.1) + rnd.uniform(-1.2, 1.2))
            c = base[int(max(0, min(len(base) - 1, (n + 2.2) / 4.4 * len(base))))]
            px[x, y] = tuple(int(v) for v in c)
    for _ in range(5):  # cracks
        x, y = rnd.randrange(size), rnd.randrange(size)
        for _ in range(rnd.randint(6, 14)):
            px[x % size, y % size] = tuple(int(v) for v in PALETTE[1])
            x += rnd.choice((-1, 0, 1)); y += rnd.choice((0, 1))
    for _ in range(18):  # pebbles / embers
        x, y = rnd.randrange(size), rnd.randrange(size)
        px[x, y] = tuple(int(v) for v in (PALETTE[7] if rnd.random() < 0.8 else PALETTE[20]))
    img.save(path)

def make_ground_painted(path, size=1024, seed=2):
    rnd = np.random.RandomState(seed)
    def noise(scale):
        small = rnd.rand(max(2, size // scale), max(2, size // scale)).astype(np.float32)
        return np.asarray(Image.fromarray((small * 255).astype(np.uint8)).resize((size, size), Image.BICUBIC)).astype(np.float32) / 255
    n = noise(128) * 0.5 + noise(48) * 0.3 + noise(12) * 0.2
    c0 = np.array([46, 38, 40], np.float32); c1 = np.array([96, 84, 82], np.float32); c2 = np.array([70, 44, 34], np.float32)
    t = n[..., None]
    rgb = c0 * (1 - t) + c1 * t
    warm = noise(200)[..., None]
    rgb = rgb * (1 - 0.35 * warm) + c2 * 0.35 * warm
    img = Image.fromarray(np.clip(rgb, 0, 255).astype(np.uint8), 'RGB')
    img = img.filter(ImageFilter.ModeFilter(9)).filter(ImageFilter.SMOOTH_MORE)
    grain = rnd.normal(0, 7, (size, size, 1))
    img = Image.fromarray(np.clip(np.asarray(img).astype(np.float32) + grain, 0, 255).astype(np.uint8))
    img.save(path)

def main():
    chars, props = read_meta('meta_chars.txt'), read_meta('meta_props.txt')
    for style in ('Pixel', 'Painted'):
        os.makedirs(os.path.join(OUT, style), exist_ok=True)
    lines = {'Pixel': [], 'Painted': []}
    allm = dict(chars); allm.update(props)
    for i, (name, (scale, u, v)) in enumerate(sorted(allm.items())):
        src = Image.open(os.path.join(SRC, name + '.png')).convert('RGBA')
        size = max(16, int(round(scale * PIX_PER_M)))
        pix = pixel_version(src, size)
        if not name.endswith('_top'):
            pix = outline(pix, OUTLINE, 1)
        pix.save(os.path.join(OUT, 'Pixel', name + '.png'))
        lines['Pixel'].append(f"{name}|{size}|{scale}|{u}|{v}")
        pnt = painted_version(src, i) if not name.endswith('_top') else grade(src, 0.85, 1.2, 0.95).resize((PAINT_RES, PAINT_RES), Image.LANCZOS)
        pnt.save(os.path.join(OUT, 'Painted', name + '.png'))
        lines['Painted'].append(f"{name}|{PAINT_RES}|{scale}|{u}|{v}")
        if name.startswith('DM_Baron_'):  # elite variants: trait-colored outline
            outline(pixel_version(src, size), ELITE, 1).save(os.path.join(OUT, 'Pixel', name.replace('DM_Baron', 'DM_BaronElite') + '.png'))
            lines['Pixel'].append(f"{name.replace('DM_Baron', 'DM_BaronElite')}|{size}|{scale}|{u}|{v}")
            pe = painted_version(src, i)
            pe = outline(pe, ELITE, 3)
            pe.save(os.path.join(OUT, 'Painted', name.replace('DM_Baron', 'DM_BaronElite') + '.png'))
            lines['Painted'].append(f"{name.replace('DM_Baron', 'DM_BaronElite')}|{PAINT_RES}|{scale}|{u}|{v}")
    make_ground_pixel(os.path.join(OUT, 'Pixel', 'Ground.png'))
    make_ground_painted(os.path.join(OUT, 'Painted', 'Ground.png'))
    for style in lines:
        with open(os.path.join(OUT, style, 'sprites.txt'), 'w') as f:
            f.write("# name|frame px|world size (m)|pivot u|pivot v\n" + "\n".join(lines[style]))

main()
