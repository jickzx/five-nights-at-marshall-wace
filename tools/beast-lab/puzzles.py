"""Loads exported puzzles and precomputes every tile sequence a player could submit.

Every puzzle comes from the game's own engine (export/), so tiles, accepted answers and rival
grammars are exactly what the game generates. A candidate is an ordered sequence of distinct tiles
(length 1..n), which is the same space PuzzleBuilder.CountValidSequences enumerates.
"""
import itertools
import json
from dataclasses import dataclass, field

import numpy as np

LANGS = ("fr", "ja")


@dataclass
class Puzzle:
    idx: int
    lang: str
    kind: str
    tier: int
    concepts: list            # concepts any rival grammar of this puzzle gets wrong
    hyp_keys: list            # answer key per hypothesis h (0 = true grammar)
    hyp_wrong: list           # frozenset of wrong concepts per hypothesis
    ans_len: int
    n_cands: int = 0
    cand_len: np.ndarray = None
    cand_accepted: np.ndarray = None
    cand_hyp: np.ndarray = None       # hypothesis that produces the candidate, -1 if none
    cand_disp: np.ndarray = None      # candidate = the first k tiles in the order shown
    cand_only_ans: np.ndarray = None  # candidate uses only answer tiles
    hyp_cands: list = field(default_factory=list)  # candidate indices realising each hypothesis
    cand_seq: list = None             # tile indices of each candidate
    seq_index: dict = None            # tile-index tuple -> candidate index


def load(path, langs=LANGS):
    raw = [json.loads(l) for l in open(path)]
    raw = [r for r in raw if r["lang"] in langs]
    out = []
    for r in raw:
        tiles = r["tiles"]
        hyp_keys = [h["key"] for h in r["hyps"]]
        hyp_wrong = [frozenset(h["wrong"]) for h in r["hyps"]]
        concepts = sorted(set().union(*hyp_wrong))
        p = Puzzle(len(out), r["lang"], r["kind"], r["tier"], concepts, hyp_keys, hyp_wrong, len(r["answer"]))
        accepted = set(r["accepted"])
        ans_ids = set(r["answer"])
        key_to_hyp = {}
        for h, k in enumerate(hyp_keys):  # first (fewest-wrong) hypothesis wins a shared key
            key_to_hyp.setdefault(k, h)
        lens, acc, hyp, disp, only, seqs = [], [], [], [], [], []
        p.hyp_cands = [[] for _ in hyp_keys]
        n = len(tiles)
        for L in range(1, n + 1):
            for seq in itertools.permutations(range(n), L):
                key = "+".join(m for i in seq for m in tiles[i]["morphs"])
                ci = len(lens)
                seqs.append(seq)
                lens.append(L)
                acc.append(key in accepted)
                h = key_to_hyp.get(key, -1)
                hyp.append(h)
                if h >= 0:
                    p.hyp_cands[h].append(ci)
                disp.append(seq == tuple(range(L)))
                only.append(all(tiles[i]["id"] in ans_ids for i in seq))
        p.n_cands = len(lens)
        p.cand_seq = seqs
        p.seq_index = {sq: i for i, sq in enumerate(seqs)}
        p.cand_len = np.array(lens, np.int16)
        p.cand_accepted = np.array(acc, bool)
        p.cand_hyp = np.array(hyp, np.int16)
        p.cand_disp = np.array(disp, bool)
        p.cand_only_ans = np.array(only, bool)
        out.append(p)
    return out


def realise(p, wrong, rejected):
    """Candidate a player holding `wrong` (set of misunderstood concepts) submits, given rejected candidates.

    The player uses the hypothesis that matches their beliefs on this puzzle's concepts; if its phrase
    was already rejected, the nearest hypothesis (fewest differing concepts) that is still untried.
    Returns -1 when every hypothesis phrase has been rejected.
    """
    want = frozenset(wrong) & frozenset(p.concepts)
    best, best_d = -1, 1 << 30
    for h, w in enumerate(p.hyp_wrong):
        free = [c for c in p.hyp_cands[h] if c not in rejected]
        if not free:
            continue
        d = len(w ^ want)
        if d < best_d:
            best, best_d = free[0], d
    return best
