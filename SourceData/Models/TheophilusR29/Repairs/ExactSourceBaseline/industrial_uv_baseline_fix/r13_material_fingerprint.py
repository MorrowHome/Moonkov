"""Strict shader-definition fingerprint; ignores datablock/UI names and texture paths, never pixel content."""
import bpy,hashlib,json,os
PROPS=['operation','blend_type','use_clamp','clamp_type','extension','interpolation','projection','projection_blend','space','uv_map','invert','vector_type','distribution','subsurface_method','component','mode','normalization','attribute_name','from_instancer','noise_dimensions','musgrave_dimensions','voronoi_dimensions','feature','distance','wave_type','bands_direction','rings_direction','wave_profile','gradient_type','data_type','factor_mode','normalize','is_active_output','target']
def serial(x):
 if x is None or isinstance(x,(str,int,float,bool)):return x
 try:return [serial(v) for v in x]
 except TypeError:return {'ID_type':x.bl_rna.identifier,'ID_name':x.name}
def material_definition(material):
 assert material.use_nodes and material.node_tree,'Expected a node material'
 nodes=list(material.node_tree.nodes);index={n:i for i,n in enumerate(nodes)};rows=[]
 for n in nodes:
  rec={'node_type':n.bl_idname,'mute':n.mute,'input_defaults':[{'identifier':s.identifier,'value':serial(s.default_value)} for s in n.inputs if hasattr(s,'default_value')],'output_defaults':[{'identifier':s.identifier,'value':serial(s.default_value)} for s in n.outputs if hasattr(s,'default_value')],'properties':{k:serial(getattr(n,k)) for k in PROPS if hasattr(n,k)}}
  if n.type=='TEX_IMAGE':
   im=n.image;assert im,'Image node missing image'
   if im.packed_file:raw=im.packed_file.data
   else:
    path=bpy.path.abspath(im.filepath,library=im.library);assert os.path.isfile(path),'Missing image content '+path
    raw=open(path,'rb').read()
   rec['image_content']={'sha256':hashlib.sha256(raw).hexdigest(),'bytes':len(raw),'dimensions':list(im.size),'colorspace':im.colorspace_settings.name,'alpha_mode':im.alpha_mode,'source':im.source,'is_float':im.is_float}
  if n.type=='GROUP':raise AssertionError('Unexpected group node in scoped R13 fingerprint')
  if hasattr(n,'color_ramp'):
   q=n.color_ramp;rec['color_ramp']={'interpolation':q.interpolation,'color_mode':q.color_mode,'hue_interpolation':q.hue_interpolation,'elements':[[e.position,list(e.color)] for e in q.elements]}
  rows.append(rec)
 links=sorted([[index[l.from_node],l.from_socket.identifier,index[l.to_node],l.to_socket.identifier] for l in material.node_tree.links])
 return {'schema':'R13 exact shader/content fingerprint v1','material_scalars':{k:serial(getattr(material,k)) for k in ['use_nodes','diffuse_color','metallic','roughness','use_backface_culling','surface_render_method','displacement_method','alpha_threshold'] if hasattr(material,k)},'nodes':rows,'links':links}
def fingerprint(material):
 D=material_definition(material);return hashlib.sha256(json.dumps(D,sort_keys=True,separators=(',',':')).encode()).hexdigest(),D
