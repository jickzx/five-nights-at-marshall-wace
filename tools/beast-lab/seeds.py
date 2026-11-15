"""Robustness: refit Bayes and the transformer on 5 different small corpora (disjoint player samples)."""
import pickle, numpy as np, warnings
warnings.filterwarnings("ignore")
from models import Bayes, evaluate, fit_bayes
from puzzles import load
import transformer as tf
from run import metrics, from_eval, pad
P = load("data/puzzles.jsonl"); cache = tf.puzzle_cache(P)
train_all = pickle.load(open("data/sim_train.pkl", "rb")); test = pickle.load(open("data/sim_test.pkl", "rb"))
y = np.array([a["correct"] for pl in test for a in pl["attempts"][:tf.T_MAX]], float)
te = tf.build(test, P, cache)
for n in (50, 500):
    b, t = [], []
    for s in range(5):
        tr_pl = train_all[s * n:(s + 1) * n]
        f = fit_bayes(tr_pl, P)
        b.append(metrics(from_eval(evaluate(Bayes(f["prior"], f["glob"], f["eps"], f["lam"], f["rev"]), test, P)))["nll"])
        nv = max(5, n // 10)
        tr, va = tf.build(tr_pl[nv:], P, cache), tf.build(tr_pl[:nv], P, cache)
        S = max(tr["sf"].shape[2], va["sf"].shape[2], te["sf"].shape[2])
        m = tf.train(pad(tr, S), pad(va, S), seed=s, log=lambda *_: None)
        r = tf.nll_rows(m, pad(te, S), "cuda")
        t.append(metrics((r[:, 0], r[:, 1], r[:, 2], y, r[:, 3]))["nll"])
    print(f"{n} players x5 samples: bayes nll {np.mean(b):.3f} ± {np.std(b):.3f}   transformer nll {np.mean(t):.3f} ± {np.std(t):.3f}   (each: {', '.join(f'{x:.2f}' for x in t)})")
