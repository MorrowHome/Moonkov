using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.MP_FPS.DollSinger;

namespace Unity.MP_FPS
{
    public sealed partial class EnergyShieldPresentation
    {
        private Transform m_Visor;
        private MeshRenderer m_VisorRenderer;
        private LineRenderer[] m_VisorRails;
        private MeshRenderer[] m_EnergyEars,m_EmitterLenses;
        private Quaternion m_VisorLocalFrame;
        private Vector3 m_VisorOffset,m_VisorSize;
        private float m_SealLatitude;
        private float m_VisorEquippedAt;
        private MaterialPropertyBlock m_VisorProperties,m_LampProperties;
        private DollSingerHaloAim m_SealHalo;
        private LineRenderer m_SealHaloStroke;
        internal float VisorBlend => Application.isPlaying?Mathf.SmoothStep(0,1,Mathf.Clamp01((Time.unscaledTime-m_VisorEquippedAt)/.48f)):1;
        internal Color SealColor {get;private set;}
        private Vector3 VisorSocket(int side)=>new Vector3(side*m_CollarRadii.x*.85f,.001f,m_CollarRadii.y*.95f);

        private void FitHelmet(Quaternion frame)
        {
            // Only skin and hair define the head envelope. Head clothing, jewels,
            // glasses and floating ornaments may remain outside the projected shell.
            var points=new List<Vector3>(4096);var inverse=Quaternion.Inverse(frame);
            var minimum=new Vector3(float.PositiveInfinity,float.PositiveInfinity,float.PositiveInfinity);
            var maximum=-minimum;
            foreach(var renderer in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mesh=renderer.sharedMesh;if(!mesh||renderer.name.Contains("Runtime"))continue;
                var bones=renderer.bones;var bind=mesh.bindposes;var weights=mesh.boneWeights;
                if(bind.Length!=bones.Length||weights.Length!=mesh.vertexCount)continue;
                var related=new bool[bones.Length];var matrices=new Matrix4x4[bones.Length];
                for(int b=0;b<bones.Length;b++)if(bones[b]){related[b]=bones[b]==m_Head||bones[b].IsChildOf(m_Head);matrices[b]=bones[b].localToWorldMatrix*bind[b];}
                var vertices=mesh.vertices;
                var physical=new bool[vertices.Length];var materials=renderer.sharedMaterials;
                for(int sub=0;sub<Mathf.Min(mesh.subMeshCount,materials.Length);sub++)
                {
                    if(!materials[sub])continue;
                    string name=materials[sub].name.Replace(" (Instance)","");
                    if(name!="Face"&&name!="Body"&&name!="Hair")continue;
                    foreach(int index in mesh.GetTriangles(sub))physical[index]=true;
                }
                for(int i=0;i<vertices.Length;i++)
                {
                    if(!physical[i])continue;
                    var w=weights[i];float head=(related[w.boneIndex0]?w.weight0:0)+(related[w.boneIndex1]?w.weight1:0)+(related[w.boneIndex2]?w.weight2:0)+(related[w.boneIndex3]?w.weight3:0);
                    if(head<.65f)continue;
                    Vector3 v=vertices[i],world=matrices[w.boneIndex0].MultiplyPoint3x4(v)*w.weight0+matrices[w.boneIndex1].MultiplyPoint3x4(v)*w.weight1+
                        matrices[w.boneIndex2].MultiplyPoint3x4(v)*w.weight2+matrices[w.boneIndex3].MultiplyPoint3x4(v)*w.weight3;
                    Vector3 p=inverse*(world-m_Head.position);if(p.y<-.025f||p.y>.6f)continue;
                    minimum=Vector3.Min(minimum,p);maximum=Vector3.Max(maximum,p);points.Add(p);
                }
            }
            if(points.Count<8){minimum=new Vector3(-.135f,-.02f,-.13f);maximum=new Vector3(.135f,.34f,.13f);}
            var anchor=m_Neck?m_Neck:m_Head;
            float bottom=(inverse*(anchor.TransformPoint(m_CollarOffset)-m_Head.position)).y-m_CollarHeight*.5f;
            Vector3 center=(minimum+maximum)*.5f;
            // Minimize the enclosing sphere along the head's vertical axis. Unlike
            // fitting X/Z radii independently, this cannot flatten the globe sideways.
            float low=minimum.y,high=maximum.y;
            for(int step=0;step<20;step++)
            {
                float left=Mathf.Lerp(low,high,1f/3),right=Mathf.Lerp(low,high,2f/3);
                if(RadiusSquared(left)<RadiusSquared(right))high=right;else low=left;
            }
            center.y=(low+high)*.5f;
            float radius=Mathf.Max(.14f,Mathf.Sqrt(RadiusSquared(center.y))+.012f);
            // A real helmet is an almost complete sphere with a small neck opening.
            // Its lower pole sits at the collar, rather than cutting the sphere at
            // jaw height and collapsing the entire lower hemisphere into the neck.
            float opening=Mathf.Min(radius*.4f,Mathf.Max(m_CollarRadii.x,m_CollarRadii.y)*1.085f+.010f);
            m_SealLatitude=Mathf.Sqrt(1-opening*opening/(radius*radius));
            m_VisorOffset=new Vector3(center.x,bottom,center.z);
            m_VisorSize=new Vector3(radius,radius*(1+m_SealLatitude),radius);
            float RadiusSquared(float y)
            {
                float distance=0;var candidate=new Vector3(center.x,y,center.z);
                foreach(var point in points)distance=Mathf.Max(distance,(point-candidate).sqrMagnitude);
                if(points.Count==0)distance=(maximum-candidate).sqrMagnitude;
                return distance;
            }
        }
        private void BuildVisor(Material metal,Material indicator)
        {
            Quaternion frame=Quaternion.LookRotation(transform.forward,transform.up);
            FitHelmet(frame);
            m_VisorRenderer=Surface("Neko complete space helmet",out m_Visor);
            m_Visor.GetComponent<MeshFilter>().sharedMesh=EnergyShieldGeometry.Visor;
            m_VisorLocalFrame=Quaternion.Inverse(m_Head.rotation)*frame;
            m_VisorProperties=new MaterialPropertyBlock();m_LampProperties=new MaterialPropertyBlock();
            m_SealHalo=GetComponentInChildren<DollSingerHaloAim>(true);
            if(m_SealHalo&&m_SealHalo.haloStrokes!=null&&m_SealHalo.haloStrokes.Length>0)m_SealHaloStroke=m_SealHalo.haloStrokes[0];
            m_VisorRails=new LineRenderer[2];m_EnergyEars=new MeshRenderer[2];m_EmitterLenses=new MeshRenderer[2];
            for(int i=0;i<2;i++)
            {
                int side=i==0?-1:1;Vector3 socket=VisorSocket(side);
                CollarPiece("Visor projection socket",EnergyShieldGeometry.Sphere,socket,new Vector3(.003f,.0045f,.005f),metal,new Color(.83f,.65f,.32f),true);
                m_EmitterLenses[i]=CollarPiece("Halo matched breathing emitter",EnergyShieldGeometry.Sphere,socket+Vector3.up*.0035f,new Vector3(.002f,.001f,.003f),indicator,Color.white);
                var node=new GameObject("Collar seal / breathing half rim"){layer=gameObject.layer};node.transform.SetParent(m_Root,false);
                var rail=node.AddComponent<LineRenderer>();rail.sharedMaterial=indicator;
                rail.useWorldSpace=true;rail.positionCount=25;rail.widthMultiplier=.00065f;rail.numCapVertices=3;
                rail.shadowCastingMode=ShadowCastingMode.Off;rail.receiveShadows=false;rail.enabled=false;
                m_VisorRails[i]=rail;m_Hardware.Add(rail);
                m_EnergyEars[i]=Surface("Neko energy ear "+(side<0?"left":"right"),out var ear);
                ear.GetComponent<MeshFilter>().sharedMesh=EnergyShieldGeometry.EnergyEar;m_Hardware.Add(m_EnergyEars[i]);
            }
        }
        private void UpdateVisor()
        {
            Quaternion frame=m_Head.rotation*m_VisorLocalFrame;
            WorldPose(m_Visor,m_Head.position+frame*m_VisorOffset,frame,m_VisorSize);
            float blend=VisorBlend;
            Color color=m_SealHalo&&m_SealHalo.isActiveAndEnabled&&m_SealHalo.haloLight?m_SealHalo.haloLight.color:
                m_SealHaloStroke?m_SealHaloStroke.startColor:m_SealHalo?m_SealHalo.idleHaloColor:Color.white;
            float breath=Mathf.Lerp(.08f,1,Mathf.SmoothStep(0,1,(Mathf.Sin(Time.realtimeSinceStartup*Mathf.PI*2/3.6f)+1)*.5f));
            color.a=breath;SealColor=color;
            m_VisorRenderer.enabled=m_HeadEquipped;
            m_VisorProperties.SetColor("_SealTint",color);
            m_VisorProperties.SetVector("_SealNeck",m_Collar.position-m_Collar.up*(m_CollarHeight*.5f));
            m_VisorProperties.SetVector("_SealRight",m_Collar.right*(m_CollarRadii.x*1.085f));
            m_VisorProperties.SetVector("_SealForward",m_Collar.forward*(m_CollarRadii.y*1.085f));
            m_VisorProperties.SetVector("_SealShape",new Vector4(m_SealLatitude,0,0,0));
            m_VisorProperties.SetVector("_FieldState",new Vector4(0,Mathf.Lerp(-.06f,1.06f,blend),100,100));
            m_VisorRenderer.SetPropertyBlock(m_VisorProperties);
            m_VisorProperties.SetVector("_FieldState",new Vector4(-1,1.06f,100,100));
            m_LampProperties.SetColor("_BaseColor",new Color(color.r*breath,color.g*breath,color.b*breath,1));
            for(int i=0;i<2;i++)
            {
                int side=i==0?-1:1;
                var ear=m_EnergyEars[i];ear.enabled=m_HeadEquipped;ear.SetPropertyBlock(m_VisorProperties);
                WorldPose(ear.transform,m_Visor.TransformPoint(new Vector3(side*.60f,.86f,.06f)),frame*Quaternion.Euler(-8,0,-side*18),
                    new Vector3(.086f,.105f,.058f)*Mathf.SmoothStep(0,1,Mathf.InverseLerp(.65f,1,blend)));
                m_EmitterLenses[i].SetPropertyBlock(m_LampProperties);
                var rail=m_VisorRails[i];rail.enabled=m_HeadEquipped;rail.SetPropertyBlock(m_LampProperties);
                for(int p=0;p<rail.positionCount;p++)
                {
                    float angle=(i*.5f+p/(float)(rail.positionCount-1)*.5f*blend)*Mathf.PI*2;
                    rail.SetPosition(p,m_Collar.TransformPoint(new Vector3(Mathf.Sin(angle)*m_CollarRadii.x*1.10f,
                        m_CollarHeight*.5f+.0005f,Mathf.Cos(angle)*m_CollarRadii.y*1.10f)));
                }
            }
        }
    }
}
