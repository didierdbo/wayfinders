"""v0.6 retrain -- table recalibration (Varn 2026-05-09).

Same architecture as v0.1 (Approach B locked 2026-05-02), same 50k mixed-policy
rollouts (seed=42), same hyperparams except output paths point to v0.6 artifacts.

Key changes vs v0.1 (v0.5 table):
  - TANH_INPUT_SCALE: 5 -> 8 (expands linear regime, kills comb pattern)
  - Half-step granularity on all Δ-components (0.5 grid vs integer)
  - Recentered negative anchors (ACTION_BASE stealth -1 -> -0.5, etc.)
  - Label distribution target: mean near 0, stdev 2.0-2.5, sat<5%

Decision gate (updated for M1 v0.6 milestone):
  val MAE <= 0.6  -> PASS
  0.6 < MAE <= 0.8 -> YELLOW -> report to Larry/Didier
  MAE > 0.8       -> RED -> escalate

Usage:
    uv run python scripts/train_v0_6.py
    uv run python scripts/train_v0_6.py --n-rollouts 5000 --epochs 10  # smoke
    uv run python scripts/train_v0_6.py --no-cache  # force re-encode
"""

from __future__ import annotations

import argparse
import json
import logging
import sys
from pathlib import Path

import torch

logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s  %(levelname)-8s  %(name)s  %(message)s",
    datefmt="%H:%M:%S",
    stream=sys.stdout,
)
logger = logging.getLogger("train_v0_6")

REPO_ROOT = Path(__file__).resolve().parent.parent
# Reuse existing embedding cache -- prose embeddings are table-independent.
CACHE_PATH = REPO_ROOT / "artifacts" / "cache" / "embeddings.npz"
OUTPUT_DIR = REPO_ROOT / "artifacts" / "models" / "v0.6"
CHECKPOINT_DIR = REPO_ROOT / "artifacts" / "models" / "checkpoints_v0.6"


def parse_args() -> argparse.Namespace:
    p = argparse.ArgumentParser(description="Train UC1 ResolutionHead v0.6")
    p.add_argument("--n-rollouts", type=int, default=50_000)
    p.add_argument("--seed", type=int, default=42)
    p.add_argument("--epochs", type=int, default=50)
    p.add_argument("--batch-size", type=int, default=64)
    p.add_argument("--lr", type=float, default=1e-3)
    p.add_argument("--patience", type=int, default=10)
    p.add_argument("--no-cache", action="store_true")
    p.add_argument("--device", type=str, default=None)
    return p.parse_args()


