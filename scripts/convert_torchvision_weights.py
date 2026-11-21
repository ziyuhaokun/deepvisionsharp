"""Convert a torchvision .pth state dict into a TorchSharp-friendly tensor bundle.

TorchSharp 0.107 exposes only `torch.load` (single tensor) and `Module.load` (its own
format); it cannot read a PyTorch state dict. This script unpacks the zip/pickle
archive with the standard library plus numpy -- no torch install required -- and
writes a flat bundle the C# loader can read:

    <out>/index.json     {"arch": "...", "tensors": [{"name","shape","dtype","file"}]}
    <out>/0001.bin ...   raw little-endian C-contiguous tensor bytes

Usage (dev machine, once per architecture):
    python scripts/convert_torchvision_weights.py resnet18-f37072fd.pth out/resnet18 --arch resnet18
"""

import os
import sys

import numpy as np

from torch_pickle_bundle import load_archive, total_values, write_bundle


def load_state_dict(pth_path):
    state = load_archive(pth_path)
    if not isinstance(state, dict):
        raise SystemExit(f"unexpected pickle payload: {type(state)!r}")
    return state


def main():
    if len(sys.argv) < 3:
        raise SystemExit(__doc__)
    source = sys.argv[1]
    out_dir = sys.argv[2]
    arch = sys.argv[4] if len(sys.argv) > 4 and sys.argv[3] == "--arch" else os.path.basename(out_dir)

    state = load_state_dict(source)
    tensors = {name: np.ascontiguousarray(tensor) for name, tensor in state.items()}
    entries = write_bundle(tensors, out_dir, arch)
    print(
        f"converted {len(entries)} tensors ({total_values(entries)} values) "
        f"from {source} -> {out_dir}"
    )


if __name__ == "__main__":
    main()