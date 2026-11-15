"""Baselines and the Bayesian beast. Each model, for every attempt, returns a probability for every
candidate tile sequence. Models see a player's attempts in order and only the past (no lookahead).
"""
import itertools
import math
from multiprocessing import Pool

import numpy as np

from puzzles import realise

CONCEPTS = ["adjective-order", "gender-agreement", "plural-agreement", "verb-final", "particles", "compound-meaning"]


def free_mask(p, rejected):
    m = np.ones(p.n_cands, bool)
    if rejected:
        m[rejected] = False
    return m


class HandSet:
    """No learning, hand-set odds: 70% the right answer, 20% a rival grammar, 10% anything else."""
    name = "hand-set (no learning)"

    def start(self, player):
        pass

    def predict(self, p, rejected):
        free = free_mask(p, rejected)
        groups = [free & p.cand_accepted, free & ~p.cand_accepted & (p.cand_hyp > 0), free & ~p.cand_accepted & (p.cand_hyp <= 0)]
        w = np.array([0.7, 0.2, 0.1]) * np.array([g.any() for g in groups])
        w /= w.sum()
        out = np.zeros(p.n_cands)
        for g, wi in zip(groups, w):
            if g.any():
                out[g] = wi / g.sum()
        return out

    def observe(self, p, rejected, cand, correct):
        pass


def fit_prior(players, P, kappa=20.0, eps=0.1):
    """Partial pooling: each (language, concept) misconception rate is shrunk toward the global rate.

    Uses each player's first attempt on the first puzzle that tests a concept, read through the
    hypothesis their phrase matches. The slip rate eps is backed out of the raw rate."""
    k, n = {}, {}
    for pl in players:
        seen = set()
        for a in pl["attempts"]:
            if a["n"] != 0:
                continue
            p = P[a["puzzle"]]
            h = p.cand_hyp[a["cand"]]
            for c in p.concepts:
                if c in seen:
                    continue
                seen.add(c)
                key = (p.lang, c)
                n[key] = n.get(key, 0) + 1
                k[key] = k.get(key, 0) + (h >= 0 and c in p.hyp_wrong[h])
    tot_k, tot_n = sum(k.values()), sum(n.values())
    glob = tot_k / max(1, tot_n)
    prior = {}
    for key in n:
        raw = (k[key] + kappa * glob) / (n[key] + kappa)
        prior[key] = float(np.clip(raw / (1 - eps), 0.02, 0.98))
    return prior, float(np.clip(glob / (1 - eps), 0.02, 0.98))


