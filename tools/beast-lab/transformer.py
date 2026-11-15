"""A small causal transformer over a player's attempt history that scores every candidate phrase.

It is told nothing about how players think. Inputs are language-independent: the puzzle's kind and
concepts, and for each past attempt what the phrase looked like (which rival grammar it matched, if
any, whether it was right, its length, whether it took tiles in the order shown).

Candidates that look identical to the model (same features) get the same score, so the softmax runs
over distinct feature signatures weighted by how many candidates share them: exact, and ~50x cheaper.
"""
import math

import numpy as np
import torch
import torch.nn as nn

from models import CONCEPTS

CI = {c: i for i, c in enumerate(CONCEPTS)}
KINDS = ["fr:adjective-after", "fr:gender", "fr:plural", "fr:plural-mixed",
         "ja:verb-last", "ja:kanji-compound", "ja:particles", "ja:compound-sentence"]
KI = {k: i for i, k in enumerate(KINDS)}
T_MAX = 48
F_TOK = 2 + len(KINDS) + 1 + 6 + 2 + 1 + 1 + 6 + 6
F_SIG = 6 + 1 + 1 + 1 + 1 + 7 + 1 + 1


def mask_of(concepts):
    m = 0
    for c in concepts:
        m |= 1 << CI[c]
    return m


def bits(m, n=6):
    return [(m >> i) & 1 for i in range(n)]


def puzzle_cache(P):
    """Per puzzle: a base code per candidate (everything except 'rejected')."""
    cache = []
    for p in P:
        hm = np.array([mask_of(p.hyp_wrong[h]) if h >= 0 else 0 for h in p.cand_hyp], np.int64)
        ld = np.clip(p.cand_len.astype(np.int64) - p.ans_len, -3, 3) + 3
        code = (hm | ((p.cand_hyp >= 0).astype(np.int64) << 6) | (p.cand_accepted.astype(np.int64) << 7)
                | ((p.cand_len == p.ans_len).astype(np.int64) << 9) | (ld << 10)
                | (p.cand_disp.astype(np.int64) << 13) | (p.cand_only_ans.astype(np.int64) << 14))
        cache.append(code)
    return cache


def decode_sig(code):
    hm = code & 63
    ld = (code >> 10) & 7
    one = [0] * 7
    one[ld] = 1
    return bits(hm) + [(code >> 6) & 1, (code >> 7) & 1, (code >> 8) & 1, (code >> 9) & 1] + one + [(code >> 13) & 1, (code >> 14) & 1]


def build(players, P, cache):
    """Dense tensors: tokens [N,T,F_TOK], sig feats [N,T,S,F_SIG], log counts, chosen sig, accepted mask."""
    per = []
    s_max = 1
    for pl in players:
        toks, sigs = [], []
        prev = None
        for a in pl["attempts"][:T_MAX]:
            p = P[a["puzzle"]]
            code = cache[a["puzzle"]].copy()
            if a["rejected"]:
                code[a["rejected"]] |= 1 << 8
            uniq, inv, cnt = np.unique(code, return_inverse=True, return_counts=True)
            ch = int(inv[a["cand"]])
            s_max = max(s_max, len(uniq))
            lang = [p.lang == "fr", p.lang == "ja"]
            kind = [0] * len(KINDS)
            kind[KI[f"{p.lang}:{p.kind}"]] = 1
            cur = lang + kind + [p.tier / 3] + bits(mask_of(p.concepts)) + [a["n"] / 8, a["n"] == 0]
            if prev is None:
                pv = [1, 0] + [0] * 6 + [0] * 6
            else:
                pp, pa = prev
                c = pa["cand"]
                h = pp.cand_hyp[c]
                pv = ([0, pa["correct"]] + bits(mask_of(pp.hyp_wrong[h]) if h >= 0 else 0)
                      + [h >= 0, pp.cand_accepted[c], pp.cand_len[c] == pp.ans_len, pp.cand_disp[c],
                         pp.cand_only_ans[c], pp.idx == p.idx])
            toks.append(np.array(cur + pv, np.float32))
            sigs.append((np.array([decode_sig(int(u)) for u in uniq], np.uint8), np.log(cnt).astype(np.float32),
                         ch, np.array([(int(u) >> 7) & 1 for u in uniq], bool), float(np.log(cnt[ch]))))
            prev = (p, a)
        per.append((toks, sigs))
    N = len(players)
    tok = np.zeros((N, T_MAX, F_TOK), np.float32)
    sf = np.zeros((N, T_MAX, s_max, F_SIG), np.uint8)
    lc = np.full((N, T_MAX, s_max), -np.inf, np.float32)
    acc = np.zeros((N, T_MAX, s_max), bool)
    chosen = np.zeros((N, T_MAX), np.int64)
    chosen_lc = np.zeros((N, T_MAX), np.float32)
    valid = np.zeros((N, T_MAX), bool)
    for i, (toks, sigs) in enumerate(per):
        for t, (tk, (f, l, ch, ac, clc)) in enumerate(zip(toks, sigs)):
            tok[i, t] = tk
            sf[i, t, :len(f)] = f
            lc[i, t, :len(l)] = l
            acc[i, t, :len(ac)] = ac
            chosen[i, t] = ch
            chosen_lc[i, t] = clc
            valid[i, t] = True
    lc[~valid, 0] = 0.0  # padding steps: one finite logit so log_softmax stays finite
    T = lambda x: torch.from_numpy(x)
    return dict(tok=T(tok), sf=T(sf), lc=T(lc), acc=T(acc), chosen=T(chosen), chosen_lc=T(chosen_lc), valid=T(valid))


