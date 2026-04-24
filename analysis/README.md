# Thesis analysis

Python notebook that consumes the batch CSVs produced by `ExperimentRunner` and renders the thesis figures.

## Setup (one time)

```powershell
cd analysis
python -m venv .venv
.venv\Scripts\Activate.ps1
pip install -r requirements.txt
```

## Run

1. Run a batch experiment in Unity (**Thesis → Run Batch Experiment**). Four CSVs land on Desktop:
   - `TrialSummary_batch_{ts}.csv`
   - `Performance_batch_{ts}.csv`
   - `GoalDistribution_batch_{ts}.csv`
   - `EventLog_batch_{ts}.csv`
2. Launch the notebook:
   ```powershell
   jupyter notebook thesis_analysis.ipynb
   ```
3. Run all cells. The notebook auto-picks the **newest** `*_batch_*` timestamp on Desktop. Override via the `BATCH_DIR` / `BATCH_TIMESTAMP` constants at the top.

Figures are rendered inline and also saved as PNG to `analysis/figures/`.

## Figures produced

1. Mean-metric bar chart per condition, faceted by `AgentCount`.
2. Goal-entropy box plot per condition, faceted by `AgentCount`.
3. Reaction-time histogram (melee + ranged) per condition.
4. Scaling curves — metric vs. `AgentCount`, one line per condition.
5. Trial-outcome stacked bars — `PlayerDeath` / `FlockWiped` / `TimeLimit` per condition × size.
6. FPS-over-time traces from `Performance_batch` (spot-check per condition).
