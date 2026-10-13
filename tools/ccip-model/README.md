# The character recognition model

EndHud names the operators in the squad panel by comparing each portrait with every operator's
wiki avatar. The two are different art: the panel shows the game's 3D model, the wiki shows a
drawing. Comparing pixels (EndHud's first approach) got 0 of 60 squad slots right on the test
recordings. A model trained to tell anime characters apart across different art gets all of them.

The model is **CCIP** (Contrastive Character Image Pretraining) by DeepGHS:
[deepghs/ccip_onnx](https://huggingface.co/deepghs/ccip_onnx), variant
`ccip-caformer-24-randaug-pruned`, licensed under
[CreativeML OpenRAIL](https://huggingface.co/spaces/CompVis/stable-diffusion-license) (see
[THIRD-PARTY-NOTICES.md](../../THIRD-PARTY-NOTICES.md)). It isn't game art, so it ships with EndHud,
compiled into `EndHud.Vision.dll`.

| File (in `src/EndHud.Vision/Assets/ccip/`) | What it is |
|---|---|
| `ccip-features-int8.onnx` (38 MB) | Image → 768 numbers describing the character. Weights quantized to 8 bits from the 150 MB original. |
| `ccip-metrics.onnx` (2 KB) | Those numbers for N images → how different each pair is (0 = same character). |

Both are stored with Git LFS. `prepare.py` rebuilds them from the original (pinned to a commit),
and prints their SHA-256 for comparison:

```
python -m venv .venv
.venv/Scripts/pip install onnx onnxruntime==1.20.1
.venv/Scripts/python tools/ccip-model/prepare.py
```

## How EndHud uses it

See [`CharacterModel`](../../src/EndHud.Vision/Operators/CharacterModel.cs) and
[`SquadIdentifier`](../../src/EndHud.Vision/Operators/SquadIdentifier.cs). In short:

- Input: RGB resized to 384×384, scaled to [-1, 1]. Avatars are composited on white.
- Each portrait is compared with each operator's wiki avatar and, when there is one, with
  EndHud's built-in description of that operator's in-game portrait (`PortraitReferences`); the
  closer counts. In-game against in-game is the same art, and much more reliable.
- Each wiki avatar is first shrunk to the portrait's size on the player's screen, so both images
  are equally blurry. Without that, 1600×900 got 36 of 53 portraits right; with it, 47.
- One frame can mislead (the HUD dims, the low-HP tint turns everything pink), so each slot keeps
  its last few readings and names the operator that's closest over all of them. On the
  recordings that's every slot right at every simulated screen size.
- The model's own "same character" threshold doesn't separate operators here (a different
  operator can score under it), so EndHud picks the closest operator rather than trusting a cut-off.
