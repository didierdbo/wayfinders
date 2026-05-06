"""M1.10 -- First real training run: v0.1 head on 50k synthetic rollouts.

Pipeline:
  1. Generate 50k rollouts (seed=42, MixedPolicy).
  2. Load MiniLMEncoder (CUDA fp16 if available).
  3. Build WayfindersDataset via from_rollouts() -- populates ProseHashCache.
  4. Split 80/10/10 by archetype + scene (no leakage).
  5. Build ResolutionHead, TrainConfig (defaults: 50 epochs, AdamW lr=1e-3).
  6. Run train_loop -- logs per-epoch metrics to stdout via logging.
  7. Evaluate on held-out test set (never seen during train/val).
  8. Write artifacts:
       artifacts/models/v0.1/head_final.pt
       artifacts/models/v0.1/metrics.json
       artifacts/models/v0.1/test_metrics.json
       artifacts/cache/embeddings.npz  (populated during step 3)
       artifacts/models/checkpoints/head_best.pt

Decision gate (Coda M1 plan substep 10):
  val MAE <= 0.8  -> PASS -> continue to substep 11 (ONNX export)
  0.8 < MAE <= 1.0 -> YELLOW -> report to Larry/Didier for decision
  MAE > 1.0       -> RED -> stop, escalate to Varn for modifier table rework

Usage:
    uv run python scripts/train_v0_1.py
    uv run python scripts/train_v0_1.py --n-rollouts 5000  # quick smoke test
    uv run python scripts/train_v0_1.py --epochs 10 --no-cache  # force re-encode
"""

from __future__ import annotations

import argparse
import json
import logging
import sys
from pathlib import Path

import torch

# Configure structured logging to stdout so Didier can watch the run live.
logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s  %(levelname)-8s  %(name)s  %(message)s",
    datefmt="%H:%M:%S",
    stream=sys.stdout,
)
logger = logging.getLogger("train_v0_1")

REPO_ROOT = Path(__file__).resolve().parent.parent
CACHE_PATH = REPO_ROOT / "artifacts" / "cache" / "embeddings.npz"
OUTPUT_DIR = REPO_ROOT / "artifacts" / "models" / "v0.1"
CHECKPOINT_DIR = REPO_ROOT / "artifacts" / "models" / "checkpoints"


def parse_args() -> argparse.Namespace:
    p = argparse.ArgumentParser(description="Train UC1 ResolutionHead v0.1")
    p.add_argument(
        "--n-rollouts",
        type=int,
        default=50_000,
        help="Number of synthetic rollouts to generate (default: 50000)",
    )
    p.add_argument(
        "--seed",
        type=int,
        default=42,
        help="Master RNG seed for rollout generation and training (default: 42)",
    )
    p.add_argument(
        "--epochs",
        type=int,
        default=50,
        help="Max training epochs (default: 50)",
    )
    p.add_argument(
        "--batch-size",
        type=int,
        default=64,
        help="Batch size (default: 64)",
    )
    p.add_argument(
        "--lr",
        type=float,
        default=1e-3,
        help="AdamW learning rate (default: 1e-3)",
    )
    p.add_argument(
        "--patience",
        type=int,
        default=10,
        help="Early stopping patience in epochs (default: 10, 0=disable)",
    )
    p.add_argument(
        "--no-cache",
        action="store_true",
        help="Ignore existing embedding cache and re-encode from scratch",
    )
    p.add_argument(
        "--device",
        type=str,
        default=None,
        help="Device override: 'cpu' or 'cuda' (default: auto-detect)",
    )
    return p.parse_args()