class BeastNet(nn.Module):
    def __init__(self, d=128, layers=3, heads=4, drop=0.1):
        super().__init__()
        self.inp = nn.Linear(F_TOK, d)
        self.pos = nn.Parameter(torch.zeros(T_MAX, d))
        layer = nn.TransformerEncoderLayer(d, heads, 2 * d, drop, batch_first=True, norm_first=True)
        self.enc = nn.TransformerEncoder(layer, layers)
        self.sig = nn.Linear(F_SIG, d)
        self.score = nn.Sequential(nn.Linear(3 * d, 2 * d), nn.GELU(), nn.Linear(2 * d, 1))
        self.register_buffer("causal", torch.triu(torch.ones(T_MAX, T_MAX, dtype=torch.bool), 1))

    def forward(self, tok, sf, lc):
        h = self.enc(self.inp(tok) + self.pos, mask=self.causal, is_causal=True)   # [B,T,d]
        s = self.sig(sf.float())                                                    # [B,T,S,d]
        hh = h.unsqueeze(2).expand_as(s)
        logits = self.score(torch.cat([hh, s, hh * s], -1)).squeeze(-1) + lc       # [B,T,S]
        return torch.log_softmax(logits, -1)


def to(batch, dev):
    return {k: v.to(dev, non_blocking=True) for k, v in batch.items()}


def nll_rows(model, data, dev, bs=512):
    """Per valid attempt: (p_chosen_candidate, p_correct, top1 hit, t)."""
    model.eval()
    out = []
    with torch.no_grad():
        for i in range(0, data["tok"].shape[0], bs):
            b = to({k: v[i:i + bs] for k, v in data.items()}, dev)
            lp = model(b["tok"], b["sf"], b["lc"])
            lpc = lp.gather(-1, b["chosen"].unsqueeze(-1)).squeeze(-1) - b["chosen_lc"]
            pcorr = (lp.exp() * b["acc"]).sum(-1)
            top = lp.argmax(-1)
            hit = (top == b["chosen"]).float() * torch.exp(-b["chosen_lc"])
            t = torch.arange(lp.shape[1], device=dev).expand_as(lpc)
            v = b["valid"]
            out.append(torch.stack([lpc[v].exp(), pcorr[v], hit[v], t[v].float()], -1).cpu())
    return torch.cat(out).numpy()


def train(train_data, val_data, dev="cuda", epochs=80, patience=8, lr=1e-3, seed=0, log=print):
    torch.manual_seed(seed)
    model = BeastNet().to(dev)
    opt = torch.optim.AdamW(model.parameters(), lr=lr, weight_decay=0.02)
    N = train_data["tok"].shape[0]
    bs = max(8, min(256, N // 4))
    best, best_state, bad = math.inf, None, 0
    gtrain = to(train_data, dev)
    for ep in range(epochs):
        model.train()
        perm = torch.randperm(N, device=dev)
        for i in range(0, N, bs):
            idx = perm[i:i + bs]
            b = {k: v[idx] for k, v in gtrain.items()}
            lp = model(b["tok"], b["sf"], b["lc"])
            lpc = lp.gather(-1, b["chosen"].unsqueeze(-1)).squeeze(-1)
            loss = -torch.where(b["valid"], lpc, 0.0).sum() / b["valid"].sum()
            opt.zero_grad()
            loss.backward()
            nn.utils.clip_grad_norm_(model.parameters(), 1.0)
            opt.step()
        rows = nll_rows(model, val_data, dev)
        v = -np.log(np.maximum(rows[:, 0], 1e-12)).mean()
        if v < best - 1e-4:
            best, bad = v, 0
            best_state = {k: x.detach().clone() for k, x in model.state_dict().items()}
        else:
            bad += 1
            if bad >= patience:
                break
    model.load_state_dict(best_state)
    log(f"    transformer: {sum(p.numel() for p in model.parameters()):,} weights, stopped at epoch {ep + 1}, val nll {best:.3f}")
    return model
