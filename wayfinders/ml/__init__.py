"""Wayfinders UC1 ML pipeline.

The module that turns engine state into a scalar delta in [-5, +5] which the
rule layer adds to the d20 modifier stack.

Pipeline:
    (CharacterState, ActionCard, SceneState, ...)
        --[deterministic renderers, EN-pinned]-->
    (prose_C, prose_A, prose_X)
        --[frozen MiniLM encoder]-->
    (char_vec, action_vec, context_vec)  each (384,)
        --[ResolutionHead MLP, ~313k params]-->
    delta in [-5, +5]

See README.md for the hard contracts (EN-pinned, deterministic, frozen
encoder, Pratchett tonal palette).
"""

__version__ = "0.1.0.dev0"
