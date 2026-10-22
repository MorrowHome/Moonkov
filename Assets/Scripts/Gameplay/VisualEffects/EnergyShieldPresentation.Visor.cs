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
        private Vector3 m_SealOffset;
        private Quaternion m_SealLocalFrame;
        private float m_SealRadius;
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
            var facePoints=new List<Vector3>(2048);
            var minimum=new Vector3(float.PositiveInfinity,float.PositiveInfinity,float.PositiveInfinity);
            var maximum=-minimum;
            var faceMinimum=minimum;var faceMaximum=maximum;
            foreach(var renderer in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mesh=renderer.sharedMesh;if(!mesh||renderer.name.Contains("Runtime"))continue;
                var bones=renderer.bones;var bind=mesh.bindposes;var weights=mesh.boneWeights;
                if(bind.Length!=bones.Length||weights.Length!=mesh.vertexCount)continue;
                var related=new bool[bones.Length];var matrices=new Matrix4x4[bones.Length];
                for(int b=0;b<bones.Length;b++)if(bones[b]){related[b]=bones[b]==m_Head||bones[b].IsChildOf(m_Head);matrices[b]=bones[b].localToWorldMatrix*bind[b];}
                var vertices=mesh.vertices;
                var physical=new bool[vertices.Length];var face=new bool[vertices.Length];var materials=renderer.sharedMaterials;
                for(int sub=0;sub<Mathf.Min(mesh.subMeshCount,materials.Length);sub++)
                {
                    if(!materials[sub])continue;
                    string name=materials[sub].name.Replace(" (Instance)","");
                    if(name!="Face"&&name!="Body"&&name!="Hair")continue;
                    foreach(int index in mesh.GetTriangles(sub)){physical[index]=true;if(name=="Face")face[index]=true;}
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
                    if(face[i]){faceMinimum=Vector3.Min(faceMinimum,p);faceMaximum=Vector3.Max(faceMaximum,p);facePoints.Add(p);}
                }
            }
            if(points.Count<8){minimum=new Vector3(-.135f,-.02f,-.13f);maximum=new Vector3(.135f,.34f,.13f);}
            var anchor=m_Neck?m_Neck:m_Head;
            // The decorative choker sits high below the jaw. It must not set the
            // bottom of the helmet: doing so pushes the entire globe over the crown.
            float bottom=(inverse*(anchor.position-m_Head.position)).y;
            Vector3 center=(minimum+maximum)*.5f;
            if(!float.IsInfinity(faceMinimum.y))center.y=(faceMinimum.y+faceMaximum.y)*.5f;
            float radius=Mathf.Max(.14f,Mathf.Sqrt(RadiusSquared(center.y))+.012f);
            // Main skull plus a small scalp clearance. Low side locks and tied
            // hair must not turn the head into the envelope of the whole hairstyle.
            radius=Mathf.Max(radius,maximum.y-center.y+.010f);
            // Locate the sphere by the face, then intersect it at the real neck root.
            // A wider neck seat is preferable to moving the sphere up over ornaments.
            m_SealLatitude=Mathf.Clamp((center.y-bottom)/radius,.70f,.98f);
            m_VisorOffset=new Vector3(center.x,center.y-radius*m_SealLatitude,center.z);
            m_VisorSize=new Vector3(radius,radius*(1+m_SealLatitude),radius);
            m_SealRadius=radius*Mathf.Sqrt(1-m_SealLatitude*m_SealLatitude);
            m_SealOffset=anchor.InverseTransformPoint(m_Head.position+frame*m_VisorOffset);
            m_SealLocalFrame=Quaternion.Inverse(anchor.rotation)*frame;
            float RadiusSquared(float y)
            {
                float distance=0;var candidate=new Vector3(center.x,y,center.z);
                foreach(var point in facePoints.Count>0?facePoints:points)distance=Mathf.Max(distance,(point-candidate).sqrMagnitude);
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
            var anchor=m_Neck?m_Neck:m_Head;
            var seal=anchor.TransformPoint(m_SealOffset);var sealFrame=anchor.rotation*m_SealLocalFrame;
            m_VisorProperties.SetVector("_SealNeck",seal);
            m_VisorProperties.SetVector("_SealRight",sealFrame*Vector3.right*m_SealRadius);
            m_VisorProperties.SetVector("_SealForward",sealFrame*Vector3.forward*m_SealRadius);
            m_VisorProperties.SetVector("_SealShape",new Vector4(m_SealLatitude,0,0,0));
            m_VisorProperties.SetVector("_FieldState",new Vector4(0,Mathf.Lerp(-.06f,1.06f,blend),100,100));
            m_VisorRenderer.SetPropertyBlock(m_VisorProperties);
            m_VisorProperties.SetVector("_FieldState",new Vector4(-1,1.06f,100,100));
            m_LampProperties.SetColor("_BaseColor",new Color(color.r*breath,color.g*breath,color.b*breath,1));
            for(int i=0;i<2;i++)
            {
                int side=i==0?-1:1;
                var ear=m_EnergyEars[i];ear.enabled=m_HeadEquipped&&!m_StandardCollar;ear.SetPropertyBlock(m_VisorProperties);
                WorldPose(ear.transform,m_Visor.TransformPoint(new Vector3(side*.60f,.86f,.06f)),frame*Quaternion.Euler(-8,0,-side*18),
                    new Vector3(.086f,.105f,.058f)*Mathf.SmoothStep(0,1,Mathf.InverseLerp(.65f,1,blend)));
                m_EmitterLenses[i].SetPropertyBlock(m_LampProperties);
                var rail=m_VisorRails[i];rail.enabled=m_HeadEquipped;rail.SetPropertyBlock(m_LampProperties);
                for(int p=0;p<rail.positionCount;p++)
                {
                    float angle=(i*.5f+p/(float)(rail.positionCount-1)*.5f*blend)*Mathf.PI*2;
                    rail.SetPosition(p,seal+sealFrame*new Vector3(Mathf.Sin(angle)*m_SealRadius,.0005f,Mathf.Cos(angle)*m_SealRadius));
                }
            }
        }
    }
}
