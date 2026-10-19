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

EndHud carries an operator package (`operators/operator-library.zip`): operator avatars, skill
icons, Chinese names and enemy status icons, taken from the community
[Endfield Talos Wiki](https://endfield.wiki.gg), whose content is licensed under
[CC BY-SA 4.0](https://creativecommons.org/licenses/by-sa/4.0). The images are the game's art and
belong to the game's developer and publisher; EndHud uses them only to recognise operators on the
player's own computer.

The package also carries the inn's data (`inn/inn-data.json`). The operators' hobbies and
elements come from the same wiki. The dishes' prices, cook times, traits, ingredients and the
themes are facts of the game as compiled by
[EndfieldHub](https://endfieldhub.org/guides/yuanyuan-inn-recipes-chefs-themes); their Chinese
names are as the game shows them. The dish and drink pictures are the game's art, as published by
EndfieldHub; they belong to the game's developer and publisher.
