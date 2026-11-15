"""Simulated players. The ground truth here is hidden from every model.

Each player picks French or Japanese (pick-a-language mode), plays 2 runs of 3 consoles, and retries
each console until it is solved (as in the game). What drives their answers:

  * a hidden belief per grammar concept: right or wrong (a misconception, e.g. adjective before noun),
    drawn from population rates that differ by concept, shifted by the player's skill `a` and by an
    "English transfer" trait `t` that makes word-order misconceptions co-occur;
  * a personal slip rate; slips mostly keep the right number of tiles, and "readers" (25%) slip by
    taking tiles in the order shown;
  * learning: after a rejection the player may drop the misconception it exposed (rate depends on
    skill); after a solve, the reveal explains the rule and fixes each misconception with p = 0.5.

The Bayesian beast is told the structure (beliefs -> grammar -> phrase, slips, learning) but not the
traits, the slip shape or the readers. The transformer is told nothing: it learns from the corpus.
"""
import math
import pickle
import sys
from multiprocessing import Pool

import numpy as np

from puzzles import load, realise

MU = {  # population misconception logits (hidden truth)
    "adjective-order": 0.6, "gender-agreement": 0.3, "plural-agreement": 0.0,
    "verb-final": 0.8, "particles": 0.2, "compound-meaning": 0.5,
}
ORDER = {"adjective-order", "verb-final", "particles", "compound-meaning"}
LANG_CONCEPTS = {
    "fr": ["adjective-order", "gender-agreement", "plural-agreement"],
    "ja": ["verb-final", "particles", "compound-meaning"],
}
MAX_ATTEMPTS = 8
RUNS, CONSOLES = 2, 3

_P = None
_BY_KIND = None


def sig(z):
    return 1 / (1 + math.exp(-z))


def _init(path):
    global _P, _BY_KIND
    _P = load(path)
    _BY_KIND = {}
    for p in _P:
        _BY_KIND.setdefault((p.lang, p.kind, p.tier), []).append(p.idx)


def pick_puzzle(rng, lang, target_tier):
    kinds = [k for k in _BY_KIND if k[0] == lang]
    best = max([k[2] for k in kinds if k[2] <= target_tier], default=min(k[2] for k in kinds))
    pool = [k for k in kinds if k[2] == best]
    k = pool[rng.integers(len(pool))]
    lst = _BY_KIND[k]
    return _P[lst[rng.integers(len(lst))]]


def play(pid, seed):
    rng = np.random.default_rng(seed)
    lang = ("fr", "ja")[rng.integers(2)]
    a, t = rng.normal(), rng.normal()
    eps = rng.beta(2, 18)
    reader = rng.random() < 0.25
    lam = sig(0.3 + 0.8 * a)
    wrong = {c for c in LANG_CONCEPTS[lang]
             if rng.random() < sig(MU[c] - 0.9 * a + (0.8 * t if c in ORDER else 0.0))}
    attempts = []
    for run in range(RUNS):
        for k in range(CONSOLES):
            p = pick_puzzle(rng, lang, min(3, k + run))
            rejected = []
            for n in range(MAX_ATTEMPTS):
                rej = set(rejected)
                slip = rng.random() < eps
                c = -1 if slip else realise(p, wrong, rej)
                if c < 0:
                    slip = True
                    free = np.array([i for i in range(p.n_cands) if i not in rej])
                    disp = [i for i in free if p.cand_disp[i] and p.cand_len[i] == p.ans_len]
                    if reader and disp:
                        c = disp[0]
                    elif rng.random() < 0.7:
                        same = free[p.cand_len[free] == p.ans_len]
                        c = int(rng.choice(same if len(same) else free))
                    else:
                        c = int(rng.choice(free))
                ok = bool(p.cand_accepted[c])
                attempts.append(dict(puzzle=p.idx, n=n, cand=int(c), correct=ok, rejected=list(rejected),
                                     slip=slip, truth=sorted(wrong & set(p.concepts))))
                if ok:
                    for cc in list(wrong & set(p.concepts)):
                        if rng.random() < 0.5:
                            wrong.discard(cc)
                    break
                rejected.append(int(c))
                h = p.cand_hyp[c]
                if not slip and h >= 0:
                    exposed = sorted(p.hyp_wrong[h] & wrong)
                    if exposed and rng.random() < lam:
                        wrong.discard(exposed[rng.integers(len(exposed))])
    return dict(pid=pid, lang=lang, attempts=attempts)


def simulate(path, n, seed0, procs=16):
    with Pool(procs, initializer=_init, initargs=(path,)) as pool:
        return pool.starmap(play, [(i, seed0 + i) for i in range(n)], chunksize=64)


if __name__ == "__main__":
    n, seed0, out = int(sys.argv[1]), int(sys.argv[2]), sys.argv[3]
    players = simulate("data/puzzles.jsonl", n, seed0)
    pickle.dump(players, open(out, "wb"))
    na = sum(len(p["attempts"]) for p in players)
    acc = np.mean([a["correct"] for p in players for a in p["attempts"]])
    first = np.mean([a["correct"] for p in players for a in p["attempts"] if a["n"] == 0])
    print(f"{n} players, {na} attempts ({na / n:.1f}/player), {acc:.1%} of attempts correct, {first:.1%} first tries correct")
