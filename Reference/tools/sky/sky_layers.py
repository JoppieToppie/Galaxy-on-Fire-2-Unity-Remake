#!/usr/bin/env python3
"""The layered GoF2 skies' assets (Options > Graphics "Skies": Enhanced, EVERSPACE 2 style; 2026-11-14, the user: "combine es2s
methods with chorus methods for the skies", "es2 shouldn't be required for this. recreate it"). Everything is made here from
noise and GoF2's own skies, nothing of EVERSPACE 2's or Chorus's: their methods, the remake's content.

Into Assets/Resources/GoF2SkyLayers (named "gof2:<name>" by a sky's "chorus" block, World.GoF2SkyLayers):

* star_mask.png: a tileable 4096 x 4096 star mask, as EVERSPACE 2's sky reads its own (M_Skybox_Stars): three star layers in
  its channels, r the few bright ones, g the middle ones, b the many faint ones, each a small gaussian (the sky clusters them
  by a noise, SpaceSkyCore's ChorusStarMask).
* nebula_masks.png: tileable 2048 x 1024 greyscale nebula masks (EVERSPACE 2's are greyscale masks the material tints): r
  billowing clouds, g dark-laned filaments, b broad haze; domain-warped spectral noise.
* patch_0XX.png: Chorus's way with painted skies: the brightest part of each GoF2 nebula sky again as its own image patch,
  sharper (Resources/GoF2SkyHd's nebula, upscaled 4x again from its 1024 shrink by Real-ESRGAN's anime model, sampled into
  the patch's own projection, as SpaceSkyCore's ChorusLayer reads it), feathered at its edge.
* layered_skies.json: per GoF2 nebula sky 0-18: its bright regions (direction in the baked cube's frame, size, colour,
  brightness), the plane its nebulae lie in (a band's normal), its main colour and how much of the sky it covers, and its
  patch (direction, angular size), from which World.GoF2SkyLayers builds both looks.

Heavy (Real-ESRGAN on the GPU, ~3 GB of RAM; its work files on disk, not /tmp): run it in its own capped scope:

    systemd-run --user --scope -p MemoryMax=8G -p MemorySwapMax=1G python3 -I Reference/tools/sky/sky_layers.py [--no-patches]
"""
import argparse
import json
import math
import os
import shutil
import subprocess
import sys

import numpy as np
from PIL import Image
from scipy import ndimage as nd

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import enhance_skies as es   # noqa: E402  (the cube's faces and sampling, the sRGB curves)

Image.MAX_IMAGE_PIXELS = None
ROOT = es.ROOT
OUT = os.path.join(ROOT, 'Assets', 'Resources', 'GoF2SkyLayers')
HD = os.path.join(ROOT, 'Assets', 'Resources', 'GoF2SkyHd')
WORK = os.path.join(os.path.expanduser('~'), '.cache', 'gof2-sky-layers')
PATCH_PX = 1536
PATCH_MAX_DEG = 40.0


# ---- the masks ---------------------------------------------------------------------------------------------------------

def spectral(rng, h, w, beta, cut=0.1, stretch=1.0):
    fy = np.fft.fftfreq(h)[:, None] * stretch
    fx = np.fft.rfftfreq(w)[None, :]
    f = np.sqrt(fx * fx + fy * fy)
    f[0, 0] = 1.0
    amp = f ** (-beta / 2)
    amp[0, 0] = 0.0
    amp[f > cut] = 0.0
    n = np.fft.irfft2(amp * np.exp(1j * rng.uniform(0, 2 * np.pi, amp.shape)), s=(h, w))
    return (n - n.mean()) / n.std()


def norm(a, lo=1, hi=99.5):
    a0, a1 = np.percentile(a, [lo, hi])
    return np.clip((a - a0) / (a1 - a0), 0, 1)


def warp(field, dx, dy):
    """'field' read at (x + dx, y + dy), wrapping (so it stays tileable)."""
    h, w = field.shape
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    return nd.map_coordinates(field, [(yy + dy) % h, (xx + dx) % w], order=1, mode='grid-wrap')


