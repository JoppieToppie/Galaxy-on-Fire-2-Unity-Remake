#!/usr/bin/env python3
"""Enhanced GoF2 skies (Options > Graphics "Skies" Enhanced / Combined, Settings.EnhancedSkies; 2026-11-14, the user's request).

Makes Assets/Resources/GoF2SkyHd from the baked base skies in Assets/Resources/GoF2Sky (SkyboxBaker's 6:1 strips, 2048 per
face, Unity's horizontal-strip order +X -X +Y -Y +Z -Z):

* the stars: every point star of stars_00X and every speck of nebula_0XX (dust / faint stars, a pixel or two wide) is lifted
  out into a star list (<name>.bytes, StarList.cs reads it) that GoF2/SpaceSky draws as sharp points at any resolution
  (SpaceSkyCore.hlsl StarGrid); what stays (the big stars' halos, the clouds) is the layer's image.
* the clouds: each nebula face, its specks taken out, is shrunk to 512 (the art's real detail: a 4x down/up loses 0.5 % of
  its variance), padded with its neighbours (so the upscale sees across the cube's edges and the faces meet without a seam),
  upscaled 4x by Real-ESRGAN's anime model (realesrgan-x4plus-anime: crisp painted edges; the user picked it over the
  general model on nebula 005 / 008 / 016), trimmed back to 2048, and brought back to the original's colours at low
  frequency (the model shifts the tone a little).

Chorus's sky (M_Skybox_MASTER) places up to four images as patches; GoF2's nebulae don't fit that: even the patchy ones
cover 15 - 60 % of every cube face, so the clouds stay cube faces, the same size as the original's.

Needs realesrgan-ncnn-vulkan (Arch: AUR realesrgan-ncnn-vulkan-bin) and numpy, scipy, Pillow. Heavy (the GPU, ~3 GB of
RAM): run it in its own capped scope:

    systemd-run --user --scope -p MemoryMax=8G -p MemorySwapMax=1G python3 -I Reference/tools/sky/enhance_skies.py

    --only nebula_005 stars_000   just these layers
    --keep DIR                    keep the upscaler's input / output images in DIR
"""
import argparse
import os
import shutil
import struct
import subprocess
import sys
import tempfile

import numpy as np
from PIL import Image
from scipy import ndimage as nd

Image.MAX_IMAGE_PIXELS = None
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..', '..'))
SRC = os.path.join(ROOT, 'Assets', 'Resources', 'GoF2Sky')
OUT = os.path.join(ROOT, 'Assets', 'Resources', 'GoF2SkyHd')
N = 2048           # face size
SMALL = 512        # what the upscaler is fed (x4 -> N)
PAD = 32           # its padding at SMALL, each side (128 px of the neighbours at N)
MODEL = 'realesrgan-x4plus-anime'
MAGIC = b'GSL1'


# ---- the cube: Unity's strip faces as GoF2/SpaceSky's StripUV reads them (s right, t up as seen from inside) ----------

def face_dirs(face, s, t):
    one = np.ones_like(s)
    return {
        0: (one, t, -s), 1: (-one, t, s), 2: (s, one, -t),
        3: (s, -one, t), 4: (s, t, one), 5: (-s, t, -one),
    }[face]


def dir_to_face(x, y, z):
    """The face and its (s, t) in [-1, 1] for directions (arrays), as StripUV."""
    ax, ay, az = np.abs(x), np.abs(y), np.abs(z)
    face = np.where((ax >= ay) & (ax >= az), np.where(x > 0, 0, 1),
                    np.where(ay >= az, np.where(y > 0, 2, 3), np.where(z > 0, 4, 5)))
    s = np.empty_like(x); t = np.empty_like(x)
    for f, (ss, tt, a) in {0: (-z, y, ax), 1: (z, y, ax), 2: (x, -z, ay), 3: (x, z, ay), 4: (x, y, az), 5: (-x, y, az)}.items():
        m = face == f
        s[m] = ss[m] / a[m]; t[m] = tt[m] / a[m]
    return face, s, t


def pixel_st(n):
    c = (np.arange(n) + 0.5) / n * 2 - 1
    s, t = np.meshgrid(c, -c)   # image rows run down: t up
    return s, t


