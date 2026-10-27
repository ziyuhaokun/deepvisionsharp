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

import collections
import json
import os
import pickle
import sys
import zipfile

import numpy as np


def _read_storage(archive, storage_id):
    storage_type, key, location, numel = storage_id[1], storage_id[2], storage_id[3], storage_id[4]
    raw = archive.read(f"archive/data/{key}")
    return (type_name(storage_type), int(numel), raw)


def type_name(storage_type):
    if isinstance(storage_type, str):
        return storage_type
    return getattr(storage_type, "__name__", str(storage_type))


def _dtype_for(storage_type_name, numel, byte_count):
    lowered = storage_type_name.lower()
    if "long" in lowered or "int64" in lowered:
        return np.int64
    if "int" in lowered or "int32" in lowered:
        return np.int32
    if "half" in lowered or "float16" in lowered:
        return np.float16
    if numel > 0 and byte_count % numel == 0:
        element = byte_count // numel
        return {1: np.uint8, 2: np.float16, 4: np.float32, 8: np.int64}.get(element, np.float32)
    return np.float32


def _rebuild_tensor(storage, storage_offset, size, stride, *_rest):
    storage_type_name, numel, raw = storage
    dtype = _dtype_for(storage_type_name, numel, len(raw))
    flat = np.frombuffer(raw, dtype=dtype, count=numel)
    shape = tuple(int(value) for value in size)
    strides = tuple(int(value) for value in stride)
    view = np.lib.stride_tricks.as_strided(
        flat[storage_offset:],
        shape=shape,
        strides=tuple(step * np.dtype(dtype).itemsize for step in strides),
    )
    return np.ascontiguousarray(view)


def _rebuild_parameter(data, *_rest):
    return data


class _Unpickler(pickle.Unpickler):
    def __init__(self, stream, archive):
        super().__init__(stream)
        self._archive = archive

    def persistent_load(self, pid):
        if pid[0] != "storage":
            raise pickle.UnpicklingError(f"unsupported persistent id: {pid[0]}")
        return _read_storage(self._archive, pid)

    def find_class(self, module, name):
        if module.startswith("torch") and name in ("_rebuild_tensor_v2", "_rebuild_tensor"):
            return _rebuild_tensor
        if module.startswith("torch") and name == "_rebuild_parameter":
            return _rebuild_parameter
        if module in ("collections", "builtins"):
            return getattr(collections, name) if module == "collections" else __import__("builtins").__dict__[name]
        # torch dtype / device markers are not needed by the tensor data itself.
        if module.startswith("torch"):
            return type(name, (), {})
        raise pickle.UnpicklingError(f"unsupported class: {module}.{name}")


def load_state_dict(pth_path):
    with zipfile.ZipFile(pth_path) as archive:
        with archive.open("archive/data.pkl") as stream:
            state = _Unpickler(stream, archive).load()
    if not isinstance(state, (dict, collections.OrderedDict)):
        raise SystemExit(f"unexpected pickle payload: {type(state)!r}")
    return state


def main():
    if len(sys.argv) < 3:
        raise SystemExit(__doc__)
    source = sys.argv[1]
    out_dir = sys.argv[2]
    arch = sys.argv[4] if len(sys.argv) > 4 and sys.argv[3] == "--arch" else os.path.basename(out_dir)
    os.makedirs(out_dir, exist_ok=True)

    state = load_state_dict(source)
    entries = []
    for index, (name, tensor) in enumerate(sorted(state.items())):
        array = np.ascontiguousarray(tensor)
        file_name = f"{index:04d}.bin"
        array.tofile(os.path.join(out_dir, file_name))
        entries.append(
            {
                "name": name,
                "shape": [int(value) for value in array.shape],
                "dtype": str(array.dtype),
                "file": file_name,
            }
        )

    with open(os.path.join(out_dir, "index.json"), "w", encoding="utf-8") as handle:
        json.dump({"arch": arch, "tensors": entries}, handle, indent=2)

    total = sum(int(np.prod(item["shape"])) for item in entries)
    print(f"converted {len(entries)} tensors ({total} values) from {source} -> {out_dir}")


if __name__ == "__main__":
    main()
