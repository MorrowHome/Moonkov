#!/usr/bin/env python3
"""Bake reviewed source takes into exact-rig playback plus bone-relative prop tracks.
DollSinger's three reviewed props retain the saved source verbatim. Other rigs
and inventory shapes solve complete chains offline; no runtime wrist IK.
"""
import argparse,copy,json,math,sys
from pathlib import Path
from retarget_hatsune_miku import Rig,Retargeter,add,sub,mul,dot,cross,length,unit,qrotate,qmul,qinverse,qslerp,frame,between,palm_frame
ROOT=Path(__file__).resolve().parents[2];SRC=json.loads((ROOT/'SourceData/Animations/Survival/BlenderExport.json').read_text());CAL=json.loads((ROOT/'SourceData/Animations/Survival/Calibration.json').read_text());GEO=json.loads((ROOT/'SourceData/Animations/Survival/RigGeometry.json').read_text());SR=Rig(json.loads((ROOT/'Tools/Blender/DollSingerBind.json').read_text()));SOURCE={m['action']:m for m in SRC['motions']};PROPS={p['code']:p for p in SRC['props']};I=(0,0,0,1)
def sm(a,b,t):v=max(0,min(1,(t-a)/(b-a)));return v*v*(3-2*v)
def read(t,f):return tuple(t['positions'][f*3:f*3+3]),tuple(t['rotations'][f*4:f*4+4])
def local_point(p,q,v):return add(p,qrotate(q,v))
def bounds(part):
 v=part['vertices'];v=[v[i:i+3] for i in range(0,len(v),3)] if v and isinstance(v[0],(int,float)) else v
 lo=tuple(min(x[i] for x in v) for i in range(3));hi=tuple(max(x[i] for x in v) for i in range(3));return mul(add(lo,hi),.5),sub(hi,lo)
def anatomy(r,p,s):
 f=unit(sub(p['中指１.'+s],p['手首.'+s]));a=unit(sub(p['人指１.'+s],p['小指１.'+s]));return f,a,mul(unit(cross(a,f)),-1 if s=='R' else 1)
def orient(r,s,forward,across):return qmul(frame(forward,across),qmul(qinverse(palm_frame(r,s)),r.r['手首.'+s]))
def forearm_surface(geometry,r,points,rotations,origin,normal,fallback):
 """Intersect the deformed production forearm, rather than a bone-radius proxy."""
 vertices={}
 for v in geometry['vertices']:
  result=(0,0,0)
  for influence in v['weights']:
   bone=influence['bone'];delta=qmul(rotations[bone],qinverse(r.r[bone]));value=add(points[bone],qrotate(delta,sub(v['p'],r.p[bone])));result=add(result,mul(value,influence['weight']))
  vertices[v['id']]=result
 hits=[]
 for triangle in geometry['triangles']:
  a,b,c=[vertices[i] for i in triangle['ids']];e1=sub(b,a);e2=sub(c,a);h=cross(normal,e2);det=dot(e1,h)
  if abs(det)<1e-10:continue
  inv=1/det;s=sub(origin,a);u=dot(s,h)*inv
  if u<0 or u>1:continue
  k=cross(s,e1);v=dot(normal,k)*inv
  if v<0 or u+v>1:continue
  distance=dot(e2,k)*inv
  if .003<distance<.09:hits.append(distance)
 return add(origin,mul(normal,min(hits)-.001)) if hits else fallback
