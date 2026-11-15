# Beast lab

Offline backtest of how well different beast models predict what a player will submit at a console.
The puzzles come from the game's own engine; the players are simulated (no real player data exists yet).

```bash
# once: .NET 8 SDK (~/.dotnet) and a CUDA PyTorch env
uv venv .venv --python 3.12 && uv pip install --python .venv/bin/python torch numpy
~/.dotnet/dotnet run -c Release --project export -- data/puzzles.jsonl 300   # export puzzles from Unity/Assets/Scripts/Lang
.venv/bin/python sim.py 2000 0 data/sim_test.pkl                            # test players
.venv/bin/python sim.py 20000 1000000 data/sim_train.pkl                    # training players
.venv/bin/python run.py      # all models at 50/500/5000/20000 training players -> results/results.json
.venv/bin/python seeds.py    # small-corpus robustness over 5 player samples
```

| File | What |
|---|---|
| `export/` | C# exporter: compiles the game's `Lang/` code, dumps tiles, accepted answers and rival grammars |
| `puzzles.py` | loads puzzles, enumerates every tile sequence a player could submit |
| `sim.py` | simulated players with hidden misconceptions, slips and learning (the ground truth) |
| `models.py` | hand-set baseline, crowd prior with partial pooling, Bayesian eavesdropping beast |
| `transformer.py` | causal transformer over the attempt history (~0.5M weights) |
| `run.py`, `seeds.py` | backtest and robustness check |

## Prior-fitted beast (`worlds.py`, `pfn.py`)

One transformer (3.6M weights) trained on 2M simulated players, each from its own random world, so it
infers what kind of player it faces from their answers alone. Tested on held-out worlds against the
Bayesian beast fitted on 50 players of each world.

```bash
.venv/bin/python pfn.py gen 2000000   # ~4 min on 16 cores, ~12 GB in data/pfn
.venv/bin/python pfn.py train 40000   # ~31 min on an RTX 5090 -> results/pfn.pt
.venv/bin/python pfn.py eval          # -> results/pfn_results.json
```
