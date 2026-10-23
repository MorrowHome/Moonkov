"""Moonkov physical item authoring. Execute in a dedicated Blender scene via MCP.
Outputs are staged outside Assets until Unity import is requested. Units: metres.
"""
import bpy, math, json, pathlib, random
from mathutils import Vector
import os
ROOT=pathlib.Path(os.environ.get('MOONKOV_PROJECT', str(pathlib.Path(__file__).resolve().parents[2])))
OUT=pathlib.Path(os.environ.get('MOONKOV_ITEM_OUTPUT', '/tmp/moonkov-item-art')); OUT.mkdir(exist_ok=True)
MANIFEST=json.loads((ROOT/'Tools/Art/item_manifest.json').read_text())
M={}; PARTS=[]
def material(key,color,metal=0,rough=.45):
 m=bpy.data.materials.get('Item_'+key) or bpy.data.materials.new('Item_'+key);m.diffuse_color=(*color,1);m.use_nodes=True
 p=m.node_tree.nodes.get('Principled BSDF');p.inputs['Base Color'].default_value=(*color,1);p.inputs['Metallic'].default_value=metal;p.inputs['Roughness'].default_value=rough
 nodes=m.node_tree.nodes;links=m.node_tree.links
 noise=nodes.new('ShaderNodeTexNoise');noise.inputs['Scale'].default_value=185;noise.inputs['Detail'].default_value=2
 bump=nodes.new('ShaderNodeBump');bump.inputs['Strength'].default_value=.18;bump.inputs['Distance'].default_value=.00025
 links.new(noise.outputs['Fac'],bump.inputs['Height']);links.new(bump.outputs['Normal'],p.inputs['Normal'])
 ramp=nodes.new('ShaderNodeMapRange');ramp.inputs['To Min'].default_value=max(.08,rough-.08);ramp.inputs['To Max'].default_value=min(.95,rough+.12)
 links.new(noise.outputs['Fac'],ramp.inputs['Value']);links.new(ramp.outputs['Result'],p.inputs['Roughness'])
 M[key]=m;return m
def setup():
 if bpy.data.filepath and pathlib.Path(bpy.data.filepath).parent not in [OUT,ROOT/'SourceData/Models/PhysicalItems']:
  raise RuntimeError('Use a fresh dedicated Blender scene; refusing to replace an unrelated file.')
 bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
 for k,c,m,r in [('ceramic',(.57,.61,.58),0,.36),('dark',(.028,.04,.045),0,.52),('steel',(.38,.44,.47),1,.28),('gold',(.58,.31,.08),1,.32),('copper',(.62,.23,.09),1,.3),('red',(.42,.045,.035),0,.4),('blue',(.06,.28,.39),0,.3),('green',(.10,.25,.15),0,.48),('amber',(.75,.39,.05),0,.4),('violet',(.30,.15,.40),0,.35),('tissue',(.28,.055,.12),0,.26),('bone',(.66,.57,.35),0,.42)]:material(k,c,m,r)
 scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.samples=24;scene.cycles.use_denoising=True;scene.render.threads_mode='FIXED';scene.render.threads=4
 scene.render.resolution_x=512;scene.render.resolution_y=512;scene.render.resolution_percentage=100;scene.render.film_transparent=True
 scene.world.color=(.25,.25,.25);scene.render.image_settings.file_format='PNG';scene.render.image_settings.color_mode='RGBA'
 for name,pos,power,size in [('key',(1,-2,3),180,2),('fill',(-2,-1,1),100,2),('rim',(1,2,2),220,1.5)]:
  d=bpy.data.lights.new(name,'AREA');d.energy=power;d.shape='DISK';d.size=size;o=bpy.data.objects.new(name,d);scene.collection.objects.link(o);o.location=pos;o.rotation_euler=(-o.location).to_track_quat('-Z','Y').to_euler()
 d=bpy.data.cameras.new('ItemCamera');o=bpy.data.objects.new('ItemCamera',d);scene.collection.objects.link(o);scene.camera=o;d.type='ORTHO';o.location=(.8,-1.5,.9)
 scene.view_settings.view_transform='AgX'
