"""Randomised player worlds for training a prior-fitted beast, plus held-out test worlds.

A world is a full description of how a population of players thinks and slips. Training draws a
fresh random world for every single player, so the network never sees the same population twice and
has to work out what kind of player it is facing from that player's answers alone.

Test worlds are held out in two ways: random worlds from the training ranges (in-distribution), and
"regime" worlds that sit outside the training ranges or add behaviours never seen in training.
"""
import math

import numpy as np

from puzzles import realise
from sim import LANG_CONCEPTS, MAX_ATTEMPTS, MU, ORDER, RUNS, CONSOLES

ALL_CONCEPTS = sorted(MU)


def sig(z):
    return 1 / (1 + math.exp(-z))


def sample_world(rng):
    """Training prior over worlds."""
    return dict(
        mu={c: rng.normal(0.3, 0.9) for c in ALL_CONCEPTS},
        wa=rng.uniform(0.2, 1.5), wt=rng.uniform(0.0, 1.2),
        eps_mean=rng.uniform(0.02, 0.25), eps_conc=rng.uniform(5, 40),
        reader=rng.uniform(0, 0.5), same_len=rng.uniform(0.4, 0.95),
        lam0=rng.uniform(-1.0, 1.5), lam_a=rng.uniform(0, 1.5), rev=rng.uniform(0.1, 0.85),
        never=rng.uniform(0, 0.3), shift=rng.uniform(0, 0.3), shift_mult=rng.uniform(1.5, 3.0),
        swap=0.0, forget=0.0,
    )


# the original sim.py population, written as a world
ORIGINAL = dict(mu=dict(MU), wa=0.9, wt=0.8, eps_mean=0.1, eps_conc=20, reader=0.25, same_len=0.7,
                lam0=0.3, lam_a=0.8, rev=0.5, never=0.0, shift=0.0, shift_mult=1.0, swap=0.0, forget=0.0)


def regimes():
    """Held-out worlds: each changes the original world in one way the training prior never covers."""
    out = {"original sim": dict(ORIGINAL)}
    out["fast learners"] = dict(ORIGINAL, lam0=3.0, rev=0.97)
    out["heavy guessers"] = dict(ORIGINAL, eps_mean=0.4)
    out["swap slips (new behaviour)"] = dict(ORIGINAL, swap=0.8)
    out["forgetting between runs (new behaviour)"] = dict(ORIGINAL, forget=0.7)
    out["everyone confused"] = dict(ORIGINAL, mu={c: 2.5 for c in MU})
    return out


def play(P, by_kind, seed, world=None):
    """One player. With world=None, a fresh random training world is drawn from the seed."""
    rng = np.random.default_rng(seed)
    W = world if world is not None else sample_world(rng)
    lang = ("fr", "ja")[rng.integers(2)]
    a, t = rng.normal(), rng.normal()
    m, k = W["eps_mean"], W["eps_conc"]
    eps = rng.beta(m * k, (1 - m) * k)
    reader = rng.random() < W["reader"]
    never = rng.random() < W["never"]
    shifted = rng.random() < W["shift"]
    lam = 0.0 if never else sig(W["lam0"] + W["lam_a"] * a)
    rev = 0.0 if never else W["rev"]
    start = {c for c in LANG_CONCEPTS[lang]
             if rng.random() < sig(W["mu"][c] - W["wa"] * a + (W["wt"] * t if c in ORDER else 0.0))}
    wrong = set(start)
    attempts = []
    for run in range(RUNS):
        if run > 0 and W["forget"] > 0:
            for c in start:
                if rng.random() < W["forget"]:
                    wrong.add(c)
        e = min(0.9, eps * W["shift_mult"]) if (shifted and run > 0) else eps
        for kk in range(CONSOLES):
            p = pick(P, by_kind, rng, lang, min(3, kk + run))
            rejected = []
            for n in range(MAX_ATTEMPTS):
                rej = set(rejected)
                plan = realise(p, wrong, rej)
                slip = plan < 0 or rng.random() < e
                c = plan
                if slip:
                    c = -1
                    if W["swap"] > 0 and plan >= 0 and p.cand_len[plan] >= 2 and rng.random() < W["swap"]:
                        sq = list(p.cand_seq[plan])
                        i = rng.integers(len(sq) - 1)
                        sq[i], sq[i + 1] = sq[i + 1], sq[i]
                        c2 = p.seq_index[tuple(sq)]
                        if c2 not in rej:
                            c = c2
                    if c < 0:
                        free = np.array([i for i in range(p.n_cands) if i not in rej])
                        disp = [i for i in free if p.cand_disp[i] and p.cand_len[i] == p.ans_len]
                        if reader and disp:
                            c = int(disp[0])
                        elif rng.random() < W["same_len"]:
                            same = free[p.cand_len[free] == p.ans_len]
                            c = int(rng.choice(same if len(same) else free))
                        else:
                            c = int(rng.choice(free))
                ok = bool(p.cand_accepted[c])
                attempts.append(dict(puzzle=p.idx, n=n, cand=int(c), correct=ok, rejected=list(rejected)))
                if ok:
                    for cc in list(wrong & set(p.concepts)):
                        if rng.random() < rev:
                            wrong.discard(cc)
                    break
                rejected.append(int(c))
                h = p.cand_hyp[c]
                if not slip and h >= 0:
                    exposed = sorted(p.hyp_wrong[h] & wrong)
                    if exposed and rng.random() < lam:
                        wrong.discard(exposed[rng.integers(len(exposed))])
    return dict(pid=seed, lang=lang, attempts=attempts)


def kinds_index(P):
    by = {}
    for p in P:
        by.setdefault((p.lang, p.kind, p.tier), []).append(p.idx)
    return by


def pick(P, by_kind, rng, lang, target_tier):
    kinds = [k for k in by_kind if k[0] == lang]
    best = max([k[2] for k in kinds if k[2] <= target_tier], default=min(k[2] for k in kinds))
    pool = [k for k in kinds if k[2] == best]
    k = pool[rng.integers(len(pool))]
    lst = by_kind[k]
    return P[lst[rng.integers(len(lst))]]
