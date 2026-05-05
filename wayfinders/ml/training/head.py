"""ResolutionHead — the only module that trains.

Architecture (Pax 2026-05-02 §2, encoder-agnostic 384-dim concat):

    char_vec   (384) ┐
    action_vec (384) ├─ concat ─┐
    context_vec(384) ┘          ├─ + cos(char, action) (1) ─ Linear(1153→256) ─ GELU ─ Dropout(0.1)
                                ┘                          ─ Linear(256→64)   ─ GELU
                                                           ─ Linear(64→1)     ─ tanh × 5
                                                           = Δ ∈ [-5, +5]

Total params: ~313k. Negligible vs. the encoder.

The cosine `cos(char_vec, action_vec)` is computed **inside** the graph so
the ONNX export carries it. Rune does not compute it client-side.
"""

from __future__ import annotations

from typing import Final

import torch
from torch import nn

# Output range (Pax §3 — symmetric, tanh-bounded).
DELTA_RANGE: Final[float] = 5.0

# Concat input: 3 vecs × 384 dims + 1 cosine scalar.
HEAD_INPUT_DIM: Final[int] = 384 * 3 + 1  # 1153
HEAD_HIDDEN_1: Final[int] = 256
HEAD_HIDDEN_2: Final[int] = 64


class ResolutionHead(nn.Module):
    """The trainable MLP head that maps three frozen embeddings to Δ.

    Inputs are L2-normalized 384-dim vectors from frozen MiniLM. The cosine
    of (char, action) is appended as an explicit competence-signal feature
    so the head doesn't have to discover it from scratch on small data.
    """

    def __init__(self, dropout: float = 0.1) -> None:
        super().__init__()
        self.fc1 = nn.Linear(HEAD_INPUT_DIM, HEAD_HIDDEN_1)
        self.fc2 = nn.Linear(HEAD_HIDDEN_1, HEAD_HIDDEN_2)
        self.fc3 = nn.Linear(HEAD_HIDDEN_2, 1)
        self.act = nn.GELU()
        self.dropout = nn.Dropout(p=dropout)

        # Init: Kaiming on hidden layers, last layer biased to zero so the
        # head starts at Δ=0 (no prior bias toward advantage or disadvantage).
        for layer in (self.fc1, self.fc2):
            nn.init.kaiming_normal_(layer.weight, nonlinearity="relu")
            nn.init.zeros_(layer.bias)
        nn.init.xavier_normal_(self.fc3.weight, gain=0.1)
        nn.init.zeros_(self.fc3.bias)

    def forward(
        self,
        char_vec: torch.Tensor,
        action_vec: torch.Tensor,
        context_vec: torch.Tensor,
    ) -> torch.Tensor:
        """
        Args:
            char_vec, action_vec, context_vec: shape (batch, 384), L2-normalized.

        Returns:
            delta: shape (batch, 1), in [-5, +5].
        """
        # Cosine — inputs are already normalized, so it's just a dot.
        cos_ca = (char_vec * action_vec).sum(dim=-1, keepdim=True)  # (batch, 1)

        x = torch.cat([char_vec, action_vec, context_vec, cos_ca], dim=-1)  # (batch, 1153)
        x = self.act(self.fc1(x))
        x = self.dropout(x)
        x = self.act(self.fc2(x))
        x = self.fc3(x)  # (batch, 1)
        return torch.tanh(x) * DELTA_RANGE
