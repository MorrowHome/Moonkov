"""Compile the reachable Blender material graphs; fail on every unsupported operation.
Geometry payload is material-specific. No viewport-colour or generic-Lit fallback.
"""
import json,pathlib,hashlib,collections,re
ROOT=pathlib.Path(__file__).resolve().parent
PROJECT=ROOT.parents[2]
IR=ROOT/'MaterialIR/MaterialIR.json'
OUT=PROJECT/'Assets/MoonEnvironment/Variants/SourceFidelity/Generated'
TYPES={'VALUE':'float','INT':'float','BOOLEAN':'float','VECTOR':'float3','RGBA':'float4'}
def constant(v,t):
 if v is None:v=0
 if isinstance(v,bool):return '1.0' if v else '0.0'
 if isinstance(v,(int,float)):
  text=format(float(v),'.9g');text+=('' if '.' in text or 'e' in text else '.0');return '('+text+')' if text.startswith('-') else text
 return f'{t}('+','.join(constant(x,'float') for x in v[:int(t[-1])])+')'
def convert(e,a,b):
 if a==b:return e
 if b=='float':return f'SourceLuminance(({e}).xyz)' if a=='float4' else f'SourceMean({e})'
 if a=='float':return f'{b}({e},{e},{e}'+(',1)' if b=='float4' else ')')
 if b=='float3':return f'({e}).xyz'
 return f'float4({e},1)'