def finish(o,name,mat,bevel=0,smooth=False):
 o.name='GEO-'+name;o.data.materials.append(M[mat]);PARTS.append(o)
 bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
 if bevel:
  mod=o.modifiers.new('Manufactured edge radii','BEVEL');mod.width=bevel;mod.segments=3
  bpy.context.view_layer.objects.active=o;bpy.ops.object.modifier_apply(modifier=mod.name)
 if smooth:
  for p in o.data.polygons:p.use_smooth=True
 return o
def box(name,pos,size,mat='ceramic',bevel=.003):
 bpy.ops.mesh.primitive_cube_add(size=1,location=pos);o=bpy.context.object;o.scale=size;return finish(o,name,mat,min(bevel,min(size)*.24))
def cyl(name,pos,r,depth,mat='steel',rot=None):
 bpy.ops.mesh.primitive_cylinder_add(vertices=32,radius=r,depth=depth,location=pos);o=bpy.context.object
 if rot:o.rotation_euler=rot
 return finish(o,name,mat,min(.002,r*.12),True)
def orb(name,pos,scale,mat='tissue'):
 bpy.ops.mesh.primitive_uv_sphere_add(segments=24,ring_count=12,radius=1,location=pos);o=bpy.context.object;o.scale=scale;return finish(o,name,mat,0,True)
def ring(name,pos,r,t=.004,mat='steel',rot=None):
 bpy.ops.mesh.primitive_torus_add(major_radius=r,minor_radius=t,major_segments=40,minor_segments=8,location=pos);o=bpy.context.object
 if rot:o.rotation_euler=rot
 return finish(o,name,mat,0,True)
def line(name,pts,r=.002,mat='steel'):
 curve=bpy.data.curves.new(name,'CURVE');curve.dimensions='3D';curve.bevel_depth=r;curve.bevel_resolution=2
 sp=curve.splines.new('POLY');sp.points.add(len(pts)-1)
 for p,co in zip(sp.points,pts):p.co=(*co,1)
 o=bpy.data.objects.new(name,curve);bpy.context.scene.collection.objects.link(o);bpy.ops.object.select_all(action='DESELECT');o.select_set(True);bpy.context.view_layer.objects.active=o;bpy.ops.object.convert(target='MESH');return finish(bpy.context.object,name,mat,0,True)
def label(text,pos,width=.08,mat='dark'):
 c=bpy.data.curves.new('Equipment marking','FONT');c.body=text;c.align_x='CENTER';c.size=.02;c.extrude=0;c.resolution_u=3
 o=bpy.data.objects.new('Marking',c);bpy.context.scene.collection.objects.link(o);o.location=pos;o.rotation_euler=(math.pi/2,0,0)
 bpy.context.view_layer.update();o.scale*=width/max(o.dimensions.x,.001)
 bpy.ops.object.select_all(action='DESELECT');o.select_set(True);bpy.context.view_layer.objects.active=o;bpy.ops.object.convert(target='MESH');return finish(bpy.context.object,'mark-'+text,mat)
def screws(w,h,y,z=0):
 for x in [-w/2,w/2]:
  for dz in [-h/2,h/2]:cyl('recessed fastener',(x,y,z+dz),.003,.002,'steel',(math.pi/2,0,0))
def panel(text,w=.12,h=.15,d=.045,col='ceramic'):
 box('sealed housing',(0,0,h/2),(w,d,h),col,.008);box('front seal',(0,-d/2-.001,h/2),(w*.83,.005,h*.78),'dark')
 box('inset face',(0,-d/2-.004,h/2),(w*.77,.005,h*.71),col);screws(w*.65,h*.58,-d/2-.008,h/2)
 label(text,(0,-d/2-.009,h*.62),w*.58);label('MOONKOV / FIELD',(0,-d/2-.009,h*.35),w*.60)
