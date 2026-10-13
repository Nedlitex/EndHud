"""
Rebuilds src/EndHud.Vision/Assets/ccip/ from the published CCIP model (see README.md).

    python -m venv .venv && .venv/Scripts/pip install -r requirements.txt
    .venv/Scripts/python tools/ccip-model/prepare.py
"""
import hashlib
import os
import shutil
import tempfile
import urllib.request

from onnxruntime.quantization import QuantType, quantize_dynamic

# deepghs/ccip_onnx on Hugging Face, pinned to a commit so the download never changes under us.
REPO = "https://huggingface.co/deepghs/ccip_onnx/resolve/eb2acdd29af1703388d3d0c04221add322bc9110"
MODEL = "ccip-caformer-24-randaug-pruned"
OUT = os.path.join(os.path.dirname(__file__), "..", "..", "src", "EndHud.Vision", "Assets", "ccip")


def download(name, target):
    request = urllib.request.Request(f"{REPO}/{MODEL}/{name}", headers={"User-Agent": "EndHud tools/ccip-model"})
    with urllib.request.urlopen(request) as response, open(target, "wb") as f:
        shutil.copyfileobj(response, f)


with tempfile.TemporaryDirectory() as work:
    features = os.path.join(work, "model_feat.onnx")
    download("model_feat.onnx", features)
    # Weights to 8-bit: a quarter of the size (38 MB instead of 150 MB), the same answers on the
    # recordings, and the right operator ahead by a wider margin.
    quantize_dynamic(features, os.path.join(OUT, "ccip-features-int8.onnx"), weight_type=QuantType.QUInt8)
    download("model_metrics.onnx", os.path.join(OUT, "ccip-metrics.onnx"))

for name in sorted(os.listdir(OUT)):
    with open(os.path.join(OUT, name), "rb") as f:
        print(hashlib.sha256(f.read()).hexdigest(), name)