def arc(r,world,points,names,target,outward):
 base=points[names[0]];sizes=[length(sub(r.p[b],r.p[a])) for a,b in zip(names,names[1:])];delta=sub(target,base);d=length(delta)
 if d<1e-7:return
 direction=unit(delta);out=sub(outward,mul(direction,dot(outward,direction)))
 if length(out)<1e-6:out=cross(direction,(0,1,0))
 if length(out)<1e-6:out=cross(direction,(1,0,0))
 out=unit(out)
 def span(theta):
  x=sum(l*math.cos(a) for l,a in zip(sizes,(theta,0,-theta)));y=sum(l*math.sin(a) for l,a in zip(sizes,(theta,0,-theta)));return math.hypot(x,y),math.atan2(y,x)
 lo,hi=0,math.pi/2
 for _ in range(20):
  mid=(lo+hi)/2
  if span(mid)[0]>d:lo=mid
  else:hi=mid
 theta=(lo+hi)/2;offset=span(theta)[1];h='手首.'+names[0][-1];hand_delta=qmul(world[h],qinverse(r.r[h]))
 for name,child,angle in zip(names,names[1:],(theta-offset,-offset,-theta-offset)):
  desired=add(mul(direction,math.cos(angle)),mul(out,math.sin(angle)));existing=qmul(hand_delta,r.r[name]);current=qrotate(hand_delta,sub(r.p[child],r.p[name]));world[name]=qmul(between(current,desired),existing)
def chain(d,s):return [d+j+'.'+s for j in (['０','１','２','先'] if d=='親指' else ['１','２','３','先'])]
def relative(track,poses,side):
 out={'name':track.get('name',''),'anchor':side,'positions':[],'rotations':[],'visible':track.get('visible',[])};bone='手首.'+side
 for f,(points,rotations) in enumerate(poses):
  p,q=read(track,f);inverse=qinverse(rotations[bone]);out['positions'].extend(qrotate(inverse,sub(p,points[bone])));out['rotations'].extend(qmul(inverse,q))
 return out
source_poses={}
for name,m in SOURCE.items():
 source_poses[name]=[SR.evaluate(local={n:tuple(m['rotations'][(f*len(SRC['bones'])+i)*4:(f*len(SRC['bones'])+i+1)*4]) for i,n in enumerate(SRC['bones'])}) for f in range(m['frames'])]
