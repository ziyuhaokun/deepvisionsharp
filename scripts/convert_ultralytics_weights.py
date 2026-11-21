"""Convert an Ultralytics .pt checkpoint into a TorchSharp-friendly tensor bundle.

An Ultralytics checkpoint is not a state dict: it is a pickled training result holding the
whole `model` (and usually an `ema` copy) whose pickle references ultralytics/torchvision
classes that do not exist on this machine. `torch_pickle_bundle.load_archive` stands those
classes in with inert placeholders, so the module tree can still be walked here; the weights
themselves are plain torch storages and are read as numpy arrays.

Output is the same bundle `convert_torchvision_weights.py` writes:
    <out>/index.json     {"arch": "...", "tensors": [{"name","shape","dtype","file"}]}
    <out>/0001.bin ...   raw little-endian C-contiguous float32/int64 tensor bytes

Key names keep the Ultralytics spelling (`model.0.conv.weight`, `model.23.cv3.0.0.0.conv.weight`);
the C# loader maps them onto the hand-written TorchSharp modules.

Usage (dev machine, once per architecture):
    python scripts/convert_ultralytics_weights.py yolo26n.pt out/yolo26n --arch yolo26n
    python scripts/convert_ultralytics_weights.py yolo26n-cls.pt out/yolo26n-cls --arch yolo26n-cls
"""

import os
import sys

import numpy as np

from torch_pickle_bundle import load_archive, total_values, write_bundle


def collect_state_dict(module, prefix="", destination=None):
    """Flatten `_parameters`/`_buffers`/`_modules` exactly like `nn.Module.state_dict()`."""
    if destination is None:
        destination = {}
    for name, value in (getattr(module, "_parameters", None) or {}).items():
        if isinstance(value, np.ndarray):
            destination[f"{prefix}{name}"] = value
    for name, value in (getattr(module, "_buffers", None) or {}).items():
        if isinstance(value, np.ndarray):
            destination[f"{prefix}{name}"] = value
    for name, child in (getattr(module, "_modules", None) or {}).items():
        collect_state_dict(child, f"{prefix}{name}.", destination)
    return destination


def pick_weights(payload):
    """Prefer the EMA copy, which is what Ultralytics validates and exports."""
    for key in ("ema", "model"):
        candidate = payload.get(key) if isinstance(payload, dict) else None
        if candidate is not None and hasattr(candidate, "_modules"):
            return key, candidate
    raise SystemExit("checkpoint 里既没有 ema 也没有 model")


def main():
    if len(sys.argv) < 3:
        raise SystemExit(__doc__)
    source = sys.argv[1]
    out_dir = sys.argv[2]
    arch = sys.argv[4] if len(sys.argv) > 4 and sys.argv[3] == "--arch" else os.path.basename(out_dir)

    payload = load_archive(source, allow_placeholders=True)
    origin, root = pick_weights(payload)
    tensors = collect_state_dict(root)
    if not tensors:
        raise SystemExit(f"{source}: 没有解出任何张量")
    if not any(name.startswith("model.") for name in tensors):
        raise SystemExit(f"{source}: 权重键名不是 model.*，无法对应到 YOLO26 模块")

    entries = write_bundle(tensors, out_dir, arch)
    print(
        f"converted {len(entries)} tensors ({total_values(entries)} values) from {source} "
        f"[{origin}] -> {out_dir}"
    )


if __name__ == "__main__":
    main()