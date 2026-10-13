# Umbra 08 source and runtime derivative

Delivered source: `E:/Download/Umbra_08_Editable_Variant.blend`, copied unchanged as `Umbra_08_Editable_Variant.blend`.

SHA-256: `FD9058D1F63D8D63A89D54DCD121B4F49E676952236A1016B5FBB1E697D480EE`.

The exporter uses Blender 5.1.1, the visible fused mesh `V4_LiquidSkin_ConstrictedMantle`, and source Bezier controls 1/6/11 as the hip/knee/toe anchors. Hidden construction parts and studio objects are excluded. Embedded source scripts are not executed; the original file is never saved or overwritten.

Rebuild from the repository root in PowerShell:

```powershell
& D:/Blender51/blender.exe --factory-startup --background --disable-autoexec SourceData/Models/Umbra08Liquid/Umbra_08_Editable_Variant.blend --python Tools/Models/export_umbra_liquid.py -- Assets/UmbrellaSpiderLab/Resources/Umbra08Liquid.bytes
```

Source coordinates already use literal Unity axes (+Y outward/up, +Z forward); the binary mesh preserves those axes and metre units without Blender/FBX conversion. `UML2` contains little-endian counts, 89 rest positions and probe radii, vertices/normals/contact flow masks, triangle indices, four bone indices and four weights per vertex. Nearest centreline interpolation blends into the body around the sockets. The runtime parser owns the resulting Unity Mesh and does not alter the original source rig or existing spider skins.

The runtime mesh is a 7.5% decimated derivative of the actual sculpt: 21,620 vertices and 43,256 triangles. Both the Blender original and binary runtime payload use the repository's existing Git LFS patterns.

The **Fluid** variant samples the closed solid with BVH ray intersections instead of using the skin as its rendered surface. The fluid budget is then redistributed: retain about 45% of torso seeds and compress the torso, lightly reduce upper-leg seeds, and replace sparse distal seeds with continuous lower-leg/toe tubes following the authored centreline. Generate its 5,731 particles (2,416 in the lower legs/toes) from the exported mesh:

```powershell
& D:/Blender51/blender.exe --factory-startup --background --disable-autoexec --python Tools/Models/export_umbra_fluid.py -- Assets/UmbrellaSpiderLab/Resources/Umbra08Liquid.bytes Assets/UmbrellaSpiderLab/Resources/Umbra08Fluid.bytes
```

`UFP1` contains a particle count, local seed positions, four guidance bone indices and weights. These are initial conditions and the skeleton attraction field. At runtime the GPU owns independent particle positions and velocities, calculates neighbouring fluid forces, and reconstructs a density isosurface; it does not render the seed mesh.