def pouch(code,col='ceramic',w=.10,h=.16):
 box('laminated sachet',(0,0,h/2),(w,.028,h),col,.012)
 for z in [.007,h-.007]:
  box('heat seal',(0,0,z),(w,.031,.012),'steel',.001)
  for x in range(11):box('seal crimp',((x-5)*w/12,-.017,z),(.0015,.002,.010),'dark',.0003)
 label(code.replace('_',' ').upper(),(0,-.016,h*.61),w*.80,'ceramic' if col=='dark' else 'dark')
 label('LUNAR / SEALED',(0,-.016,h*.34),w*.73,'ceramic' if col=='dark' else 'dark')
 box('tear stripe',(0,-.016,h*.78),(w*.88,.001,.008),'amber',.0002)
def injector(code,col):
 cyl('injector barrel',(0,0,.085),.018,.13,'ceramic');cyl('dose window',(0,0,.09),.0185,.045,col)
 for z in [.028,.145]:cyl('locking collar',(0,0,z),.022,.015,'dark')
 cyl('plunger',(0,0,.168),.010,.032,'steel');cyl('thumb pad',(0,0,.188),.025,.01,col)
 cyl('needle shroud',(0,0,.012),.013,.025,'steel');label(code.replace('_',' ').upper(),(0,-.024,.117),.040)
 for i in range(5):box('dose graduation',(.008,-.019,.072+i*.006),(.008,.001,.001),'ceramic',.0001)
def bottle(code,col='blue',r=.038,h=.20):
 cyl('pressure vessel',(0,0,h*.48),r,h*.82,col);orb('shoulder',(0,0,h*.85),(r,r,r*.65),col)
 cyl('valve neck',(0,0,h*.97),r*.43,h*.14,'steel');cyl('valve cap',(0,0,h*1.06),r*.57,.022,'dark')
 for z in [.015,h*.75]:ring('protective band',(0,0,z),r,.004,'dark')
 label(code.replace('_',' ').upper(),(0,-r-.003,h*.5),r*1.6,'dark' if col=='ceramic' else 'ceramic')
 label('O2' if 'oxygen' in code else 'SEALED',(0,-r-.003,h*.28),r*1.15,'dark' if col=='ceramic' else 'ceramic')
 if 'oxygen' in code:
  cyl('regulator outlet',(r*.5,0,h*1.05),.009,r,'steel',(0,math.pi/2,0))
  cyl('pressure gauge',(-r*.5,-.012,h*1.04),.014,.014,'steel',(math.pi/2,0,0))
  cyl('gauge face',(-r*.5,-.020,h*1.04),.011,.002,'dark',(math.pi/2,0,0))
  box('gauge needle',(-r*.5,-.022,h*1.04),(.001,.002,.014),'amber',.0002)
def grenade(code):
 col={'fire_bomb':'amber','smoke_grenade':'ceramic','choke_bomb':'blue','frag_bomb':'green','energy_grenade':'violet'}.get(code,'green')
 cyl('canister',(0,0,.065),.036,.11,col)
 for z in [.015,.115]:cyl('end rim',(0,0,z),.039,.012,'dark')
 cyl('fuse assembly',(0,0,.137),.018,.034,'steel');box('safety lever',(.021,0,.115),(.016,.014,.10),'steel')
 ring('pull ring',(-.029,0,.147),.019,.002,'steel',(math.pi/2,0,0));label(code.split('_')[0].upper(),(0,-.038,.068),.056,'ceramic' if col!='ceramic' else 'dark')
 if code=='frag_bomb':
  for a in range(12):
   for z in range(4):
    angle=a*math.tau/12;o=box('fragment segment',(.037*math.sin(angle),.037*math.cos(angle),.033+z*.02),(.015,.008,.016),'green',.002);o.rotation_euler.z=-angle
 if code=='energy_grenade':
  for a in range(6):
   angle=a*math.tau/6;box('capacitor cage',(.036*math.sin(angle),.036*math.cos(angle),.065),(.009,.009,.075),'steel')