def nebula_masks():
    rng = np.random.default_rng(1411)
    h, w = 1024, 2048
    wx, wy = spectral(rng, h, w, 3.4, 0.05) * 60, spectral(rng, h, w, 3.4, 0.05) * 60
    billow = warp(np.abs(spectral(rng, h, w, 2.8)) ** 0.6 + 0.8 * spectral(rng, h, w, 3.2), wx, wy)
    billow = norm(billow) ** 1.8
    ridge = warp(1.0 - np.abs(spectral(rng, h, w, 3.0)), wx * 1.5, wy * 1.5)
    ridge = norm(ridge) ** 3.5 * norm(spectral(rng, h, w, 3.6) + 0.6)
    haze = norm(spectral(rng, h, w, 3.8, 0.03) + 0.3 * spectral(rng, h, w, 3.0)) ** 1.4
    return np.round(np.stack([norm(billow), norm(ridge), norm(haze)], -1) * 255).astype(np.uint8)


def star_mask():
    rng = np.random.default_rng(2611)
    n = 4096
    img = np.zeros((n, n, 3), np.float32)
    for ch, count, sig, amp in ((0, 1800, (0.8, 1.2), (0.6, 1.0)), (1, 7000, (0.6, 0.85), (0.4, 0.8)), (2, 32000, (0.5, 0.65), (0.2, 0.6))):
        x = rng.uniform(0, n, count); y = rng.uniform(0, n, count)
        s = rng.uniform(*sig, count); a = rng.uniform(*amp, count) ** 2
        xi = np.floor(x).astype(int); yi = np.floor(y).astype(int)
        for dy in range(-3, 4):
            for dx in range(-3, 4):
                px, py = xi + dx, yi + dy
                g = a * np.exp(-((px + 0.5 - x) ** 2 + (py + 0.5 - y) ** 2) / (2 * s * s))
                np.add.at(img[..., ch], (py % n, px % n), g)
    return np.round(np.clip(img, 0, 1) * 255).astype(np.uint8)


# ---- each GoF2 sky's layout ----------------------------------------------------------------------------------------------

def equirect(n_w=512):
    """Unit directions of an equirect grid (u round the sky from +Z toward +X, v up), and each cell's solid angle."""
    n_h = n_w // 2
    lon = ((np.arange(n_w) + 0.5) / n_w - 0.5) * 2 * np.pi
    lat = (0.5 - (np.arange(n_h) + 0.5) / n_h) * np.pi
    LAT, LON = np.meshgrid(lat, lon, indexing='ij')
    d = np.stack([np.cos(LAT) * np.sin(LON), np.sin(LAT), np.cos(LAT) * np.cos(LON)], -1)
    return d, np.cos(LAT)


def two_hues(px, w):
    """A region's two main colours (k-means of its pixels by chromaticity, weighted by light), the heavier first, each
    normalised to its brightest channel: the average of a brown and teal region is a beige that saturates to yellow."""
    if len(px) < 8 or w.sum() <= 0:
        return [[1.0, 1.0, 1.0], [1.0, 1.0, 1.0]]
    chroma = px / np.maximum(px.sum(-1, keepdims=True), 1e-6)
    rng = np.random.default_rng(3)
    p = w / w.sum()
    centres = chroma[rng.choice(len(chroma), 2, replace=False, p=p)]
    for _ in range(30):
        lab = np.argmin(((chroma[:, None, :] - centres[None]) ** 2).sum(-1), 1)
        for j in range(2):
            m = lab == j
            if m.any(): centres[j] = (chroma[m] * w[m, None]).sum(0) / w[m].sum()
    weights = [w[lab == j].sum() for j in range(2)]
    cols = []
    for j in np.argsort(weights)[::-1]:
        m = lab == j
        c = (px[m] * w[m, None]).sum(0) / max(w[m].sum(), 1e-9) if m.any() else centres[j]
        cols.append([round(float(v), 4) for v in c / max(c.max(), 1e-6)])
    return cols