class Compiler:
 def __init__(self,name,m,index,images):
  self.name=name;self.m=m;self.index=index;self.nodes={n['id']:n for n in m['nodes']};self.links={tuple(l['to']):tuple(l['from']) for l in m['links']};self.cache={};self.lines=[];self.textures=[];self.images=images
  named=sorted({n.get('uv_map','') for n in m['nodes'] if n['type'] in {'UVMAP','NORMAL_MAP'} and n.get('uv_map','')});active=[u for u in m.get('active_uv_names',[]) if u];primary=active[0] if len(active)==1 else '$ACTIVE';self.uvs=[primary]+[uv for uv in named if uv!=primary];self.tangent_uv=next((n.get('uv_map') or '$ACTIVE' for n in m['nodes'] if n['type']=='NORMAL_MAP'),'$ACTIVE')
  if len(self.uvs)>3:raise ValueError('Too many UV layers '+name)
  self.attrs=sorted({n['attribute_name'] for n in m['nodes'] if n['type']=='ATTRIBUTE'});self.attr_layout={};offset=0
  for attr in self.attrs:
   size=3 if attr=='R37_Cloud_Terrain_Normal' else 1;self.attr_layout[attr]={'offset':offset,'size':size};offset+=size
  if offset>8:raise ValueError('Too many source attributes '+name)
 def emit(self,e,t):
  v='v'+str(len(self.lines));self.lines.append(f'{t} {v}={e};');return v,t
 def input(self,n,key,t=None):
  s=next(s for s in n['inputs'] if s['id']==key);a=TYPES.get(s['type'],s['type']);t=t or a
  link=self.links.get((n['id'],key))
  if link:e,typ=self.output(*link)
  else:e,typ=constant(s['value'],a),a
  return convert(e,typ,t)
 def linked(self,n,key):return (n['id'],key) in self.links
 def uv(self,name):return f'c.uv{self.uvs.index(name)}' if name in self.uvs else 'c.uv0'
 def output(self,node_id,socket):
  k=(node_id,socket)
  if k in self.cache:return self.cache[k]
  n=self.nodes[node_id];kind=n['type'];out=next(s for s in n['outputs'] if s['id']==socket);t=TYPES[out['type']]
  get=lambda key,typ=None:self.input(n,key,typ)
  if kind=='VALUE':e=constant(out['value'],t)
  elif kind=='NEW_GEOMETRY':e={'Position':'c.position','Normal':'c.normal'}[socket]
  elif kind=='TEX_COORD':
   if socket=='Object' and 'object_inverse_world' in n:
    mat=n['object_inverse_world'];e='float3('+','.join('dot(float4(c.position,1),float4('+','.join(constant(x,'float') for x in row)+'))' for row in mat[:3])+')'
   else:e={'Object':'c.local','Generated':'c.generated','UV':'float3(c.uv0,0)','Normal':'c.normal'}[socket]
  elif kind=='UVMAP':e=f'float3({self.uv(n["uv_map"])},0)'
  elif kind=='OBJECT_INFO':e={'Random':'c.random'}[socket]
  elif kind=='CAMERA':e={'View Distance':'c.viewDistance'}[socket]
  elif kind=='LIGHT_PATH':e={'Is Shadow Ray':'0.0'}[socket]
  elif kind=='ATTRIBUTE':
   layout=self.attr_layout[n['attribute_name']];comps=[]
   for j in range(layout['size']):
    pos=layout['offset']+j;comps.append(f'c.attributes{pos//4}.{"xyzw"[pos%4]}')
   raw=comps[0] if len(comps)==1 else 'float3('+','.join(comps)+')';e=convert(raw,'float' if len(comps)==1 else 'float3',t)
  elif kind in {'SEPXYZ','SEPARATE_COLOR'}:
   key='Vector' if kind=='SEPXYZ' else 'Color';com={'X':'x','Y':'y','Z':'z','Red':'r','Green':'g','Blue':'b','Alpha':'a'}[socket];e=f'({get(key)}).{com}'
  elif kind in {'COMBXYZ','COMBINE_COLOR'}:
   keys=['X','Y','Z'] if kind=='COMBXYZ' else ['Red','Green','Blue'];e=t+'('+','.join(get(x) for x in keys)+(',1)' if kind=='COMBINE_COLOR' else ')')
  elif kind=='TEX_NOISE':
   assert n['noise_type']=='FBM' and n['noise_dimensions'] in {'3D','4D'}
   assert not self.linked(n,'Distortion') and next(s['value'] for s in n['inputs'] if s['id']=='Distortion')==0
   p=get('Vector') if self.linked(n,'Vector') else 'c.generated'
   e='SourceFBM('+','.join([p,get('W'),get('Scale'),get('Detail'),get('Roughness'),get('Lacunarity'),'true' if n['noise_dimensions']=='4D' else 'false','true' if n.get('normalize',True) else 'false'])+')'
   if socket=='Color':e='SourceNoiseColor('+','.join([p,get('W'),get('Scale'),get('Detail'),get('Roughness'),get('Lacunarity'),'true' if n['noise_dimensions']=='4D' else 'false','true' if n.get('normalize',True) else 'false'])+')'
  elif kind=='TEX_WHITE_NOISE':
   assert socket=='Value' and n['noise_dimensions'] in {'1D','3D','4D'}
   e=f'SourceWhiteNoise1({get("W")})' if n['noise_dimensions']=='1D' else f'SourceWhiteNoise({get("Vector")},{get("W")},{"true" if n["noise_dimensions"]=="4D" else "false"})'
  elif kind=='MATH':
   a=get('Value');b=get('Value_001') if any(s['id']=='Value_001' for s in n['inputs']) else '0';z=get('Value_002') if any(s['id']=='Value_002' for s in n['inputs']) else '0';op=n['operation']
   expr={'MULTIPLY':f'({a}*{b})','SUBTRACT':f'({a}-{b})','ADD':f'({a}+{b})','MAXIMUM':f'max({a},{b})','MINIMUM':f'min({a},{b})','ABSOLUTE':f'abs({a})','DIVIDE':f'SourceDivide({a},{b})','LESS_THAN':f'float({a}<{b})','GREATER_THAN':f'float({a}>{b})','POWER':f'SourcePower({a},{b})','MULTIPLY_ADD':f'({a}*{b}+{z})','EXPONENT':f'exp({a})','FLOOR':f'floor({a})','MODULO':f'SourceModulo({a},{b})','SQRT':f'sqrt(max({a},0))','PINGPONG':f'SourcePingPong({a},{b})'}
   e=expr[op]
   if n.get('use_clamp'):e=f'saturate({e})'
  elif kind=='VECT_MATH':
   a=get('Vector');b=get('Vector_001');op=n['operation'];e={'SUBTRACT':f'({a}-{b})','ADD':f'({a}+{b})','MULTIPLY':f'({a}*{b})','SCALE':f'({a}*{get("Scale")})','DOT_PRODUCT':f'dot({a},{b})','DISTANCE':f'distance({a},{b})','NORMALIZE':f'SourceSafeNormal({a})','LENGTH':f'length({a})'}[op]
  elif kind=='MAP_RANGE':
   assert n['data_type']=='FLOAT';a=get('Value');low=get('From Min');high=get('From Max');x=f'SourceDivide(({a}-{low}),({high}-{low}))'
   if n.get('clamp'):x=f'saturate({x})'
   it=n['interpolation_type']
   if it=='SMOOTHSTEP':x=f'(({x})*({x})*(3-2*({x})))'
   elif it=='SMOOTHERSTEP':x=f'(({x})*({x})*({x})*(({x})*(({x})*6-15)+10))'
   elif it=='STEPPED':x=f'SourceDivide(floor(({x})*({get("Steps")}+1)),{get("Steps")})'
   elif it!='LINEAR':raise ValueError(it)
   e=f'lerp({get("To Min")},{get("To Max")},{x})'
  elif kind=='MIX_RGB':
   fac=get('Fac');a=get('Color1');b=get('Color2');op=n['blend_type']
   blended={'MIX':b,'MULTIPLY':f'({a}*{b})','DIVIDE':f'float4(SourceDivide(({a}).rgb,({b}).rgb),({a}).a)'}[op]
   e=f'lerp({a},{blended},saturate({fac}))'
   if n.get('use_clamp'):e=f'saturate({e})'
  elif kind=='VALTORGB':
   ramp=n['ramp'];assert ramp['interpolation']=='LINEAR' and ramp['mode']=='RGB';f=get('Fac');elements=ramp['elements'];e=constant(elements[-1][1],'float4')
   for (p,a),(q,b) in reversed(list(zip(elements,elements[1:]))):
    blend=f'lerp({constant(a,"float4")},{constant(b,"float4")},saturate(({f}-{constant(p,"float")})/{constant(max(q-p,1e-20),"float")}))'
    e=f'(({f})<={constant(q,"float")}?{blend}:{e})'
   if socket=='Alpha':e=f'({e}).a'
  elif kind=='TEX_IMAGE':
   assert n['projection']=='FLAT';image=n['image'];slot=next((i for i,x in enumerate(self.textures) if x['node']==n['id']),None)
   if slot is None:
    slot=len(self.textures);self.textures.append({'node':n['id'],'image':image,'extension':n['extension'],'interpolation':n['interpolation']})
   uv=f'({get("Vector")}).xy' if self.linked(n,'Vector') else 'c.uv0';wrap='REPEAT' if n['extension']=='REPEAT' else 'CLAMP';sampler=next(i for i,tex in enumerate(self.textures) if ('REPEAT' if tex['extension']=='REPEAT' else 'CLAMP')==wrap);sample=f'SAMPLE_TEXTURE2D(_SourceTex{slot},sampler_SourceTex{sampler},{uv})'
   if n['extension']=='CLIP':sample=f'({sample}*float(all({uv}>=0)&&all({uv}<=1)))'
   if socket=='Alpha':e=f'({sample}).a'
   else:e=sample
  elif kind=='NORMAL_MAP':
   if n['space']=='TANGENT':e=f'SourceTangentNormal(c.normal,({get("Color")}).rgb,{get("Strength")},c.position,{self.uv(n["uv_map"])},c.tangent)'
   elif n['space']=='WORLD':e=f'SourceSafeNormal(lerp(c.normal,({get("Color")}).rgb*2-1,saturate({get("Strength")})))'
   else:raise ValueError('Unsupported source normal space '+n['space'])
  elif kind=='BUMP':
   normal=get('Normal') if self.linked(n,'Normal') else 'c.normal';e=f'SourceBump({normal},{get("Height")},{get("Strength")},{get("Distance")},c.position,{"true" if n.get("invert") else "false"})'
  elif kind=='AMBIENT_OCCLUSION':
   assert not n.get('inside');e='c.ao.x' if socket=='AO' else f'({get("Color")}*c.ao.x)'
  elif kind=='FRESNEL':
   normal=get('Normal') if self.linked(n,'Normal') else 'c.normal';e=f'SourceFresnel({normal},c.view,{get("IOR")})'
  else:raise ValueError('Unsupported source node/output '+self.name+' / '+kind+' / '+socket)
  self.cache[k]=self.emit(e,t);return self.cache[k]
 def closure(self,node_id):
  n=self.nodes[node_id]
  if n['type']=='MIX_SHADER':
   fac=self.input(n,'Fac');left=self.links.get((node_id,'Shader'));right=self.links.get((node_id,'Shader_001'));assert left and right
   a=self.closure(left[0]);b=self.closure(right[0])
   if a['transparent']:return {'a':b['a'],'b':b.get('b'), 'mix':b.get('mix','0'),'alpha':f'({b.get("alpha","1")}*{fac})','transparent':False}
   if b['transparent']:return {'a':a['a'],'b':a.get('b'),'mix':a.get('mix','0'),'alpha':f'({a.get("alpha","1")}*(1-{fac}))','transparent':False}
   assert not a.get('b') and not b.get('b');return {'a':a['a'],'b':b['a'],'mix':fac,'alpha':'1','transparent':False}
  if n['type']=='BSDF_TRANSPARENT':return {'transparent':True}
  assert n['type']=='BSDF_PRINCIPLED'
  v='b'+str(len(self.lines));self.lines.append(f'SourceClosure {v}=SourceDefaultClosure(c);')
  props={'Base Color':('baseColor','float3'),'Metallic':('metallic','float'),'Roughness':('roughness','float'),'IOR':('ior','float'),'Specular IOR Level':('specular','float'),'Coat Weight':('coat','float'),'Coat Roughness':('coatRoughness','float'),'Transmission Weight':('transmission','float'),'Alpha':('alpha','float'),'Anisotropic':('anisotropy','float'),'Anisotropic Rotation':('anisotropicRotation','float')}
  for key,(field,typ) in props.items():
   value=self.input(n,key,typ);self.lines.append(f'{v}.{field}={value};')
  for key,field in [('Normal','normal'),('Coat Normal','coatNormal')]:
   if self.linked(n,key):self.lines.append(f'{v}.{field}={self.input(n,key)};')
  emission=self.input(n,'Emission Color','float3');strength=self.input(n,'Emission Strength');self.lines.append(f'{v}.emission={emission}*{strength};')
  # Explicitly reject additional physical lobes rather than silently dropping them.
  for key in ['Sheen Weight','Subsurface Weight','Thin Film Thickness']:
   s=next((s for s in n['inputs'] if s['id']==key),None)
   if s and (self.linked(n,key) or s['value']!=0):raise ValueError('Unsupported physical lobe '+self.name+' '+key)
  return {'a':v,'b':None,'mix':'0','alpha':'1','transparent':False}
 def run(self):
  output=self.nodes[self.m['output']];link=self.links[(output['id'],'Surface')];surface=self.closure(link[0]);assert not surface['transparent']
  body='\n'.join(self.lines)+f'\na={surface["a"]}; b={surface.get("b") or surface["a"]}; factor={surface.get("mix","0")}; a.alpha*={surface.get("alpha","1")}; b.alpha*={surface.get("alpha","1")};'
  samplers={};
  for i,texture in enumerate(self.textures):samplers.setdefault('REPEAT' if texture['extension']=='REPEAT' else 'CLAMP',i)
  text='\n'.join(f'TEXTURE2D(_SourceTex{i});' for i in range(len(self.textures)))+'\n'+'\n'.join(f'SAMPLER(sampler_SourceTex{i});' for i in samplers.values())+'\nvoid EvaluateSourceMaterial(SourceContext c,out SourceClosure a,out SourceClosure b,out float factor)\n{\n'+body+'\n}\n'
  return text,{'id':self.index,'name':self.name,'uv_names':self.uvs,'tangent_uv':self.tangent_uv,'attribute_layout':self.attr_layout,'textures':self.textures,'two_closures':bool(surface.get('b')),'transparent':surface.get('alpha','1')!='1' or any(n['type']=='BSDF_PRINCIPLED' and any(((v['id']=='Transmission Weight' and v['value']!=0) or (v['id']=='Alpha' and v['value']!=1) or (v['id'] in ('Transmission Weight','Alpha') and self.linked(n,v['id']))) for v in n['inputs']) for n in self.nodes.values()),'node_count':len(self.nodes)}
def main():
 data=json.loads(IR.read_text());OUT.mkdir(parents=True,exist_ok=True);rows=[];errors=[]
 for index,(name,m) in enumerate(data['materials'].items()):
  try:
   text,row=Compiler(name,m,index,data['images']).run();path=OUT/f'Material_{index:03d}.hlsl';path.write_text(text);row['path']=str(path.relative_to(PROJECT));rows.append(row)
  except Exception as exc:errors.append({'material':name,'error':str(exc)})
 report={'source_sha256':data['source_sha256'],'materials':rows,'errors':errors,'images':data['images']}
 (ROOT/'MaterialIR/CompiledMaterials.json').write_text(json.dumps(report,ensure_ascii=False,indent=2))
 print(json.dumps({'compiled':len(rows),'errors':errors},ensure_ascii=False))
 if errors:raise SystemExit(1)
if __name__=='__main__':main()
