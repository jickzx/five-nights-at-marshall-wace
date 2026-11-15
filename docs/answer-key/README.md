# Answer key printer

Prints the puzzles, their answers and the quiz answers for any seed by compiling the game's own language engine (`Unity/Assets/Scripts/Lang` plus two Core files) outside Unity. It doesn't change the game. See `docs/PITCH.md`, section 1.

```bash
D=/Applications/Unity/Hub/Editor/6000.6.9f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet
cd docs/answer-key
$D run -c Release -- 4242               # demo seed: what a fresh run gets at each of the 3 consoles
$D run -c Release -- 4242 --all-tiers   # every version a console can take, by director difficulty
$D run -c Release -- --catalog          # one example of each of the 22 puzzle types
```