def build(item):
 code=item['code'];kind=item['kind'];PARTS.clear()
 if code in ['medkit','vitality_shot','stamina_shot','antidote_shot']:injector(code,{'stamina_shot':'amber','antidote_shot':'green'}.get(code,'red'))
 elif code in ['med_gel','energy_gel','protein_bar','ration','electrolyte','dust']:
  pouch(code,{'med_gel':'ceramic','energy_gel':'amber','protein_bar':'dark','ration':'green','electrolyte':'blue','dust':'ceramic'}[code],.08 if code=='protein_bar' else .12,.19 if code=='ration' else .14)
 elif code in ['water','oxygen_canister','oxygen_reserve','helium3_sample']:bottle(code,'blue' if code=='water' else 'ceramic',.047 if code=='oxygen_reserve' else .033,.28 if code=='oxygen_reserve' else .18)
 elif code in ['energy_grenade','frag_bomb','fire_bomb','smoke_grenade','choke_bomb']:grenade(code)
 elif code=='dynamite_bundle':
  for x,y in [(-.025,-.015),(.025,-.015),(0,.025)]:
   cyl('sealed explosive',(x,y,.10),.023,.19,'red');cyl('end plug',(x,y,.198),.019,.007,'dark')
  for z in [.04,.15]:box('binding strap',(0,-.041,z),(.095,.008,.022),'dark')
  line('fuse',[(0,0,.20),(.025,0,.23),(.04,0,.24)],.003,'amber')
 elif 'trip_mine' in code:
  panel(code.split('_')[0].upper(),.15,.08,.065,'green');cyl('trip spool',(.05,0,.10),.025,.03,'steel');line('folded stake',[(-.05,.01,.05),(-.07,.01,.16),(-.08,.01,.18)],.004)
  if code=='concertina_trip_mine':
   for i in range(5):ring('compressed wire',(0,.01,.1+i*.007),.054,.002,'steel')
  elif code=='poison_trip_mine':cyl('toxin vial',(-.04,0,.12),.018,.075,'violet')
 elif code in ['trauma_kit','secure_case','ammo_box','black_box']:
  w,h,d=(.24,.18,.12) if code!='trauma_kit' else (.16,.22,.09);panel(code.replace('_',' ').upper(),w,h,d,'amber' if code=='black_box' else 'ceramic')
  for x in [-w*.35,w*.35]:box('latch',(x,-d/2-.012,h*.75),(.026,.02,.06),'dark')
  line('carry handle',[(-w*.25,0,h),( -w*.25,0,h+.035),(w*.25,0,h+.035),(w*.25,0,h)],.008,'dark')
  if code=='trauma_kit':box('medical cross vertical',(0,-d/2-.011,h*.49),(.012,.005,.05),'red');box('medical cross horizontal',(0,-d/2-.012,h*.49),(.045,.005,.012),'red')
 elif kind=='Rig':
  w=.30 if code=='scout_rig' else .40;h=.27 if code!='field_rig' else .36
  box('woven support',(0,.02,h/2),(w,.025,h),'dark',.012)
  for x in [-w*.36,w*.36]:
   line('shoulder harness',[(x,.02,h*.7),(x,.035,h+.16),(x,.09,h+.18),(x,.12,h*.6)],.018,'green')
  for i in range(3):
   x=(i-1)*w*.30;box('storage pouch',(x,-.03,h*.4),(w*.27,.09,h*.63),'green',.015);box('pouch flap',(x,-.08,h*.65),(w*.27,.012,.065),'green')
   box('buckle',(x,-.091,h*.59),(.022,.008,.032),'steel')
  label(code.upper(),(0,-.084,h*.27),w*.22,'ceramic')
 elif kind=='Backpack':
  count={'sling_pack':1,'small_pack':1,'backpack':2,'cargo_pack':3}[code];h=.26+count*.06
  panel('SUBSPACE / '+str(count),.19,h,.09)
  for i in range(count):ring('capacity toroid',(0,0,.09+i*.10),.13,.016,'steel');ring('field inset',(0,0,.09+i*.10),.13,.007,'blue')
  for x in [-.17,.17]:box('stabilizer fin',(x,.01,h*.5),(.045,.06,h*.85),'ceramic',.014)
 elif kind=='Helmet':
  ring('life support collar',(0,0,.035),.115,.024,'ceramic');ring('neck gasket',(0,0,.05),.098,.010,'dark')
  for x in [-.12,.12]:box('air recycler',(x,0,.04),(.05,.08,.065),'steel')
  if code=='energy_head_dome':
   for x in [-.032,.032]:o=orb('bow wing',(x,-.13,.04),(.034,.014,.025),'violet');o.rotation_euler.y=x*10
   orb('cat bell',(0,-.144,.01),(.018,.015,.018),'gold');line('bell slit',[(-.012,-.157,.007),(.012,-.157,.007)],.001,'dark')
 elif code=='energy_shield_generator':
  panel('S-100',.18,.16,.055);ring('field projector',(0,-.035,.082),.052,.012,'steel',(math.pi/2,0,0));cyl('projector lens',(0,-.042,.082),.039,.012,'blue',(math.pi/2,0,0))
 elif code in ['dagger','energy_sword']:
  length=.22 if code=='dagger' else .67;w=.035 if code=='dagger' else .045
  verts=[(-w/2,0,.14),(w/2,0,.14),(-w/2,0,.14+length*.76),(w/2,0,.14+length*.76),(0,0,.14+length),(0,-.006,.14),(0,-.006,.14+length*.76),(0,.006,.14),(0,.006,.14+length*.76)]
  faces=[(0,5,6,2),(5,1,3,6),(2,6,4),(6,3,4),(7,0,2,8),(1,7,8,3),(8,2,4),(3,8,4),(0,7,1,5)]
  mesh=bpy.data.meshes.new('Forged blade');mesh.from_pydata(verts,[],faces);mesh.update();o=bpy.data.objects.new('blade',mesh);bpy.context.scene.collection.objects.link(o);finish(o,'tapered blade','steel' if code=='dagger' else 'blue')
  box('guard',(0,0,.14),(.085,.025,.014),'steel');box('grip',(0,0,.07),(.029,.028,.13),'dark',.009)
  for z in range(6):ring('grip rib',(0,0,.024+z*.018),.018,.002,'steel')
 elif code=='copper_coil':
  cyl('spool core',(0,0,.06),.037,.10,'dark')
  for z in [.012,.108]:cyl('spool cheek',(0,0,z),.066,.014,'dark')
  pts=[(.048*math.cos(i*.2),.048*math.sin(i*.2),.023+i*.00014) for i in range(520)];line('copper winding',pts,.003,'copper')
 elif code=='alloy':
  for i in range(3):box('machined alloy ingot',(0,i*.028,.014),(.16,.025,.028),'steel',.005)
  label('Ti / LUNAR',(0,-.013,.012),.09,'dark')
 elif code=='ceramic_plate':
  for i in range(3):box('technical ceramic tile',(0,0,.008+i*.012),(.20,.16,.01),'ceramic',.004)
  label('Al2O3',(0,-.081,.02),.08)
 elif code=='cells':
  for x in [-.016,.016]:cyl('energy cell',(x,0,.055),.015,.10,'blue');cyl('contact',(x,0,.11),.008,.008,'gold')
  box('cell retention frame',(0,.008,.055),(.07,.015,.06),'dark');label('120',(0,-.016,.055),.038,'ceramic')
 elif code in ['circuit_board','quantum_core']:
  box('PCB',(0,0,.006),(.15,.12,.008),'green',.002)
  for i in range(7):
   x=-.06+i*.02;box('trace',(x,-.015,.011),(.001,.075,.001),'gold',.0001);box('edge contact',(x,-.055,.012),(.009,.018,.002),'gold',.0002)
  box('processor',(0,0,.018),(.055,.045,.018),'dark')
  if code=='quantum_core':
   for i in range(9):box('heat sink fin',(-.035+i*.009,0,.037),(.003,.07,.025),'steel',.001)
  for x in [-.05,.05]:
   for y in [-.025,.025]:cyl('capacitor',(x,y,.021),.007,.02,'steel')
 elif code in ['optical_sensor','laser_rangefinder','survey_compass']:
  if code=='survey_compass':
   cyl('compass case',(0,0,.02),.05,.03,'steel');cyl('dial',(0,0,.037),.044,.004,'dark');o=box('needle',(0,0,.043),(.005,.066,.004),'red');o.rotation_euler.z=.4
   for i in range(12):a=i*math.tau/12;box('dial index',(.036*math.sin(a),.036*math.cos(a),.043),(.003,.003,.002),'ceramic',.0002)
  else:
   panel('R-500' if code=='laser_rangefinder' else 'OPTIC',.11,.075,.065,'dark')
   for x in [-.028,.028]:cyl('lens barrel',(x,-.045,.035),.021,.03,'steel',(math.pi/2,0,0));cyl('coated lens',(x,-.062,.035),.017,.004,'blue',(math.pi/2,0,0))
 elif code in ['data_drive','mc00_archive_drive']:
  panel('MC00 ARCHIVE' if code.startswith('mc00') else 'ENCRYPTED',.09,.12,.025,'amber' if code.startswith('mc00') else 'steel');box('interface',(0,0,.13),(.04,.014,.025),'dark')
  for i in range(5):box('contact',(-.014+i*.007,-.008,.132),(.004,.002,.014),'gold',.0002)
 elif code=='mooncore_fragment':
  bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=1,radius=.07,location=(0,0,.06));o=bpy.context.object;o.scale=(.65,.8,1.4);finish(o,'fractured crystal','violet')
 elif code.startswith('alien_'):
  profiles={
   'alien_tissue':('tissue',4,.02,.019,.015), 'alien_raw_meat':('tissue',3,.025,.032,.018),
   'alien_core':('violet',7,.025,.025,.035), 'alien_neural_core':('violet',9,.027,.017,.023),
   'alien_silk_gland':('bone',3,.027,.021,.044), 'alien_adrenal_gland':('red',2,.025,.018,.040),
   'alien_nutrient_sac':('amber',1,.032,.045,.052), 'alien_contractile_heart':('tissue',4,.025,.023,.037),
   'alien_venom_gland':('green',2,.022,.025,.039), 'alien_regen_sac':('tissue',5,.025,.018,.022),
   'alien_gel_bladder':('blue',1,.038,.045,.030), 'alien_root_nodule':('bone',5,.018,.019,.030),
   'alien_neural_ganglion':('violet',8,.022,.014,.016)}
  col,n,spread,rx,rz=profiles[code]
  for j in range(n):
   a=j*2.399;rad=spread*(.5+j/max(n,1));x=rad*math.sin(a) if n>1 else 0;y=rad*math.cos(a) if n>1 else 0
   orb('anatomical lobe',(x,y,rz+.01),(rx,rx*.8,rz),col)
   # Curved capillaries follow each lobe rather than sticking out as straight rods.
   for side in [-1,1]:
    pts=[(x+side*rx*.7*math.sin(t),y-rx*.8*math.cos(t),rz+.01+rz*.72*math.sin(t)) for t in [k*math.pi/16 for k in range(17)]]
    line('surface vessel',pts,.0009,'tissue' if col!='tissue' else 'violet')
  if code in ['alien_core','alien_neural_core','alien_neural_ganglion']:
   for j in range(6):
    a=j*math.tau/6;pts=[((.025+k*.004)*math.cos(a+k*.12),(.025+k*.004)*math.sin(a+k*.12),.018+.003*math.sin(k)) for k in range(10)]
    line('neural root',pts,.0025,'tissue')
  elif code=='alien_root_nodule':
   for j in range(7):
    a=j*math.tau/7;line('root fiber',[(.02*math.cos(a),.02*math.sin(a),.03),(.05*math.cos(a+.2),.05*math.sin(a+.2),.018),(.08*math.cos(a-.1),.08*math.sin(a-.1),.006)],.002,'green')
  elif code in ['alien_silk_gland','alien_venom_gland','alien_contractile_heart']:
   for j in range(2):line('duct',[(j*.025-.012,0,.04),(j*.025-.012,.015,.075),(j*.035-.017,.035,.085)],.005,col)
  elif code=='alien_tissue':
   box('specimen tray',(0,0,.004),(.13,.10,.008),'steel')
   for x in [-.065,.065]:box('tray rim',(x,0,.012),(.004,.10,.020),'steel',.001)
  elif code=='alien_raw_meat':
   for j in range(4):line('muscle fibers',[(-.05+j*.018,-.025,.03),(-.04+j*.018,0,.045),(-.05+j*.018,.025,.03)],.002,'bone')
 else:raise ValueError('No authored form for '+code)
 return list(PARTS)
