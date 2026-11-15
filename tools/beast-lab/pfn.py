"""Prior-fitted beast: one transformer trained on ~1M simulated players, each from its own random
world, so it learns to infer what kind of player it faces from their answers alone (in context, no
retraining). Then a backtest against the Bayesian beast fitted on 50 players of each test world.

  python pfn.py gen 1000000      # simulate + featurise training players into data/pfn/
  python pfn.py train            # train on the 5090 -> results/pfn.pt
  python pfn.py eval             # test worlds -> results/pfn_results.json
"""
import json
import math
import os
import sys
import time
import warnings
from multiprocessing import Pool

import numpy as np
import torch
import torch.nn as nn

import transformer as tf
from models import Bayes, HandSet, evaluate, fit_bayes
from puzzles import load
from worlds import kinds_index, play, regimes, sample_world

warnings.filterwarnings("ignore", message="enable_nested_tensor")
S_MAX = 128
SHARD = 100_000
TRAIN_SEED0, VAL_SEED0, TEST_SEED0 = 10_000_000, 800_000_000, 900_000_000
DATA = "data/pfn"
DEV = "cuda"

# ---------------------------------------------------------------- featurise (same features as transformer.py)

_G = {}


def _init():
    P = load("data/puzzles.jsonl")
    _G.update(P=P, cache=tf.puzzle_cache(P), by=kinds_index(P))


def featurise(pl, P, cache):
    toks, codes, cnts, chosen = [], [], [], []
    prev = None
    for a in pl["attempts"][:tf.T_MAX]:
        p = P[a["puzzle"]]
        code = cache[a["puzzle"]].copy()
        if a["rejected"]:
            code[a["rejected"]] |= 1 << 8
        uniq, inv, cnt = np.unique(code, return_inverse=True, return_counts=True)
        assert len(uniq) <= S_MAX, len(uniq)
        row_c = np.full(S_MAX, 0, np.int16)
        row_n = np.zeros(S_MAX, np.uint16)
        row_c[:len(uniq)] = uniq
        row_n[:len(uniq)] = cnt
        codes.append(row_c)
        cnts.append(row_n)
        chosen.append(int(inv[a["cand"]]))
        kind = [0] * len(tf.KINDS)
        kind[tf.KI[f"{p.lang}:{p.kind}"]] = 1
        cur = [p.lang == "fr", p.lang == "ja"] + kind + [p.tier / 3] + tf.bits(tf.mask_of(p.concepts)) + [a["n"] / 8, a["n"] == 0]
        if prev is None:
            pv = [1, 0] + [0] * 6 + [0] * 6
        else:
            pp, pa = prev
            c = pa["cand"]
            h = pp.cand_hyp[c]
            pv = ([0, pa["correct"]] + tf.bits(tf.mask_of(pp.hyp_wrong[h]) if h >= 0 else 0)
                  + [h >= 0, pp.cand_accepted[c], pp.cand_len[c] == pp.ans_len, pp.cand_disp[c],
                     pp.cand_only_ans[c], pp.idx == p.idx])
        toks.append(np.array(cur + pv, np.float16))
        prev = (p, a)
    return np.stack(toks), np.stack(codes), np.stack(cnts), np.array(chosen, np.int16)


def pack(players, P, cache):
    parts = [featurise(pl, P, cache) for pl in players]
    lens = np.array([len(x[0]) for x in parts], np.int8)
    return dict(tok=np.concatenate([x[0] for x in parts]), codes=np.concatenate([x[1] for x in parts]),
                cnt=np.concatenate([x[2] for x in parts]), chosen=np.concatenate([x[3] for x in parts]), lens=lens)


def _gen(seeds):
    P = _G["P"]
    return pack([play(P, _G["by"], s) for s in seeds], P, _G["cache"])


def gen(n):
    os.makedirs(DATA, exist_ok=True)
    t0 = time.time()
    with Pool(16, initializer=_init) as pool:
        for s0 in range(0, n, SHARD):
            chunks = [range(TRAIN_SEED0 + i, TRAIN_SEED0 + min(i + 1000, s0 + SHARD, n)) for i in range(s0, min(s0 + SHARD, n), 1000)]
            parts = pool.map(_gen, chunks)
            shard = {k: np.concatenate([p[k] for p in parts]) for k in parts[0]}
            np.savez(f"{DATA}/shard_{s0 // SHARD:03d}.npz", **shard)
            done = min(s0 + SHARD, n)
            print(f"  {done:,} players, {len(shard['tok']):,} answers in this shard, {time.time() - t0:.0f}s", flush=True)


# ---------------------------------------------------------------- GPU dataset