def main() -> None:
    args = parse_args()

    # ------------------------------------------------------------------
    # 0. CUDA invariant check (wayfinders-torch-cuda lock)
    # ------------------------------------------------------------------
    cuda_ok = torch.cuda.is_available()
    device = args.device or ("cuda" if cuda_ok else "cpu")
    logger.info("torch %s | CUDA available: %s | device: %s", torch.__version__, cuda_ok, device)
    if not cuda_ok:
        logger.warning(
            "CUDA not available -- training on CPU. "
            "Verify torch cu124 build if unexpected (torch.version.cuda=%s).",
            getattr(torch.version, "cuda", "None"),
        )

    # ------------------------------------------------------------------
    # 1. Generate rollouts (v0.6 table, new labels)
    # ------------------------------------------------------------------
    logger.info("Generating %d rollouts (seed=%d, v0.6 table) ...", args.n_rollouts, args.seed)
    from wayfinders.ml.data_gen.policies import MixedPolicy
    from wayfinders.ml.data_gen.rollouts import generate_rollouts

    rollouts = list(generate_rollouts(args.n_rollouts, seed=args.seed, policy=MixedPolicy()))
    logger.info("Generated %d rollouts", len(rollouts))

    missing = sum(1 for r in rollouts if r.label_delta is None)
    if missing > 0:
        logger.error("%d rollouts have no label_delta -- aborting", missing)
        sys.exit(1)

    import statistics

    deltas = [r.label_delta for r in rollouts if r.label_delta is not None]
    sat_hi = sum(1 for d in deltas if abs(d) >= 4.5) / len(deltas) * 100
    logger.info(
        "Label distribution: min=%.3f max=%.3f mean=%.3f stdev=%.3f sat(|d|>=4.5)=%.2f%%",
        min(deltas),
        max(deltas),
        statistics.mean(deltas),
        statistics.stdev(deltas),
        sat_hi,
    )

    # ------------------------------------------------------------------
    # 2. Load encoder
    # ------------------------------------------------------------------
    logger.info("Loading MiniLM encoder on %s ...", device)
    from wayfinders.ml.inference.encoder import MiniLMEncoder

    encoder = MiniLMEncoder(device=device)
    logger.info("Encoder ready (fp16=%s)", encoder.use_fp16)

    # ------------------------------------------------------------------
    # 3. Embedding cache
    # Prose strings are table-independent (the table only changes label_delta,
    # not the rendered prose). Reuse the v0.1 cache for hits; encode any new
    # strings not yet cached.
    # ------------------------------------------------------------------
    from wayfinders.ml.data_gen.cache import ProseHashCache

    if not args.no_cache and CACHE_PATH.exists():
        logger.info("Loading existing embedding cache from %s ...", CACHE_PATH)
        cache = ProseHashCache.load(CACHE_PATH)
        logger.info("Cache loaded: %d entries", len(cache))
    else:
        logger.info("Building fresh embedding cache ...")
        cache = ProseHashCache()

    # ------------------------------------------------------------------
    # 4. Build dataset (populates cache for misses)
    # ------------------------------------------------------------------
    logger.info("Building full dataset / populating cache ...")
    from wayfinders.ml.training.dataset import WayfindersDataset

    full_ds = WayfindersDataset.from_rollouts(rollouts, encoder, cache)
    logger.info("Full dataset: %d items", len(full_ds))

    logger.info("Saving embedding cache to %s ...", CACHE_PATH)
    cache.save(CACHE_PATH)
    logger.info(
        "Cache saved: %d entries (hits=%d misses=%d)",
        len(cache),
        cache.hit_count,
        cache.miss_count,
    )

    # ------------------------------------------------------------------
    # 5. Split by archetype+scene (no leakage)
    # ------------------------------------------------------------------
    from wayfinders.ml.data_gen.split import split_rollouts

    train_rollouts, val_rollouts, test_rollouts = split_rollouts(
        rollouts, ratios=(0.8, 0.1, 0.1), seed=args.seed
    )
    logger.info(
        "Split: train=%d val=%d test=%d",
        len(train_rollouts),
        len(val_rollouts),
        len(test_rollouts),
    )

    # Sanity: label distribution per split.
    for name, split in [("train", train_rollouts), ("val", val_rollouts), ("test", test_rollouts)]:
        sd = [r.label_delta for r in split if r.label_delta is not None]
        if sd:
            logger.info(
                "  %s labels: mean=%.3f stdev=%.3f min=%.3f max=%.3f",
                name,
                statistics.mean(sd),
                statistics.stdev(sd),
                min(sd),
                max(sd),
            )

    train_ds = WayfindersDataset(train_rollouts, cache)
    val_ds = WayfindersDataset(val_rollouts, cache)
    test_ds = WayfindersDataset(test_rollouts, cache)

    # ------------------------------------------------------------------
    # 6. DataLoaders
    # ------------------------------------------------------------------
    from torch.utils.data import DataLoader

    train_loader: DataLoader[tuple[torch.Tensor, ...]] = DataLoader(
        train_ds,
        batch_size=args.batch_size,
        shuffle=True,
        num_workers=0,
        pin_memory=cuda_ok,
        generator=torch.Generator().manual_seed(args.seed),
    )
    val_loader: DataLoader[tuple[torch.Tensor, ...]] = DataLoader(
        val_ds,
        batch_size=args.batch_size,
        shuffle=False,
        num_workers=0,
        pin_memory=cuda_ok,
    )
    test_loader: DataLoader[tuple[torch.Tensor, ...]] = DataLoader(
        test_ds,
        batch_size=args.batch_size,
        shuffle=False,
        num_workers=0,
        pin_memory=cuda_ok,
    )
    logger.info(
        "DataLoaders: %d train | %d val | %d test batches",
        len(train_loader),
        len(val_loader),
        len(test_loader),
    )

    # ------------------------------------------------------------------
    # 7. Model + config
    # ------------------------------------------------------------------
    from wayfinders.ml.training.head import ResolutionHead
    from wayfinders.ml.training.train import TrainConfig, train_loop

    model = ResolutionHead(dropout=0.1)
    n_params = sum(p.numel() for p in model.parameters())
    logger.info("ResolutionHead: %d params", n_params)

    config = TrainConfig(
        epochs=args.epochs,
        batch_size=args.batch_size,
        lr=args.lr,
        weight_decay=1e-4,
        dropout=0.1,
        huber_delta=1.0,
        monotonicity_weight=0.1,
        patience=args.patience,
        seed=args.seed,
        device=device,
        output_dir=OUTPUT_DIR,
        checkpoint_dir=CHECKPOINT_DIR,
    )
    logger.info("TrainConfig: %s", config.model_dump(mode="json"))

    # ------------------------------------------------------------------
    # 8. Train
    # ------------------------------------------------------------------
    logger.info("Starting training loop ...")
    result = train_loop(model, train_loader, val_loader, config)

    logger.info(
        "Training complete: epochs_run=%d best_val_MAE=%.4f (epoch %d)",
        result.epochs_run,
        result.best_val_mae,
        result.best_epoch + 1,
    )
    logger.info(
        "Train curves (epoch 1 / mid / last): loss=[%.4f, %.4f, %.4f]",
        result.train_losses[0],
        result.train_losses[len(result.train_losses) // 2],
        result.train_losses[-1],
    )
    logger.info(
        "Val MAE curves (epoch 1 / mid / last): [%.4f, %.4f, %.4f]",
        result.val_maes[0],
        result.val_maes[len(result.val_maes) // 2],
        result.val_maes[-1],
    )

    # ------------------------------------------------------------------
    # 9. Evaluate best checkpoint on test set
    # ------------------------------------------------------------------
    logger.info("Loading best checkpoint for test evaluation ...")
    from wayfinders.ml.training.eval import evaluate
    from wayfinders.ml.training.train import load_checkpoint

    best_ckpt = CHECKPOINT_DIR / "head_best.pt"
    if best_ckpt.exists():
        best_model = load_checkpoint(best_ckpt, device=device)
    else:
        logger.warning("head_best.pt not found -- using final model state for test eval")
        best_model = model

    best_model.eval()
    test_metrics = evaluate(best_model, test_loader, device=device)
    logger.info(
        "TEST METRICS (held-out, best checkpoint): MAE=%.4f RMSE=%.4f R2=%.4f count=%d",
        test_metrics.mae,
        test_metrics.rmse,
        test_metrics.r_squared,
        test_metrics.count,
    )
    logger.info("MAE by bucket:")
    for bucket, val in test_metrics.mae_by_bucket.items():
        if val is not None:
            logger.info("  %s: %.4f", bucket, val)
        else:
            logger.info("  %s: (no samples)", bucket)

    test_metrics_path = OUTPUT_DIR / "test_metrics.json"
    test_metrics_path.write_text(
        json.dumps(
            {
                "mae": test_metrics.mae,
                "rmse": test_metrics.rmse,
                "r_squared": test_metrics.r_squared,
                "mae_by_bucket": test_metrics.mae_by_bucket,
                "count": test_metrics.count,
            },
            indent=2,
        ),
        encoding="utf-8",
    )
    logger.info("Test metrics written to %s", test_metrics_path)

    # ------------------------------------------------------------------
    # 10. Decision gate (updated to <=0.6 for v0.6 milestone)
    # ------------------------------------------------------------------
    val_mae = result.best_val_mae
    logger.info("=" * 60)
    logger.info("DECISION GATE v0.6 (target: val MAE <= 0.6)")
    logger.info("  best val MAE : %.4f", val_mae)
    logger.info("  test MAE     : %.4f", test_metrics.mae)
    if val_mae <= 0.6:
        gate = "PASS"
        logger.info("  verdict      : PASS (MAE <= 0.6) -- gate cleared")
    elif val_mae <= 0.8:
        gate = "YELLOW"
        logger.warning("  verdict      : YELLOW (0.6 < MAE <= 0.8) -- report to Larry/Didier")
    else:
        gate = "RED"
        logger.error("  verdict      : RED (MAE > 0.8) -- stop, escalate")
    logger.info("=" * 60)

    gate_path = OUTPUT_DIR / "gate.json"
    gate_path.write_text(
        json.dumps(
            {
                "gate": gate,
                "val_mae": val_mae,
                "test_mae": test_metrics.mae,
                "best_epoch": result.best_epoch,
                "epochs_run": result.epochs_run,
                "table_version": "v0.6",
                "tanh_input_scale": 8.0,
                "n_rollouts": args.n_rollouts,
                "seed": args.seed,
            },
            indent=2,
        ),
        encoding="utf-8",
    )
    logger.info("Gate verdict written to %s", gate_path)

    if gate == "RED":
        sys.exit(2)


if __name__ == "__main__":
    main()
