"""Replacing Blender material slots must retain every polygon's slot index."""
import numpy as np

MATERIAL_BINDING_VERSION = 1


def replace_materials_preserving_indices(mesh, materials):
    materials = list(materials)
    if not materials or any(material is None for material in materials):
        raise ValueError('Prepared mesh requires all original material slots')
    indices = np.empty(len(mesh.polygons), dtype=np.int32)
    mesh.polygons.foreach_get('material_index', indices)
    if len(indices) and (indices.min() < 0 or indices.max() >= len(materials)):
        raise ValueError('Evaluated polygon references an unavailable source material slot')
    # clear() resets polygon.material_index to zero, even when slots are appended again.
    mesh.materials.clear()
    for material in materials:
        mesh.materials.append(material)
    mesh.polygons.foreach_set('material_index', indices)
    check = np.empty_like(indices)
    mesh.polygons.foreach_get('material_index', check)
    if not np.array_equal(indices, check):
        raise RuntimeError('Source polygon material assignment was not preserved')
