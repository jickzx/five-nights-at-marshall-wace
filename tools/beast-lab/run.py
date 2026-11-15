"""Backtest: hand-set vs crowd prior vs Bayesian beast vs transformer, at several corpus sizes.

Every model is fitted only on training players and scored on 2,000 unseen test players, attempt by
attempt, seeing only that player's past. Writes results/results.json and prints a table.
"""
import json
import math
import os
import pickle
import time

import numpy as np
import torch

from models import Bayes, HandSet, evaluate, fit_bayes
from puzzles import load
import transformer as tf

SIZES = [50, 500, 5000, 20000]
BUCKETS = [(0, 0, "1st answer"), (1, 3, "answers 2-4"), (4, 9, "answers 5-10"), (10, 99, "answers 11+")]


def metrics(rows):
    """rows: array [n, >=4] of (p_chosen, p_correct, hit, correct?, t) in model-specific layout."""
    pc, pcor, hit, y, t = rows
    nll = -np.log(np.maximum(pc, 1e-12))
    brier = (pcor - y) ** 2
    bins = np.minimum((pcor * 10).astype(int), 9)
    ece = sum(abs(pcor[bins == b].mean() - y[bins == b].mean()) * (bins == b).mean() for b in range(10) if (bins == b).any())
    out = dict(nll=float(nll.mean()), top1=float(hit.mean()), brier=float(brier.mean()), ece=float(ece), n=int(len(pc)))
    out["by_history"] = {name: dict(nll=float(nll[(t >= lo) & (t <= hi)].mean()), top1=float(hit[(t >= lo) & (t <= hi)].mean()))
                         for lo, hi, name in BUCKETS if ((t >= lo) & (t <= hi)).any()}
    return out


def from_eval(rows):
    r = np.array(rows, dtype=float)
    return r[:, 0], r[:, 1], r[:, 2], r[:, 3], r[:, 4]


def main():
    os.makedirs("results", exist_ok=True)
    P = load("data/puzzles.jsonl")
    train_all = pickle.load(open("data/sim_train.pkl", "rb"))
    test = pickle.load(open("data/sim_test.pkl", "rb"))
    y_test = np.array([a["correct"] for pl in test for a in pl["attempts"][:tf.T_MAX]], float)
    cache = tf.puzzle_cache(P)
    t0 = time.time()
    test_data = tf.build(test, P, cache)
    print(f"test tensors built in {time.time() - t0:.0f}s; {len(test)} players, {int(test_data['valid'].sum())} answers")
    results = {"hand-set": metrics(from_eval(evaluate(HandSet(), test, P)))}
    print(f"hand-set: {results['hand-set']['nll']:.3f}")
    for n in SIZES:
        train = train_all[:n]
        print(f"\n== corpus of {n} players")
        f = fit_bayes(train, P)
        print(f"    bayes fit: eps={f['eps']} lam={f['lam']} rev={f['rev']}")
        for learn in (False, True):
            m = Bayes(f["prior"], f["glob"], f["eps"], f["lam"], f["rev"], learn=learn)
            key = ("bayes" if learn else "crowd") + f"@{n}"
            results[key] = metrics(from_eval(evaluate(m, test, P)))
            print(f"    {m.name}: nll {results[key]['nll']:.3f} top1 {results[key]['top1']:.3f}")
        nval = max(5, n // 10)
        t0 = time.time()
        tr = tf.build(train[nval:], P, cache)
        va = tf.build(train[:nval], P, cache)
        # signature tensors must share S: pad to the larger
        S = max(tr["sf"].shape[2], va["sf"].shape[2], test_data["sf"].shape[2])
        tr, va, te = (pad(d, S) for d in (tr, va, test_data))
        model = tf.train(tr, va)
        rows = tf.nll_rows(model, te, "cuda")
        r = metrics((rows[:, 0], rows[:, 1], rows[:, 2], y_test, rows[:, 3]))
        results[f"transformer@{n}"] = r
        print(f"    transformer: nll {r['nll']:.3f} top1 {r['top1']:.3f} ({time.time() - t0:.0f}s incl. training)")
        torch.save(model.state_dict(), f"results/transformer_{n}.pt")
        json.dump(results, open("results/results.json", "w"), indent=1)


def pad(d, S):
    s = d["sf"].shape[2]
    if s == S:
        return d
    d = dict(d)
    N, T = d["sf"].shape[:2]
    d["sf"] = torch.cat([d["sf"], torch.zeros(N, T, S - s, tf.F_SIG, dtype=d["sf"].dtype)], 2)
    d["lc"] = torch.cat([d["lc"], torch.full((N, T, S - s), -math.inf)], 2)
    d["acc"] = torch.cat([d["acc"], torch.zeros(N, T, S - s, dtype=torch.bool)], 2)
    return d


if __name__ == "__main__":
    main()