class Bayes:
    """The eavesdropping beast: a belief per concept (P the player has it wrong), updated by exact
    Bayes over the joint beliefs on the puzzle's concepts after each phrase, then a learning step
    (a rejection may fix the exposed misconception, a reveal may fix the rest)."""

    def __init__(self, prior, glob, eps, lam, rev, learn=True, name=None):
        self.prior, self.glob, self.eps, self.lam, self.rev, self.learn = prior, glob, eps, lam, rev, learn
        self.name = name or ("Bayesian beast" if learn else "crowd prior (no per-player learning)")

    def start(self, player):
        self.q = {}
        self.lang = player["lang"]

    def _q(self, c):
        return self.q.get(c, self.prior.get((self.lang, c), self.glob))

    def _joint(self, p, rejected):
        rej = set(rejected)
        C = p.concepts
        out = []
        for bits in itertools.product((0, 1), repeat=len(C)):
            w = frozenset(c for c, b in zip(C, bits) if b)
            pr = 1.0
            for c, b in zip(C, bits):
                q = self._q(c)
                pr *= q if b else 1 - q
            out.append((w, pr, realise(p, w, rej)))
        return out

    def predict(self, p, rejected):
        free = free_mask(p, rejected)
        N = free.sum()
        out = np.where(free, self.eps / N, 0.0)
        for w, pr, r in self._joint(p, rejected):
            if r >= 0:
                out[r] += pr * (1 - self.eps)
            else:
                out[free] += pr * (1 - self.eps) / N
        return out / out.sum()

    def observe(self, p, rejected, cand, correct):
        if not self.learn:
            return
        free = free_mask(p, rejected)
        N = free.sum()
        post = {}  # new belief set -> mass
        for w, pr, r in self._joint(p, rejected):
            l_plan = (1 - self.eps) * (1.0 if r == cand else 0.0) if r >= 0 else (1 - self.eps) / N
            l_slip = self.eps / N
            for slip, l in ((False, l_plan), (True, l_slip)):
                m = pr * l
                if m <= 0:
                    continue
                if correct:  # reveal: each misconception on this puzzle fixed with p = rev
                    ws = sorted(w)
                    for keep in itertools.product((0, 1), repeat=len(ws)):
                        mm = m
                        for b in keep:
                            mm *= (1 - self.rev) if b else self.rev
                        nw = frozenset(c for c, b in zip(ws, keep) if b)
                        post[nw] = post.get(nw, 0) + mm
                    continue
                h = p.cand_hyp[cand]
                exposed = sorted(p.hyp_wrong[h] & w) if (not slip and h >= 0) else []
                if exposed:
                    post[w] = post.get(w, 0) + m * (1 - self.lam)
                    for c in exposed:
                        nw = w - {c}
                        post[nw] = post.get(nw, 0) + m * self.lam / len(exposed)
                else:
                    post[w] = post.get(w, 0) + m
        z = sum(post.values())
        for c in p.concepts:
            self.q[c] = float(np.clip(sum(m for w, m in post.items() if c in w) / z, 1e-4, 1 - 1e-4))


# ---------------------------------------------------------------- evaluation

def run_player(model, pl, P):
    """Yields per-attempt (p_chosen, p_correct, hit, correct, t) for one player."""
    model.start(pl)
    rows = []
    for t, a in enumerate(pl["attempts"]):
        p = P[a["puzzle"]]
        probs = model.predict(p, a["rejected"])
        pc = probs[a["cand"]]
        top = probs.max()
        ties = np.isclose(probs, top).sum()
        hit = (1.0 / ties) if np.isclose(pc, top) else 0.0
        rows.append((float(pc), float(probs[p.cand_accepted].sum()), hit, a["correct"], t))
        model.observe(p, a["rejected"], a["cand"], a["correct"])
    return rows


_G = {}


def _init(P, model):
    _G["P"], _G["model"] = P, model


def _run(pl):
    return run_player(_G["model"], pl, _G["P"])


def evaluate(model, players, P, procs=16):
    with Pool(procs, initializer=_init, initargs=(P, model)) as pool:
        rows = pool.map(_run, players, chunksize=16)
    return [r for rs in rows for r in rs]


def loglik(model, players, P, procs=16):
    rows = evaluate(model, players, P, procs)
    return float(np.mean([math.log(max(r[0], 1e-12)) for r in rows]))


def fit_bayes(train, P, procs=16, wide=False):
    """Prior from the corpus, then the three structural rates by grid search on training likelihood.
    wide=True searches a larger grid, so extreme test worlds are within reach."""
    best = None
    sub = train[:1000]
    E, L, R = ((0.05, 0.1, 0.2, 0.35, 0.5), (0.3, 0.55, 0.8, 0.95), (0.3, 0.5, 0.7, 0.9)) if wide else \
              ((0.05, 0.1, 0.15), (0.3, 0.55, 0.8), (0.3, 0.5, 0.7))
    for eps in E:
        prior, glob = fit_prior(train, P, eps=eps)
        for lam in L:
            for rev in R:
                ll = loglik(Bayes(prior, glob, eps, lam, rev), sub, P, procs)
                if best is None or ll > best[0]:
                    best = (ll, eps, lam, rev, prior, glob)
    _, eps, lam, rev, prior, glob = best
    return dict(prior=prior, glob=glob, eps=eps, lam=lam, rev=rev)