def layout(index):
    faces = es.to_linear(es.load_strip(f'nebula_{index:03d}'))   # the original bake (its colours, its own specks are tiny)
    d, area = equirect()
    rgb = es.sample_cube(faces, d[..., 0], d[..., 1], d[..., 2])
    lum = rgb @ np.array([0.2126, 0.7152, 0.0722])
    smooth = nd.gaussian_filter(lum, 6, mode=['nearest', 'wrap'])
    coverage = float((area * (lum > 0.01)).sum() / area.sum())
    w = lum * area
    main = (rgb * w[..., None]).sum((0, 1)) / max(w.sum(), 1e-9)
    # the plane the nebulae lie in: the bright directions' least spread axis
    pts = d.reshape(-1, 3); ww = w.reshape(-1)
    cov = (pts * ww[:, None]).T @ pts / max(ww.sum(), 1e-9)
    evals, evecs = np.linalg.eigh(cov)
    normal = evecs[:, 0]
    planarity = float(1.0 - evals[0] / max(evals[1], 1e-9))
    regions = []
    work = smooth.copy()
    first = None
    for _ in range(4):
        iy, ix = np.unravel_index(np.argmax(work), work.shape)
        peak = work[iy, ix]
        if first is None: first = peak
        if peak < 0.25 * first or peak <= 1e-4: break
        c = d[iy, ix]
        ang = np.arccos(np.clip(d @ c, -1, 1))
        near = ang < math.radians(45)
        wt = smooth * near * area
        # its size: where it falls to half the peak, on average round it
        r = float(np.sqrt((wt * ang ** 2).sum() / max(wt.sum(), 1e-9)))
        col = (rgb * (wt * (ang < r * 1.5))[..., None]).sum((0, 1)) / max((wt * (ang < r * 1.5)).sum(), 1e-9)
        hues = two_hues(rgb[ang < r * 1.5], (wt * (ang < r * 1.5))[ang < r * 1.5])
        regions.append({'dir': [round(float(v), 5) for v in c], 'radius': round(math.degrees(r), 2),
                        'color': [round(float(v), 4) for v in col / max(col.max(), 1e-6)], 'brightness': round(float(peak), 5),
                        'colorA': hues[0], 'colorB': hues[1]})   # (flat: Unity's JsonUtility reads no nested lists)
        work *= 1.0 - np.exp(-(ang / max(r * 1.2, 0.05)) ** 2)
    return {'coverage': round(coverage, 4), 'main': [round(float(v), 4) for v in main / max(main.max(), 1e-6)],
            'brightness': round(float(np.percentile(lum[lum > 0.01], 90)) if (lum > 0.01).any() else 0.0, 5),
            'bandNormal': [round(float(v), 5) for v in normal], 'planarity': round(planarity, 3), 'regions': regions}


# ---- the patches (Chorus's way) ------------------------------------------------------------------------------------------

def patch_angles(c):
    """The layer's heading / elevation (turns, Chorus's units) whose patch is centred on the cube-frame direction c: from
    SpaceSkyCore's ChorusLayer, the patch's middle is q = (0, -1, 0), the view w = (cos e sin h, cos e cos h, -sin e)."""
    e = -math.asin(max(-1.0, min(1.0, c[2])))
    h = math.atan2(c[0], c[1])
    return h / (2 * math.pi), -e / (math.pi / 2)


def patch_dirs(c, scale, n):
    """Cube-frame directions of a patch's n x n pixels (rotation 0), inverting ChorusLayer's mapping."""
    heading, elevation = patch_angles(c)
    h = heading * 2 * math.pi; e = -elevation * 0.5 * math.pi
    sh, ch, se, ce = math.sin(h), math.cos(h), math.sin(e), math.cos(e)
    s = (np.arange(n) + 0.5) / n - 0.5
    rx, ry = np.meshgrid(s, s[::-1])                 # the image's rows run down: r.y up
    u = (rx * scale + 1.0) * 0.5
    v = ry * scale + 0.5
    phi = (2 * u - 1) * np.pi; th = v * np.pi
    q = np.stack([np.sin(th) * np.sin(phi), -np.sin(th) * np.cos(phi), np.cos(th)], -1)
    px = q[..., 1] * ce + q[..., 2] * se
    py = -q[..., 1] * se + q[..., 2] * ce
    pz = q[..., 0]
    dx = py; dy = px * sh + pz * ch; dz = px * ch - pz * sh   # the sphere's inward normal (Unreal axes)
    vx, vy, vz = -dx, -dy, -dz                                 # the view (Unreal: x forward, y right, z up)
    return np.stack([vy, vz, vx], -1)                          # Unity: (y, z, x)