parser=argparse.ArgumentParser();parser.add_argument('--rig',action='append',choices=('DollSinger','HatsuneMiku','Shian'));selected=parser.parse_args().rig or ('DollSinger','HatsuneMiku','Shian')
report_path=ROOT/'SourceData/Animations/Survival/RuntimeBake.json';previous_report=json.loads(report_path.read_text()) if report_path.exists() else {};report=[m for m in previous_report.get('motions',[]) if m['rig'] not in selected] if previous_report.get('sourceSha256')==SRC['sourceSha256'] else []
for rig_name in ('DollSinger','HatsuneMiku','Shian'):
 if rig_name not in selected:continue
 r=Rig(json.loads((ROOT/f'Tools/Blender/{rig_name}Bind.json').read_text()));solver=Retargeter(SR,r);names=solver.arm_tracks;scale=solver.scale;chest=r.p['上半身2'];schest=SR.p['上半身2'];cal=next(x for x in CAL['rigs'] if x['rig']==rig_name);geometry=next(x for x in GEO['rigs'] if x['rig']==rig_name);spad=GEO['rigs'][0]['palmPad'];pad=geometry['palmPad'];map_point=lambda p:add(chest,mul(sub(p,schest),scale));output=[]
 existing=json.loads((ROOT/f'Assets/Resources/Moonkov/Actions/{rig_name}.json').read_text());oldnames=existing['bones']
 # Preserve treatment references and independently authored oxygen takes.
 for old in existing['motions']:
  if old['action'] not in ('Treat','RefillOxygen'):continue
  m=copy.deepcopy(old);m['rotations']=[v for f in range(m['frames']) for n in names for v in old['rotations'][(f*len(oldnames)+oldnames.index(n))*4:(f*len(oldnames)+oldnames.index(n)+1)*4]];output.append(m)
 entries=[{'code':'','action':'Search'}]+[p for p in GEO['props'] if p['action'] in ('Eat','Drink','Inject')];groups={}
 for item in entries:
  code=item['code'];kind=item['action'];key=(kind,'injectors') if kind=='Inject' else (kind,code) if code in ('','ration','water') or code.startswith('alien_') else (kind,*[round(v,5) for v in item['size']],*[round(v,5) for v in item['contact']])
  groups.setdefault(key,[]).append(item)
 for key,items in groups.items():
  item=items[0];code=item['code'];kind=item['action'];raw=code.startswith('alien_');refcode='medkit' if kind=='Inject' else 'water' if code=='water' else 'ration';source_kind='Search' if kind=='Search' else 'Inject' if kind=='Inject' else 'Drink' if code=='water' else 'Eat';src=SOURCE[source_kind];m=copy.deepcopy(src);m['action']=kind;m['items']=[x['code'] for x in items] if code else [];m['openMarker']=.12 if kind=='Inject' else .08 if code=='water' else .18;m['sourceSha256']=SRC['sourceSha256'];m['injectionSite']=[];previous={};poses=[];roots={'positions':[],'rotations':[]};part_tracks=[{'name':t['name'],'positions':[],'rotations':[],'visible':t['visible']} for t in src['parts']];values=[];max_wrist=0
  exact=rig_name=='DollSinger' and (code in ('ration','water') or kind in ('Inject','Search'))
  ref=PROPS.get(refcode);grip=tuple(ref['grip']) if ref else (0,0,0);actual_contact=tuple(ref['contact']) if ref else (0,0,0)
  if code and code not in ('ration','water') and kind!='Inject':
   actual_contact=tuple(item['contact']);size=item['size'];center=item['center'];grip=tuple(item['grip']) if raw else (center[0]+size[0]*.375,center[1]+size[1]*.105,center[2])
  for f in range(src['frames']):
   phase=f/(src['frames']-1);sp,sq=source_poses[source_kind][f];world={n:solver.semantic_rotation(n,sq) for n in solver.semantic};starts,_=r.evaluate();rp,rq=read(src['root'],f) if code else ((0,0,0),I);desired_parts={};root_p,root_q=rp,rq
   if exact:
    points,rotations=sp,sq
   else:
    if code:
     source_contact=local_point(rp,rq,ref['contact']);contact_p=map_point(source_contact)
     contact_weight=sm(src['contactIn']-.08,src['contactIn'],phase)*(1-sm(src['contactOut'],src['contactOut']+.08,phase))
     if kind in ('Eat','Drink'):contact_p=add(contact_p,mul(sub(tuple(cal['mouth']),map_point(CAL['rigs'][0]['mouth'])),contact_weight))
     root_p=sub(contact_p,qrotate(root_q,actual_contact))
    left_goal=map_point(sp['手首.L']);right_goal=map_point(sp['手首.R'])
    # Detached pieces keep their own authored paths. At closure the cap must
    # physically fit the target bottle, whose size is independent of the rig.
    for t in src['parts']:
     pp,pq=read(t,f);part=next(p for p in ref['parts'] if p['name']==t['name']);pivot,_=bounds(part);center=local_point(pp,pq,pivot)
     if t['name'] in ('Cap','TearStrip','NeedleSheath'):
      detach=sm(.18,.27,phase)*(1-sm(.70,.78,phase)) if t['name']=='Cap' else sm(.18,.30,phase) if t['name']=='TearStrip' else sm(.12,.23,phase)
      actual_pivot=pivot
      if t['name']=='TearStrip' and code!='ration':actual_pivot=(0,item['center'][1]+item['size'][1]*.5-.009,0)
      cap_center=add(mul(local_point(root_p,root_q,actual_pivot),1-detach),mul(map_point(center),detach));pp=sub(cap_center,qrotate(pq,actual_pivot));desired_parts[t['name']]=(pp,pq)
      holding=(t['name']=='Cap') or (t['name']=='TearStrip' and not raw) or (t['name']=='NeedleSheath' and phase<=.24)
      if holding:
       sf,sa,sn=anatomy(SR,sp,'L');tf,ta,tn=anatomy(r,r.evaluate(world=world)[0],'L');src_grip=local_point(read(t,f)[0],read(t,f)[1],(-.046,.181,0) if t['name']=='TearStrip' else pivot)
       source_offset=sub(src_grip,sp['手首.L']);index_offset=qrotate(qmul(world['手首.L'],qinverse(r.r['手首.L'])),sub(r.p['人指１.L'],r.p['手首.L']));oldindex=sub(sp['人指１.L'],sp['手首.L']);normal_offset=dot(sub(source_offset,oldindex),sn)+pad['L']-spad['L']
       actual_grip=local_point(pp,pq,(-item['size'][0]*.383,actual_pivot[1],0) if t['name']=='TearStrip' and code!='ration' else (-.046,.181,0) if t['name']=='TearStrip' else actual_pivot)
       source_residual=sub(sub(sub(src_grip,oldindex),mul(sn,dot(sub(source_offset,oldindex),sn))),sp['手首.L'])
       left_goal=sub(sub(sub(actual_grip,index_offset),mul(tn,normal_offset)),mul(source_residual,scale))
     else:
      relp=qrotate(qinverse(rq),sub(pp,rp));relq=qmul(qinverse(rq),pq);desired_parts[t['name']]=(local_point(root_p,root_q,relp),qmul(root_q,relq))
    if raw:
     left_goal=add(chest,(-.17,.015,.19));world['手首.L']=orient(r,'L',(0,-.75,.661438),(1,0,0))
    try:solver.arm('L',left_goal,map_point(sp['ひじ.L']),world,starts,previous=previous.get('L'),anatomical_wrist_limit=60)
    except ValueError as error:raise RuntimeError((rig_name,code,f,left_goal,sp['手首.L'])) from error
    lp,lq=r.evaluate(world=world)
    if kind=='Inject':
     if rig_name=='HatsuneMiku':
      # The forearm is fully sleeved. Use the exposed front of the upper arm.
      axis=unit(sub(lp['ひじ.L'],lp['腕.L']));normal=unit(sub((0,0,1),mul(axis,axis[2])));origin=add(lp['腕.L'],mul(sub(lp['ひじ.L'],lp['腕.L']),.30));radius=.025
     else:
      axis=unit(sub(lp['手首.L'],lp['ひじ.L']));normal=unit(sub((0,1,0),mul(axis,axis[1])));origin=add(lp['ひじ.L'],mul(sub(lp['手首.L'],lp['ひじ.L']),.28));radius=cal['radius']
     site=forearm_surface(geometry,r,lp,lq,origin,normal,add(origin,mul(normal,radius)))
     strength=sm(.37,.48,phase)*(1-sm(.72,.80,phase));current_tip=local_point(root_p,root_q,actual_contact);adjust=between(qrotate(root_q,(0,1,0)),normal);adjust=qslerp(I,adjust,strength);root_q=qmul(adjust,root_q);world['手首.R']=qmul(adjust,world['手首.R']);root_p=sub(add(current_tip,mul(sub(site,current_tip),strength)),qrotate(root_q,actual_contact))
    if code:
     sf,sa,sn=anatomy(SR,sp,'R');tp,tq=r.evaluate(world=world);tf,ta,tn=anatomy(r,tp,'R');source_grip=local_point(rp,rq,ref['grip']);normal_offset=dot(sub(sub(source_grip,sp['手首.R']),mul(sub(sp['中指１.R'],sp['手首.R']),.95)),sn)+pad['R']-spad['R'];middle=qrotate(qmul(world['手首.R'],qinverse(r.r['手首.R'])),mul(sub(r.p['中指１.R'],r.p['手首.R']),.95));right_goal=sub(sub(local_point(root_p,root_q,grip),middle),mul(tn,normal_offset))
    solver.arm('R',right_goal,map_point(sp['ひじ.R']),world,starts,previous=previous.get('R'),anatomical_wrist_limit=60,elbow_max_y=r.p['腕.R'][1]-.025 if kind=='Inject' else None)
    points,rotations=r.evaluate(world=world)
    if code:
     # Each target phalanx chain meets the same physical prop surface. Bone
     # lengths are never scaled. Transport source bend planes through the prop.
     for side in ('R','L'):
      if side=='L' and (raw or not desired_parts):continue
      for digit in ('人指','中指','薬指','小指','親指'):
       ns=chain(digit,side);oldbase=sp[ns[0]];oldtip=sp[ns[-1]];chord=sub(oldtip,oldbase);ratio=length(sub(SR.p[ns[1]],SR.p[ns[0]]))/max(1e-6,sum(length(sub(SR.p[b],SR.p[a])) for a,b in zip(ns,ns[1:])));outward=sub(sp[ns[1]],add(oldbase,mul(chord,ratio)))
       if side=='R':
        local_tip=qrotate(qinverse(rq),sub(oldtip,rp));offset=sub(local_tip,tuple(ref['grip']))
        if code not in ('ration','water') and kind!='Inject':
         refsize=(.12,.19,.032);offset=tuple(offset[i]*min(1.5,max(.2,item['size'][i]/refsize[i])) for i in range(3))
        target=local_point(root_p,root_q,add(grip,offset));outward=qrotate(qmul(root_q,qinverse(rq)),outward)
       else:
        t=next((t for t in src['parts'] if t['name'] in ('Cap','TearStrip','NeedleSheath')),None)
        if t is None or t['name']=='NeedleSheath' and phase>.24:continue
        pp,pq=read(t,f);dp,dq=desired_parts[t['name']];target=local_point(dp,dq,qrotate(qinverse(pq),sub(oldtip,pp)));outward=qrotate(qmul(dq,qinverse(pq)),outward)
       arc(r,world,points,ns,target,outward)
     points,rotations=r.evaluate(world=world)
   poses.append((points,rotations));previous={s:{n:rotations[n] for n in ('腕.'+s,'ひじ.'+s)} for s in ('L','R')}
   for n in names:values.extend(qmul(qinverse(rotations[r.parent[n]]),rotations[n]))
   if code:
    roots['positions'].extend(root_p);roots['rotations'].extend(root_q)
    for t,out in zip(src['parts'],part_tracks):
     pp,pq=read(t,f) if exact else desired_parts[t['name']]
     if not exact and t['name'] not in ('Cap','TearStrip','NeedleSheath'):
      relp=qrotate(qinverse(rq),sub(read(t,f)[0],rp));relq=qmul(qinverse(rq),read(t,f)[1]);pp,pq=local_point(root_p,root_q,relp),qmul(root_q,relq)
     out['positions'].extend(pp);out['rotations'].extend(pq)
    site=local_point(root_p,root_q,actual_contact);m['injectionSite'].extend(qrotate(qinverse(rotations['ひじ.L']),sub(site,points['ひじ.L'])))
   for s in ('L','R'):max_wrist=max(max_wrist,math.degrees(math.acos(max(-1,min(1,dot(unit(sub(points['手首.'+s],points['ひじ.'+s])),unit(sub(points['中指１.'+s],points['手首.'+s]))))))))
  m['rotations']=[round(v,7) for v in values]
  if code:
   m['root']=relative(roots,poses,'R');m['parts']=[relative(t,poses,'L' if t['name'] in ('Cap','TearStrip','NeedleSheath') else 'R') for t in part_tracks]
   if raw:m['parts']=[];m['openMarker']=0
  else:m.pop('root',None);m['parts']=[]
  output.append(m);report.append({'rig':rig_name,'items':m['items'],'action':kind,'maximumWristBendDegrees':max_wrist})
 lib={'version':1,'anatomicalContactVersion':2,'frameRate':60,'rig':rig_name,'sourceSha256':SRC['sourceSha256'],'bones':names,'motions':output};path=ROOT/f'Assets/Resources/Moonkov/Actions/{rig_name}.json';path.write_text(json.dumps(lib,separators=(',',':'))+'\n');print(rig_name,len(output),'motions',flush=True)
(ROOT/'SourceData/Animations/Survival/RuntimeBake.json').write_text(json.dumps({'sourceSha256':SRC['sourceSha256'],'motions':report},indent=2))
