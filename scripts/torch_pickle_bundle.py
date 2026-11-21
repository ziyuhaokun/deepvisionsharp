"""Read PyTorch zip/pickle checkpoints and write TorchSharp tensor bundles.

TorchSharp 0.107 exposes only `torch.load` (a single tensor) and `Module.load` (its own
format), so a PyTorch checkpoint is unpacked here with the standard library plus numpy --
no torch install is involved -- and rewritten as a flat bundle:

    <out>/index.json     {"arch": "...", "tensors": [{"name","shape","dtype","file"}]}
    <out>/0001.bin ...   raw little-endian C-contiguous tensor bytes

Two callers sit on top of this module: `convert_torchvision_weights.py` reads a plain
state dict, `convert_ultralytics_weights.py` reads a whole Ultralytics checkpoint whose
pickle references classes (modules, ultralytics config objects) that do not exist here.
`load_archive(..., allow_placeholders=...)` covers both: those classes are replaced by inert
stand-ins that only keep the state the loader later walks.
"""

import builtins
import collections
import json
import os
import pickle
import zipfile

import numpy as np

# The C# bundle reader understands these two element types only; everything else is cast.
FLOAT_DTYPE = np.float32
INT_DTYPE = np.int64


class Placeholder:
    """Stand-in for a class from torch/ultralytics/torchvision that cannot be imported here.

    Only the module tree matters to the caller, so the object just has to accept whatever
    constructor arguments and `__setstate__` payload the pickle hands it.
    """

    def __init__(self, *_args, **_kwargs):
        pass

    def __setstate__(self, state):
        if isinstance(state, dict):
            self.__dict__.update(state)
        else:
            self.__dict__["_state"] = state


def storage_type_name(storage_type):
    if isinstance(storage_type, str):
        return storage_type
    return getattr(storage_type, "__name__", str(storage_type))


def storage_dtype(storage_type_name_value, numel, byte_count):
    """Infer the numpy element type of a storage, whose dtype is not recorded in the archive."""
    lowered = storage_type_name_value.lower()
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


def read_storage(archive, prefix, storage_id):
    """Materialise one persistent storage id as (type name, element count, raw bytes)."""
    storage_type, key, _location, numel = storage_id[1], storage_id[2], storage_id[3], storage_id[4]
    raw = archive.read(f"{prefix}/data/{key}")
    return (storage_type_name(storage_type), int(numel), raw)


def _rebuild_tensor(storage, storage_offset, size, stride, *_rest):
    storage_name, numel, raw = storage
    dtype = storage_dtype(storage_name, numel, len(raw))
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


class ArchiveUnpickler(pickle.Unpickler):
    def __init__(self, stream, archive, prefix, allow_placeholders):
        super().__init__(stream)
        self._archive = archive
        self._prefix = prefix
        self._allow_placeholders = allow_placeholders

    def persistent_load(self, pid):
        if pid[0] != "storage":
            raise pickle.UnpicklingError(f"unsupported persistent id: {pid[0]}")
        return read_storage(self._archive, self._prefix, pid)

    def find_class(self, module, name):
        if module.startswith("torch") and name in ("_rebuild_tensor_v2", "_rebuild_tensor"):
            return _rebuild_tensor
        if module.startswith("torch") and name == "_rebuild_parameter":
            return _rebuild_parameter
        if module == "collections":
            return getattr(collections, name)
        if module in ("builtins", "__builtin__"):
            return builtins.__dict__[name]
        if self._allow_placeholders:
            return type(name, (Placeholder,), {})
        # torch dtype / device markers are not needed by the tensor data itself.
        if module.startswith("torch"):
            return type(name, (), {})
        raise pickle.UnpicklingError(f"unsupported class: {module}.{name}")


def load_archive(path, allow_placeholders=False):
    """Unpickle a PyTorch zip checkpoint; tensors come back as numpy arrays."""
    with zipfile.ZipFile(path) as archive:
        pickles = [
            name for name in archive.namelist() if name == "data.pkl" or name.endswith("/data.pkl")
        ]
        if not pickles:
            raise SystemExit(f"{path} 不是 PyTorch zip 检查点（没有 data.pkl）")
        pickle_name = pickles[0]
        prefix = pickle_name[: -len("/data.pkl")] if "/" in pickle_name else ""
        with archive.open(pickle_name) as stream:
            return ArchiveUnpickler(stream, archive, prefix, allow_placeholders).load()


def write_bundle(tensors, out_dir, arch):
    """Write {name: array} as a bundle; returns the index entries written."""
    os.makedirs(out_dir, exist_ok=True)
    entries = []
    for index, (name, array) in enumerate(sorted(tensors.items())):
        array = _normalise(array, name)
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

    return entries


def _normalise(array, name):
    array = np.asarray(array)
    if array.dtype == np.float16 or array.dtype == np.float64:
        array = array.astype(FLOAT_DTYPE)
    elif array.dtype.kind in "iu" and array.dtype != INT_DTYPE:
        array = array.astype(INT_DTYPE)
    if array.dtype not in (FLOAT_DTYPE, INT_DTYPE):
        raise SystemExit(f"{name}: 暂不支持的权重类型 {array.dtype}")
    return np.ascontiguousarray(array)


def total_values(entries):
    return sum(int(np.prod(entry["shape"])) if entry["shape"] else 1 for entry in entries)