def hires_faces(index, esrgan, models):
    """GoF2SkyHd's nebula 'index' at 4096 a face: its 2048 faces shrunk to 1024, padded with their neighbours, upscaled 4x."""
    src = os.path.join(WORK, 'in'); dst = os.path.join(WORK, 'out')
    shutil.rmtree(WORK, ignore_errors=True)
    os.makedirs(src); os.makedirs(dst)
    a = np.asarray(Image.open(os.path.join(HD, f'nebula_{index:03d}.png')).convert('RGB'))
    lin = es.to_linear(np.stack([a[:, i * 2048:(i + 1) * 2048] for i in range(6)]))
    hires_faces.ref = lin
    pad = 32
    for f in range(6):
        big = es.padded_face(lin, f, 2048, pad * 2)
        Image.fromarray(es.to_srgb(es.shrink(big, 2))).save(os.path.join(src, f'{f}.png'))
    cmd = [esrgan, '-i', src, '-o', dst, '-n', es.MODEL, '-s', '4', '-f', 'png'] + (['-m', models] if models else [])
    subprocess.run(cmd, check=True, stdout=subprocess.DEVNULL)
    out = []
    for f in range(6):
        up = np.asarray(Image.open(os.path.join(dst, f'{f}.png')).convert('RGB'))
        out.append(up[pad * 4:pad * 4 + 4096, pad * 4:pad * 4 + 4096])
    return np.stack(out)


def make_patch(faces_u8, ref_lin, c, radius_deg):
    """The patch, brought back to 'ref_lin''s (GoF2SkyHd's, the sky under it) colours at low frequency: the second upscale
    lifts the highlights (the first pass's patches came out whiter than the sky round them)."""
    span = min(PATCH_MAX_DEG, max(15.0, radius_deg * 2.5))
    scale = span / 180.0
    dirs = patch_dirs(c, scale, PATCH_PX)
    lin = es.sample_cube(es.to_linear(faces_u8), dirs[..., 0], dirs[..., 1], dirs[..., 2])
    ref = es.sample_cube(ref_lin, dirs[..., 0], dirs[..., 1], dirs[..., 2])
    rgb = es.to_srgb(es.match_tone(lin, ref, sigma=24)).astype(np.float32)
    s = (np.arange(PATCH_PX) + 0.5) / PATCH_PX - 0.5
    rr = np.sqrt(s[None, :] ** 2 + s[:, None] ** 2) * 2           # 0 middle, 1 edge midpoints
    alpha = np.clip((1.0 - rr) / 0.35, 0, 1) ** 1.5                  # feathered over its outer third
    img = np.dstack([np.clip(rgb, 0, 255), alpha * 255]).round().astype(np.uint8)
    return img, span


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('--no-patches', action='store_true')
    ap.add_argument('--only', nargs='*', type=int)
    ap.add_argument('--esrgan', default=shutil.which('realesrgan-ncnn-vulkan'))
    ap.add_argument('--models', default='/usr/share/realesrgan-ncnn-vulkan/models')
    a = ap.parse_args()
    os.makedirs(OUT, exist_ok=True)
    if not a.only:
        Image.fromarray(star_mask()).save(os.path.join(OUT, 'star_mask.png'), optimize=True)
        Image.fromarray(nebula_masks()).save(os.path.join(OUT, 'nebula_masks.png'), optimize=True)
        print('masks', flush=True)
    path = os.path.join(OUT, 'layered_skies.json')
    data = json.load(open(path)) if os.path.exists(path) else {'skies': [None] * 19}
    for i in (a.only or range(19)):
        sky = layout(i)
        if not a.no_patches and sky['regions']:
            if not a.esrgan: sys.exit('realesrgan-ncnn-vulkan not found (--esrgan)')
            faces = hires_faces(i, a.esrgan, a.models)
            top = sky['regions'][0]
            img, span = make_patch(faces, hires_faces.ref, top['dir'], top['radius'])
            Image.fromarray(img, 'RGBA').save(os.path.join(OUT, f'patch_{i:03d}.png'), optimize=True)
            heading, elevation = patch_angles(top['dir'])
            sky['patch'] = {'image': f'patch_{i:03d}', 'dir': top['dir'], 'span': round(span, 2),
                            'heading': round(heading, 6), 'elevation': round(elevation, 6)}
        elif data['skies'][i] and data['skies'][i].get('patch'):
            sky['patch'] = data['skies'][i]['patch']
        data['skies'][i] = sky
        print(i, len(sky['regions']), 'regions', sky.get('patch', {}).get('span'), flush=True)
    with open(path, 'w') as f:
        json.dump(data, f, indent=1)
    shutil.rmtree(WORK, ignore_errors=True)


if __name__ == '__main__':
    main()