class Packed:
    """All attempts on the GPU; players are (start, len) slices. Row A is a padding row."""

    def __init__(self, d):
        A = len(d["tok"])
        self.tok = torch.cat([torch.from_numpy(d["tok"]), torch.zeros(1, tf.F_TOK, dtype=torch.float16)]).to(DEV)
        self.codes = torch.cat([torch.from_numpy(d["codes"]), torch.zeros(1, S_MAX, dtype=torch.int16)]).to(DEV)
        cnt = torch.cat([torch.from_numpy(d["cnt"].astype(np.float32)), torch.zeros(1, S_MAX)])
        cnt[A, 0] = 1  # padding row: one finite logit
        self.lc = torch.where(cnt > 0, cnt.log(), torch.tensor(-math.inf)).half().to(DEV)
        self.chosen = torch.cat([torch.from_numpy(d["chosen"].astype(np.int64)), torch.zeros(1, dtype=torch.int64)]).to(DEV)
        lens = torch.from_numpy(d["lens"].astype(np.int64))
        self.starts = (torch.cumsum(lens, 0) - lens).to(DEV)
        self.lens = lens.to(DEV)
        self.pad = A
        self.n = len(lens)

    def batch(self, idx, T=None):
        lens = self.lens[idx]
        T = T or int(lens.max())
        ar = torch.arange(T, device=DEV)
        valid = ar[None, :] < lens[:, None]
        rows = torch.where(valid, self.starts[idx][:, None] + ar[None, :], torch.full_like(valid, self.pad, dtype=torch.int64))
        codes = self.codes[rows].long()
        lc = self.lc[rows].float()
        return dict(tok=self.tok[rows].float(), codes=codes, lc=lc, chosen=self.chosen[rows],
                    acc=((codes >> 7) & 1).bool() & torch.isfinite(lc), valid=valid)


SIG_TABLE = None


def sig_table():
    global SIG_TABLE
    if SIG_TABLE is None:
        SIG_TABLE = torch.tensor([tf.decode_sig(c if (c >> 10) & 7 < 7 else c & ~(7 << 10)) for c in range(1 << 15)], dtype=torch.uint8, device=DEV)
    return SIG_TABLE


def load_shards(lo=0, hi=None):
    files = sorted(f for f in os.listdir(DATA) if f.startswith("shard_"))[lo:hi]
    ds = [dict(np.load(f"{DATA}/{f}")) for f in files]
    return {k: np.concatenate([d[k] for d in ds]) for k in ds[0]}


# ---------------------------------------------------------------- model

class PFN(tf.BeastNet):
    def __init__(self, d=256, layers=6, heads=8, drop=0.0):
        super().__init__(d, layers, heads, drop)

    def forward(self, tok, sf, lc):
        T = tok.shape[1]
        h = self.enc(self.inp(tok) + self.pos[:T], mask=self.causal[:T, :T], is_causal=True)
        s = self.sig(sf.float())
        hh = h.unsqueeze(2).expand_as(s)
        return torch.log_softmax(self.score(torch.cat([hh, s, hh * s], -1)).squeeze(-1) + lc, -1)


def logp(model, b, T_fixed=False):
    sf = sig_table()[b["codes"]]
    return model(b["tok"], sf, b["lc"])


def rows_for(model, data, bs=128, T=None):
    """Per valid attempt: (p_chosen_candidate, p_correct, top1 hit, t)."""
    model.eval()
    out = []
    with torch.no_grad(), torch.autocast("cuda", dtype=torch.bfloat16):
        for i in range(0, data.n, bs):
            idx = torch.arange(i, min(i + bs, data.n), device=DEV)
            b = data.batch(idx, T)
            lp = logp(model, b).float()
            clc = b["lc"].gather(-1, b["chosen"].unsqueeze(-1)).squeeze(-1)
            lpc = lp.gather(-1, b["chosen"].unsqueeze(-1)).squeeze(-1) - clc
            pcor = (lp.exp() * b["acc"]).sum(-1)
            hit = (lp.argmax(-1) == b["chosen"]).float() * torch.exp(-clc)
            t = torch.arange(lp.shape[1], device=DEV).expand_as(lpc)
            v = b["valid"]
            out.append(torch.stack([lpc[v].exp(), pcor[v], hit[v], t[v].float()], -1).cpu())
    return torch.cat(out).numpy()