def main() -> None:
    args = parse_args()

    # ------------------------------------------------------------------
    # 0. CUDA check
    # ------------------------------------------------------------------
    cuda_ok = torch.cuda.is_available()
    device = args.device or ("cuda" if cuda_ok else "cpu")
    logger.info("CUDA available: %s | device: %s", cuda_ok, device)
    if not cuda_ok:
        logger.warning(
            "CUDA not available -- training on CPU. "
            "This will be much slower. Check torch installation if unexpected."
        )

    # ------------------------------------------------------------------
    # 1. Generate rollouts
    # ------------------------------------------------------------------
    logger.info("Generating %d rollouts (seed=%d) ...", args.n_rollouts, args.seed)
    from wayfinders.ml.data_gen.policies import MixedPolicy
    from wayfinders.ml.data_gen.rollouts import generate_rollouts

    rollouts = list(generate_rollouts(args.n_rollouts, seed=args.seed, policy=MixedPolicy()))
    logger.info("Generated %d rollouts", len(rollouts))

    # Sanity check: all rollouts have a label.
    missing = sum(1 for r in rollouts if r.label_delta is None)
    if missing > 0:
        logger.error("%d rollouts have no label_delta -- aborting", missing)
        sys.exit(1)

    # Quick label distribution summary.
    import statistics

    deltas = [r.label_delta for r in rollouts if r.label_delta is not None]
    logger.info(
        "Label distribution: min=%.2f max=%.2f mean=%.2f stdev=%.2f",
        min(deltas),
        max(deltas),
        statistics.mean(deltas),
        statistics.stdev(deltas),
    )

    # ------------------------------------------------------------------
    # 2. Load encoder
    # ------------------------------------------------------------------
    logger.info("Loading MiniLM encoder on %s ...", device)
    from wayfinders.ml.inference.encoder import MiniLMEncoder

    encoder = MiniLMEncoder(device=device)
    logger.info("Encoder ready (fp16=%s)", encoder.use_fp16)

    # ------------------------------------------------------------------
    # 3. Build/load embedding cache
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
    # 4. Build datasets
    # ------------------------------------------------------------------
    logger.info("Building datasets (from_rollouts -- populates cache for misses) ...")
    from wayfinders.ml.training.dataset import WayfindersDataset

    # First build the full dataset to populate the cache -- this is the one
    # expensive pass. Then split and construct per-split datasets from cache.
    full_ds = WayfindersDataset.from_rollouts(rollouts, encoder, cache)
    logger.info("Full dataset: %d items", len(full_ds))

    # Save cache after populating (the expensive part is done).
    logger.info("Saving embedding cache to %s ...", CACHE_PATH)
    cache.save(CACHE_PATH)
    logger.info(
        "Cache saved: %d entries (hits=%d misses=%d)",
        len(cache),
        cache.hit_count,
        cache.miss_count,
    )

    # ------------------------------------------------------------------
    # 5. Split
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

    train_ds = WayfindersDataset(train_rollouts, cache)
    val_ds = WayfindersDataset(val_rollouts, cache)
    test_ds = WayfindersDataset(test_rollouts, cache)

    # ------------------------------------------------------------------
    # 6. DataLoaders
    # ------------------------------------------------------------------
    from torch.utils.data import DataLoader

    # num_workers=0 on Windows -- fork-based workers cause issues with
    # CUDA context and are slower than in-process on the small head.
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
        "DataLoaders ready: %d train batches | %d val batches | %d test batches",
        len(train_loader),
        len(val_loader),
        len(test_loader),
    )

    # ------------------------------------------------------------------
    # 7. Build model + config
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
    # 9. Load best checkpoint and evaluate on test set
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

    # Write test metrics.
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
    # 10. Decision gate
    # ------------------------------------------------------------------
    val_mae = result.best_val_mae
    logger.info("=" * 60)
    logger.info("DECISION GATE (Coda M1 plan substep 10)")
    logger.info("  best val MAE : %.4f", val_mae)
    logger.info("  test MAE     : %.4f", test_metrics.mae)
    if val_mae <= 0.8:
        gate = "PASS"
        logger.info("  verdict      : PASS (MAE <= 0.8) -- proceed to substep 11 (ONNX export)")
    elif val_mae <= 1.0:
        gate = "YELLOW"
        logger.warning(
            "  verdict      : YELLOW (0.8 < MAE <= 1.0) -- report to Larry/Didier for decision"
        )
    else:
        gate = "RED"
        logger.error(
            "  verdict      : RED (MAE > 1.0) -- stop. Escalate to Varn for modifier table rework."
        )
    logger.info("=" * 60)

    # Write gate verdict alongside metrics.
    gate_path = OUTPUT_DIR / "gate.json"
    gate_path.write_text(
        json.dumps(
            {
                "gate": gate,
                "val_mae": val_mae,
                "test_mae": test_metrics.mae,
                "best_epoch": result.best_epoch,
                "epochs_run": result.epochs_run,
            },
            indent=2,
        ),
        encoding="utf-8",
    )
    logger.info("Gate verdict written to %s", gate_path)

    # Exit with non-zero code on RED gate so CI/caller can detect the failure.
    if gate == "RED":
        sys.exit(2)


if __name__ == "__main__":
    main()