def sample_cube(faces, x, y, z):
    """Bilinear samples of the cube 'faces' (6 x N x N x C, float) toward the directions, each from its own face."""
    face, s, t = dir_to_face(x, y, z)
    n = faces.shape[1]
    px = np.clip((s + 1) * 0.5 * n - 0.5, 0, n - 1)
    py = np.clip((1 - t) * 0.5 * n - 0.5, 0, n - 1)
    x0 = np.floor(px).astype(int); y0 = np.floor(py).astype(int)
    x1 = np.minimum(x0 + 1, n - 1); y1 = np.minimum(y0 + 1, n - 1)
    fx = (px - x0)[..., None]; fy = (py - y0)[..., None]
    a = faces[face, y0, x0]; b = faces[face, y0, x1]; c = faces[face, y1, x0]; d = faces[face, y1, x1]
    return (a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy


def padded_face(faces, f, n, pad):
    """Face f at n px plus 'pad' px of its neighbours on each side (the same pixel spacing, straight through the edges)."""
    m = n + 2 * pad
    c = ((np.arange(m) - pad) + 0.5) / n * 2 - 1
    s, t = np.meshgrid(c, -c)
    x, y, z = face_dirs(f, s, t)
    return sample_cube(faces, x, y, z)


# ---- sRGB ------------------------------------------------------------------------------------------------------------

def to_linear(u8):
    v = u8.astype(np.float32) / 255
    return np.where(v <= 0.04045, v / 12.92, ((v + 0.055) / 1.055) ** 2.4)


def to_srgb(lin):
    v = np.clip(lin, 0, 1)
    v = np.where(v <= 0.0031308, v * 12.92, 1.055 * np.power(v, 1 / 2.4) - 0.055)
    return np.clip(np.round(v * 255), 0, 255).astype(np.uint8)


def load_strip(name):
    a = np.asarray(Image.open(os.path.join(SRC, name + '.png')).convert('RGB'))
    return np.stack([a[:, i * N:(i + 1) * N] for i in range(6)])   # 6 x N x N x 3 (u8)


def save_strip(faces_u8, path):
    Image.fromarray(np.concatenate(list(faces_u8), axis=1)).save(path, optimize=True)


# ---- the stars -------------------------------------------------------------------------------------------------------

CORE = 3           # the core's radius (px at N) taken out and fitted
RING = (4, 6)      # the ring its background is read from

_yy, _xx = np.mgrid[-RING[1]:RING[1] + 1, -RING[1]:RING[1] + 1]
_rr = np.hypot(_xx, _yy)
CORE_MASK = _rr <= CORE + 0.5
RING_MASK = (_rr >= RING[0]) & (_rr <= RING[1] + 0.5)
EDGE_MASK = (_rr >= 2) & (_rr <= CORE + 0.5)


def extract(face_u8, threshold, point_share, grain=8):
    """The point stars of one face (u8 N x N x 3): a list of (x, y, sigma px, amplitude rgb linear) and the face with them
    taken out (linear). A star: a 5 x 5 local maximum standing 'threshold' (u8 luminance) and 'grain' times its 15 x 15
    neighbourhood's own grain (the median of |the excess|) over its 9 x 9 median, whose light falls off fast (its excess
    2 - 3 px out under 'point_share' of its peak's): the halos and the clouds' knots and grain stay. (A fixed threshold
    either missed half of nebula 016's specks (18) or took nebula 005's cloud grain for stars (10: 751 points on two faces
    for 61); the grain test keeps 016's and drops 005's to its few faint knots.)"""
    lin = to_linear(face_u8)
    L = face_u8.astype(np.float32).mean(2)
    excess = L - nd.median_filter(L, size=9, mode='reflect')
    noise = nd.median_filter(np.abs(excess), size=15, mode='reflect')
    peaks = (L == nd.maximum_filter(L, size=5, mode='reflect')) & (excess > np.maximum(threshold, grain * noise))
    ys, xs = np.nonzero(peaks)
    order = np.argsort(-excess[ys, xs])   # brightest first: a dimmer peak inside a brighter star's core is skipped
    out = lin.copy()
    taken = np.zeros(L.shape, bool)
    stars = []
    R = RING[1]
    for i in order:
        y, x = ys[i], xs[i]
        if y < R or x < R or y >= N - R or x >= N - R or taken[y, x]:
            continue   # (the faces' outer 6 px: their stars stay in the image, drawn as the original's)
        win = lin[y - R:y + R + 1, x - R:x + R + 1]
        bg = np.median(win[RING_MASK], axis=0)
        ex = win - bg
        lum = ex.mean(2)
        peak = lum[R, R]
        if peak <= 0 or lum[EDGE_MASK].mean() > point_share * peak:
            continue
        w = np.clip(lum, 0, None) * CORE_MASK
        tot = w.sum()
        cx = (w * _xx).sum() / tot; cy = (w * _yy).sum() / tot
        var = (w * ((_xx - cx) ** 2 + (_yy - cy) ** 2)).sum() / tot / 2
        sigma = float(np.clip(np.sqrt(var), 0.35, 1.6))
        amp = np.clip(ex[R, R] * np.exp(((cx ** 2 + cy ** 2) / (2 * sigma ** 2))), 0, None)   # at the centre, not the pixel
        stars.append((x + cx, y + cy, sigma, amp))
        out[y - R:y + R + 1, x - R:x + R + 1][CORE_MASK] = bg
        taken[y - CORE:y + CORE + 1, x - CORE:x + CORE + 1] = True
    return stars, out


def star_records(face, stars):
    """(x, y) px of 'face' -> the unit direction, sigma -> radians (the cube's pixels shrink toward the corners)."""
    recs = []
    for x, y, sigma, amp in stars:
        s = (x + 0.5) / N * 2 - 1
        t = 1 - (y + 0.5) / N * 2
        d = np.array(face_dirs(face, np.array(s), np.array(t)), dtype=np.float64)
        q = 1 + s * s + t * t
        recs.append((*(d / np.sqrt(q)), sigma * (2 / N) / q, *amp))
    return recs


def write_list(path, recs, layer):
    with open(path, 'wb') as f:
        f.write(MAGIC + struct.pack('<ii', len(recs), layer))
        for r in recs:
            f.write(struct.pack('<7f', *r))


# ---- the clouds ------------------------------------------------------------------------------------------------------

def shrink(img, k):
    h, w, c = img.shape
    return img.reshape(h // k, k, w // k, k, c).mean((1, 3))


def upscale(esrgan, models, src_dir, dst_dir):
    cmd = [esrgan, '-i', src_dir, '-o', dst_dir, '-n', MODEL, '-s', '4', '-f', 'png']
    if models:
        cmd += ['-m', models]
    subprocess.run(cmd, check=True, stdout=subprocess.DEVNULL)


def match_tone(up, ref, sigma=24):
    """'up' (linear) brought to 'ref''s colours at low frequency, its fine detail kept."""
    lo_up = np.stack([nd.gaussian_filter(up[..., c], sigma) for c in range(3)], -1)
    lo_ref = np.stack([nd.gaussian_filter(ref[..., c], sigma) for c in range(3)], -1)
    gain = np.clip((lo_ref + 1e-4) / (lo_up + 1e-4), 0.5, 2.0)
    return up * gain


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('--only', nargs='*')
    ap.add_argument('--keep')
    ap.add_argument('--esrgan', default=shutil.which('realesrgan-ncnn-vulkan'))
    ap.add_argument('--models', default='/usr/share/realesrgan-ncnn-vulkan/models')
    a = ap.parse_args()
    names = [f'stars_{i:03d}' for i in range(3)] + [f'nebula_{i:03d}' for i in range(19)]
    if a.only:
        names = [n for n in names if n in a.only]
    os.makedirs(OUT, exist_ok=True)
    work = a.keep or tempfile.mkdtemp(prefix='gof2sky_')
    src_dir = os.path.join(work, 'in'); dst_dir = os.path.join(work, 'out')
    os.makedirs(src_dir, exist_ok=True); os.makedirs(dst_dir, exist_ok=True)

    clean = {}
    for name in names:
        faces = load_strip(name)
        nebula = name.startswith('nebula')
        recs, lin = [], []
        for f in range(6):
            stars, out = extract(faces[f], threshold=10, point_share=0.35)
            recs += star_records(f, stars)
            lin.append(out)
        write_list(os.path.join(OUT, name + '.bytes'), recs, 1 if nebula else 0)
        lin = np.stack(lin)
        print(f'{name}: {len(recs)} stars', flush=True)
        if not nebula:
            save_strip(to_srgb(lin), os.path.join(OUT, name + '.png'))   # the halos
            continue
        clean[name] = lin
        k = N // SMALL
        for f in range(6):
            big = padded_face(lin, f, N, PAD * k)
            Image.fromarray(to_srgb(shrink(big, k))).save(os.path.join(src_dir, f'{name}_{f}.png'))

    if clean:
        if not a.esrgan:
            sys.exit('realesrgan-ncnn-vulkan not found (--esrgan)')
        upscale(a.esrgan, a.models, src_dir, dst_dir)
        for name, lin in clean.items():
            out = []
            for f in range(6):
                up = to_linear(np.asarray(Image.open(os.path.join(dst_dir, f'{name}_{f}.png')).convert('RGB')))
                p = PAD * 4
                up = up[p:p + N, p:p + N]
                out.append(to_srgb(match_tone(up, lin[f])))
            save_strip(np.stack(out), os.path.join(OUT, name + '.png'))
            print(f'{name}: upscaled', flush=True)
    if not a.keep:
        shutil.rmtree(work)


if __name__ == '__main__':
    main()