def train(steps=30000, bs=256, lr=3e-4):
    t0 = time.time()
    tr = Packed(load_shards())
    va = Packed(pack_players([play(_G["P"], _G["by"], VAL_SEED0 + i) for i in range(4000)]))
    print(f"loaded {tr.n:,} training players ({tr.pad:,} answers) in {time.time() - t0:.0f}s", flush=True)
    torch.manual_seed(0)
    model = PFN().to(DEV)
    print(f"model: {sum(p.numel() for p in model.parameters()):,} weights", flush=True)
    opt = torch.optim.AdamW(model.parameters(), lr=lr, weight_decay=0.01, betas=(0.9, 0.98))
    sched = torch.optim.lr_scheduler.OneCycleLR(opt, max_lr=lr, total_steps=steps, pct_start=0.03)
    best = math.inf
    for step in range(1, steps + 1):
        model.train()
        idx = torch.randint(0, tr.n, (bs,), device=DEV)
        b = tr.batch(idx)
        with torch.autocast("cuda", dtype=torch.bfloat16):
            lp = logp(model, b).float()
        lpc = lp.gather(-1, b["chosen"].unsqueeze(-1)).squeeze(-1)
        loss = -torch.where(b["valid"], lpc, 0.0).sum() / b["valid"].sum()
        opt.zero_grad(set_to_none=True)
        loss.backward()
        nn.utils.clip_grad_norm_(model.parameters(), 1.0)
        opt.step()
        sched.step()
        if step % max(1, min(1000, steps // 3)) == 0 or step == steps:
            r = rows_for(model, va)
            v = float(-np.log(np.maximum(r[:, 0], 1e-12)).mean())
            if v < best:
                best = v
                torch.save(model.state_dict(), "results/pfn.pt")
            print(f"  step {step:6d}  train {loss.item():.3f}  val nll {v:.3f}  ({time.time() - t0:.0f}s)", flush=True)
    print(f"best val nll {best:.3f}")


def pack_players(players):
    return pack(players, _G["P"], _G["cache"])


# ---------------------------------------------------------------- evaluation

def metrics(pc, pcor, hit, y):
    nll = -np.log(np.maximum(pc, 1e-12))
    bins = np.minimum((pcor * 10).astype(int), 9)
    ece = sum(abs(pcor[bins == b].mean() - y[bins == b].mean()) * (bins == b).mean() for b in range(10) if (bins == b).any())
    return dict(nll=float(nll.mean()), top1=float(hit.mean()), ece=float(ece))


def _play_world(args):
    seed, world = args
    return play(_G["P"], _G["by"], seed, world)


def evaluate_all(n_test=2000, n_fit=50):
    P = _G["P"]
    pfn = PFN().to(DEV)
    pfn.load_state_dict(torch.load("results/pfn.pt"))
    spec = tf.BeastNet().to(DEV)
    spec.load_state_dict(torch.load("results/transformer_20000.pt"))
    worlds = {"in-distribution (new random world per player)": None}
    worlds.update(regimes())
    out = {}
    with Pool(16, initializer=_init) as pool:
        for wi, (name, W) in enumerate(worlds.items()):
            base = TEST_SEED0 + wi * 100_000
            test = pool.map(_play_world, [(base + i, W) for i in range(n_test)], chunksize=32)
            fit = pool.map(_play_world, [(base + 50_000 + i, W) for i in range(n_fit)], chunksize=8)
            y = np.array([a["correct"] for pl in test for a in pl["attempts"][:tf.T_MAX]], float)
            data = Packed(pack_players(test))
            res = {}
            r = np.array(evaluate(HandSet(), test, P), float)
            res["hand-set"] = metrics(r[:, 0], r[:, 1], r[:, 2], y)
            f = fit_bayes(fit, P, wide=True)
            r = np.array(evaluate(Bayes(f["prior"], f["glob"], f["eps"], f["lam"], f["rev"]), test, P), float)
            res["Bayesian beast (fit on 50 players of this world)"] = metrics(r[:, 0], r[:, 1], r[:, 2], y)
            r = rows_for(spec, data, T=tf.T_MAX)
            res["specialist transformer (20k original-sim players)"] = metrics(r[:, 0], r[:, 1], r[:, 2], y)
            r = rows_for(pfn, data)
            res["prior-fitted beast (zero players of this world)"] = metrics(r[:, 0], r[:, 1], r[:, 2], y)
            out[name] = res
            print(f"\n{name}  (Bayes fit eps={f['eps']} lam={f['lam']} rev={f['rev']})")
            for k, v in res.items():
                print(f"  {k:52s} nll {v['nll']:.3f}  top1 {v['top1']:.3f}  ece {v['ece']:.3f}")
            json.dump(out, open("results/pfn_results.json", "w"), indent=1)


if __name__ == "__main__":
    cmd = sys.argv[1]
    os.makedirs("results", exist_ok=True)
    if cmd == "gen":
        gen(int(sys.argv[2]))
    else:
        _init()
        if cmd == "train":
            train(int(sys.argv[2]) if len(sys.argv) > 2 else 30000)
        elif cmd == "eval":
            evaluate_all()
