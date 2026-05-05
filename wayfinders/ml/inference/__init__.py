"""Inference: frozen encoder wrapper + dev-time pipeline.

The encoder is loaded once, frozen (``requires_grad=False``, ``.eval()``),
and exposes a ``encode()`` API with prose-hash caching.

M1 ships the encoder wrapper. The FastAPI service stub is a placeholder
until M2 wires the live inference path.
"""
