#!/usr/bin/env python3
"""The local nebula clouds' assets (Options > Graphics "Nebula clouds", World.NebulaClouds; 2026-11-14, the user: EVERSPACE 2's
sky methods in the base game, "this shouldnt require es2s files"): made here from nothing but noise and GoF2's own skies.

* Assets/Resources/GoF2SkyLayers/cloud_masks.png: a tileable 1024 x 1024 mask, three cloud characters in its channels (as
  EVERSPACE 2's local nebulae read one channel of a greyscale mask each, M_Nebula_Local_Parent): r billowy clumps, g ridged
  filaments, b stretched wisps. Spectral noise (random phases under a 1 / f^beta spectrum, inverse FFT), so it tiles exactly.
* Assets/Resources/GoF2SkyLayers/nebula_clouds.json: for each of GoF2's nebula skies 0-18 (Resources/GoF2Sky/nebula_0XX, the
  original bakes): how much of the sky its clouds cover, how bright they are, and their main colours (k-means of the bright
  pixels, weighted by light), so an orbit's clouds take its own sky's colours.

    python3 -I Reference/tools/sky/nebula_clouds.py        (numpy, Pillow; light: no scope needed)
"""
import json
import os

import numpy as np
from PIL import Image

Image.MAX_IMAGE_PIXELS = None
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..', '..'))
SKY = os.path.join(ROOT, 'Assets', 'Resources', 'GoF2Sky')
OUT = os.path.join(ROOT, 'Assets', 'Resources', 'GoF2SkyLayers')
SIZE = 1024


def spectral(rng, beta, stretch=1.0):
    """Tileable noise, 0..1: random phases under a 1 / f^beta amplitude spectrum ('stretch' > 1 makes it run along x)."""
    fy = np.fft.fftfreq(SIZE)[:, None] * stretch
    fx = np.fft.rfftfreq(SIZE)[None, :]
    f = np.sqrt(fx * fx + fy * fy)
    f[0, 0] = 1.0
    amp = f ** (-beta / 2)
    amp[0, 0] = 0.0
    amp[f > 0.08] = 0.0   # (no detail finer than 12 px: the mask is magnified a lot; finer grain read as dust beside
                          # EVERSPACE 2's smooth masks, coarser (20 px) as blobby contours through its crushing curve)
    phase = rng.uniform(0, 2 * np.pi, amp.shape)
    n = np.fft.irfft2(amp * np.exp(1j * phase), s=(SIZE, SIZE))
    n = (n - n.mean()) / n.std()
    return n


def norm(a):
    lo, hi = np.percentile(a, [1, 99.5])
    return np.clip((a - lo) / (hi - lo), 0, 1)


def masks():
    rng = np.random.default_rng(20261114)
    billow = norm(np.abs(spectral(rng, 2.6)) ** 0.7 + 0.6 * spectral(rng, 3.0))
    billow = billow ** 1.6
    ridge = 1.0 - np.abs(spectral(rng, 3.4))
    ridge = norm(ridge) ** 1.6 * norm(spectral(rng, 3.6) + 0.8)   # (^3 made lines that crumbled through ES2's crushing curve)
    wisp = norm(spectral(rng, 3.2, stretch=3.0) + 0.4 * spectral(rng, 3.4))
    wisp = wisp ** 2.0
    # smoothed (wrapping: still tileable): the billows' |noise| creases were the clouds' crumbly grain beside ES2's smooth masks
    from scipy import ndimage as nd
    img = np.stack([norm(nd.gaussian_filter(ch, 2.5, mode='wrap')) for ch in (billow, ridge, wisp)], -1)
    # each channel's values mapped onto EVERSPACE 2's local nebula masks' distribution (their 5 % quantiles, numbers only:
    # T_Skybox_Nebula_001's r, 002's r, 005's b, as the ES2 importer packs them): its material crushes the mask (Bias 0.05,
    # ^2), so only the top of it shows: ES2's have broad high plateaus, a wider distribution showed as crumbly specks
    for c in range(3):
        ranks = img[..., c].ravel().argsort().argsort() / (img[..., c].size - 1)
        img[..., c] = np.interp(ranks, np.linspace(0, 1, 21), ES2_QUANTILES[c]).reshape(img.shape[:2])
    return np.round(img * 255).astype(np.uint8)


ES2_QUANTILES = [
    [0.239, 0.314, 0.353, 0.373, 0.396, 0.42, 0.435, 0.451, 0.467, 0.486, 0.514, 0.533, 0.557, 0.584, 0.616, 0.647, 0.678, 0.71, 0.741, 0.816, 1.0],
    [0.271, 0.357, 0.396, 0.427, 0.451, 0.471, 0.486, 0.506, 0.522, 0.541, 0.561, 0.58, 0.604, 0.631, 0.647, 0.667, 0.69, 0.718, 0.753, 0.796, 1.0],
    [0.0, 0.129, 0.208, 0.259, 0.298, 0.322, 0.345, 0.365, 0.388, 0.408, 0.42, 0.443, 0.463, 0.486, 0.506, 0.533, 0.565, 0.6, 0.647, 0.722, 1.0],
]


def palette(name, k=3):
    a = np.asarray(Image.open(os.path.join(SKY, name + '.png')).convert('RGB').resize((6 * 256, 256), Image.BOX)).reshape(-1, 3)
    lin = (a / 255.0) ** 2.2
    lum = lin @ np.array([0.2126, 0.7152, 0.0722])
    bright = lum > 0.01
    coverage = float(bright.mean())
    if bright.sum() < 50:
        return {'coverage': coverage, 'brightness': 0.0, 'colors': []}
    px, w = lin[bright], lum[bright]
    rng = np.random.default_rng(1)
    centres = px[rng.choice(len(px), k, replace=False)]
    for _ in range(25):
        lab = np.argmin(((px[:, None, :] - centres[None]) ** 2).sum(-1), 1)
        for j in range(k):
            m = lab == j
            if m.any():
                centres[j] = (px[m] * w[m, None]).sum(0) / w[m].sum()
    weights = np.array([w[lab == j].sum() for j in range(k)])
    weights /= weights.sum()
    cols = []
    for c, wt in sorted(zip(centres, weights), key=lambda x: -x[1]):
        c = c / max(c.max(), 1e-6)
        cols.append({'r': round(float(c[0]), 4), 'g': round(float(c[1]), 4), 'b': round(float(c[2]), 4), 'weight': round(float(wt), 4)})
    return {'coverage': round(coverage, 4), 'brightness': round(float(np.percentile(lum[bright], 90)), 4), 'colors': cols}


def main():
    os.makedirs(OUT, exist_ok=True)
    Image.fromarray(masks()).save(os.path.join(OUT, 'cloud_masks.png'))
    data = {'nebulae': [palette(f'nebula_{i:03d}') for i in range(19)]}
    with open(os.path.join(OUT, 'nebula_clouds.json'), 'w') as f:
        json.dump(data, f, indent=1)
    for i, n in enumerate(data['nebulae']):
        print(i, n['coverage'], n['brightness'], [(c['r'], c['g'], c['b'], c['weight']) for c in n['colors']])


if __name__ == '__main__':
    main()
