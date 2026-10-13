# Third-party notices

EndHud includes the following third-party components.

## CCIP character recognition model

- **What:** `ccip-caformer-24-randaug-pruned` from [deepghs/ccip_onnx](https://huggingface.co/deepghs/ccip_onnx)
  (CCIP, Contrastive Character Image Pretraining, by DeepGHS), with its weights quantized to 8 bits
  by EndHud (see [tools/ccip-model](tools/ccip-model/README.md)). Compiled into `EndHud.Vision.dll`.
- **Used for:** naming the operators in the squad panel by comparing each portrait with the
  operators' avatars.
- **License:** OpenRAIL, as declared by the model's authors on its model card
  ([about OpenRAIL licenses](https://huggingface.co/blog/open_rail)). OpenRAIL licenses permit
  use and redistribution, and carry use-based restrictions that pass on to anyone who uses or
  redistributes the model: it may not be used to break the law, to harm people, or for the
  other purposes the license prohibits. EndHud uses it only to recognise game characters on the
  player's own screen.

## ONNX Runtime

- **What:** [Microsoft.ML.OnnxRuntime](https://github.com/microsoft/onnxruntime), which runs the
  model above.
- **License:** MIT License, Copyright (c) Microsoft Corporation.

## Operator art

EndHud ships no game art. Operator avatars and skill icons are downloaded on first start from
the community [Arknights: Endfield Wiki](https://endfield.wiki.gg) to the player's own computer;
they belong to the game's developer and publisher.
