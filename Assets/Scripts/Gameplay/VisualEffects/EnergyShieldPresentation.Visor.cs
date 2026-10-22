using UnityEngine;
using UnityEngine.Rendering;

namespace Unity.MP_FPS
{
    public sealed partial class EnergyShieldPresentation
    {
        private Transform m_Visor;
        private MeshRenderer m_VisorRenderer;
        private LineRenderer[] m_VisorRails;
        private Quaternion m_VisorLocalFrame;
        private float m_VisorEquippedAt;
        private MaterialPropertyBlock m_VisorProperties;
        private static readonly Vector3 VisorSize=new Vector3(.135f,.216f,.17f);
        internal float VisorBlend => Application.isPlaying?Mathf.SmoothStep(0,1,Mathf.Clamp01((Time.unscaledTime-m_VisorEquippedAt)/.48f)):1;
        private Vector3 VisorSocket(int side)=>new Vector3(side*m_CollarRadii.x*.85f,.001f,m_CollarRadii.y*.95f);

        private void BuildVisor(Material metal,Material indicator)
        {
            m_VisorRenderer=Surface("Collar projected face seal",out m_Visor);
            m_Visor.GetComponent<MeshFilter>().sharedMesh=EnergyShieldGeometry.Visor;
            m_VisorLocalFrame=Quaternion.Inverse(m_Head.rotation)*Quaternion.LookRotation(transform.forward,transform.up);
            m_VisorProperties=new MaterialPropertyBlock();
            m_VisorRails=new LineRenderer[2];
            for(int i=0;i<2;i++)
            {
                int side=i==0?-1:1;
                Vector3 socket=VisorSocket(side);
                CollarPiece("Visor projection socket",EnergyShieldGeometry.Sphere,socket,new Vector3(.003f,.0045f,.005f),metal,new Color(.83f,.65f,.32f),true);
                CollarPiece("Visor emitter lens",EnergyShieldGeometry.Sphere,socket+Vector3.up*.0035f,new Vector3(.002f,.001f,.003f),indicator,new Color(.26f,.85f,.92f));
                var node=new GameObject("Collar to visor / optical light guide"){layer=gameObject.layer};node.transform.SetParent(m_Root,false);
                var rail=node.AddComponent<LineRenderer>();rail.sharedMaterial=indicator;
                rail.useWorldSpace=true;rail.positionCount=13;rail.widthMultiplier=.00075f;rail.numCapVertices=3;
                rail.shadowCastingMode=ShadowCastingMode.Off;rail.receiveShadows=false;rail.enabled=false;
                var properties=new MaterialPropertyBlock();properties.SetColor("_BaseColor",new Color(.25f,.74f,.85f));rail.SetPropertyBlock(properties);
                m_VisorRails[i]=rail;m_Hardware.Add(rail);
            }
        }
        private void UpdateVisor()
        {
            Quaternion frame=m_Head.rotation*m_VisorLocalFrame;
            WorldPose(m_Visor,m_Head.position-frame*Vector3.up*.018f,frame,VisorSize);
            float blend=VisorBlend;
            m_VisorRenderer.enabled=m_HeadEquipped;
            m_VisorProperties.SetVector("_FieldState",new Vector4(0,Mathf.Lerp(-.06f,1.06f,blend),100,100));
            m_VisorRenderer.SetPropertyBlock(m_VisorProperties);
            for(int side=0;side<2;side++)
            {
                var rail=m_VisorRails[side];rail.enabled=m_HeadEquipped;
                Vector3 origin=m_Collar.TransformPoint(VisorSocket(side==0?-1:1)+Vector3.up*.0035f);
                for(int p=0;p<rail.positionCount;p++)
                {
                    float v=p/(float)(rail.positionCount-1)*blend;
                    Vector3 point=m_Visor.TransformPoint(EnergyShieldGeometry.VisorPoint(side,v));
                    // The socket follows Neck; the canopy follows Head. A short flexible
                    // guide bridges both frames instead of leaving a floating head bubble.
                    float bridge=Mathf.Clamp01(v/.15f);
                    rail.SetPosition(p,Vector3.Lerp(origin,point,Mathf.SmoothStep(0,1,bridge))+
                        m_Collar.forward*(Mathf.Sin(bridge*Mathf.PI)*.025f));
                }
            }
        }
    }
}