def publish(item):
 for o in list(bpy.context.scene.objects):
  if o.type=='MESH':bpy.data.objects.remove(o,do_unlink=True)
 parts=build(item);bpy.context.view_layer.update()
 # Join static parts into one mesh with shared material slots; apply transforms, ground origin.
 bpy.ops.object.select_all(action='DESELECT')
 for o in parts:o.select_set(True)
 bpy.context.view_layer.objects.active=parts[0];bpy.ops.object.join();o=bpy.context.object;o.name=item['code']
 bpy.ops.object.transform_apply(location=False,rotation=True,scale=True)
 coords=[o.matrix_world@Vector(c) for c in o.bound_box];low=min(v.z for v in coords)
 o.location.z-=low;bpy.context.scene.cursor.location=(0,0,0);bpy.ops.object.origin_set(type='ORIGIN_CURSOR')
 bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT');bpy.ops.mesh.normals_make_consistent(inside=False);bpy.ops.uv.smart_project(angle_limit=1.15192,island_margin=.015);bpy.ops.object.mode_set(mode='OBJECT')
 bpy.ops.export_scene.fbx(filepath=str(OUT/(item['code']+'.fbx')),use_selection=True,object_types={'MESH'},apply_unit_scale=True,axis_forward='-Z',axis_up='Y',bake_anim=False,add_leaf_bones=False)
 bpy.context.view_layer.update();coords=[o.matrix_world@Vector(c) for c in o.bound_box];center=sum(coords,Vector())/8
 cam=bpy.context.scene.camera;cam.location=center+Vector((.8,-1.5,.9));cam.rotation_euler=(center-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=max(o.dimensions)*1.45
 bpy.ops.wm.save_as_mainfile(filepath=str(OUT/(item['code']+'.blend')))
 bpy.context.scene.render.filepath=str(OUT/(item['code']+'.png'));bpy.ops.render.render(write_still=True)
 o.data.calc_loop_triangles();return dict(code=item['code'],triangles=len(o.data.loop_triangles),size=list(o.dimensions),materials=[m.name for m in o.data.materials])

if __name__ == '__main__':
 setup()
 report=[publish(item) for item in MANIFEST]
 (OUT/'report.json').write_text(json.dumps(report,indent=2))
