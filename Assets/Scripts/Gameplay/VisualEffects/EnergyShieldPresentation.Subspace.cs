using System;
using UnityEngine;

namespace Unity.MP_FPS
{
    public sealed partial class EnergyShieldPresentation
    {
        private Transform m_Subspace,m_PackAnchor,m_ShieldAnchor;
        private Transform m_ScoutFins;
        private Vector3 m_PackOffset,m_DeviceOffset;
        private Quaternion m_PackFrame,m_DeviceFrame;
        private MeshRenderer m_ApertureRenderer;
        private MeshRenderer[] m_PackRings,m_PackLights,m_ChargeBars;
        private MaterialPropertyBlock m_EquipmentProperties;
        private byte m_BackpackStyle;
        private float m_Charge,m_Energy;

        private void FitEquipment()
        {
            m_PackAnchor=m_Chest?m_Chest:m_Hips?m_Hips:transform;
            m_ShieldAnchor=m_Hips?m_Hips:m_PackAnchor;
            var frame=Quaternion.LookRotation(transform.forward,transform.up);
            Vector3 hip=m_ShieldAnchor.position,center=Vector3.Lerp(hip,m_PackAnchor.position,.65f);
            float back=-.09f,front=.09f,right=.12f;
            // Fit against torso skin/clothing only. Skin matrices preserve imported FBX scale.
            foreach(var renderer in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mesh=renderer.sharedMesh;if(!mesh || renderer.name.Contains("Runtime"))continue;
                var bones=renderer.bones;var bind=mesh.bindposes;var weights=mesh.boneWeights;
                if(bind.Length!=bones.Length || weights.Length!=mesh.vertexCount)continue;
                var accepted=new bool[mesh.vertexCount];var materials=renderer.sharedMaterials;
                for(int sub=0;sub<Mathf.Min(materials.Length,mesh.subMeshCount);sub++)
                {
                    string name=materials[sub]?materials[sub].name:"";
                    if(!name.Equals("Body",StringComparison.OrdinalIgnoreCase) && name.IndexOf("Dress",StringComparison.OrdinalIgnoreCase)<0)continue;
                    foreach(int i in mesh.GetTriangles(sub))accepted[i]=true;
                }
                var matrices=new Matrix4x4[bones.Length];for(int i=0;i<bones.Length;i++)if(bones[i])matrices[i]=bones[i].localToWorldMatrix*bind[i];
                var vertices=mesh.vertices;var inverse=Quaternion.Inverse(frame);
                for(int i=0;i<vertices.Length;i++)
                {
                    if(!accepted[i])continue;var w=weights[i];var v=vertices[i];
                    Vector3 world=matrices[w.boneIndex0].MultiplyPoint3x4(v)*w.weight0+matrices[w.boneIndex1].MultiplyPoint3x4(v)*w.weight1+
                        matrices[w.boneIndex2].MultiplyPoint3x4(v)*w.weight2+matrices[w.boneIndex3].MultiplyPoint3x4(v)*w.weight3;
                    Vector3 local=inverse*(world-center);
                    if(Mathf.Abs(local.y)>.055f || Mathf.Abs(local.x)>.19f || Mathf.Abs(local.z)>.22f)continue;
                    back=Mathf.Min(back,local.z);front=Mathf.Max(front,local.z);right=Mathf.Max(right,Mathf.Abs(local.x));
                }
            }
            m_PackOffset=m_PackAnchor.InverseTransformPoint(center+frame*new Vector3(0,0,back-.045f));
            m_PackFrame=Quaternion.Inverse(m_PackAnchor.rotation)*frame*Quaternion.Euler(0,180,0);
            m_DeviceOffset=m_ShieldAnchor.InverseTransformPoint(center+frame*new Vector3(Mathf.Clamp(right,.12f,.19f),-.065f,front+.025f));
            m_DeviceFrame=Quaternion.Inverse(m_ShieldAnchor.rotation)*frame*Quaternion.Euler(0,20,0);
        }

        private void BuildEquipment(Material metal,Material indicator)
        {
            m_EquipmentProperties=new MaterialPropertyBlock();
            var dark=new Color(.045f,.06f,.1f);var silver=new Color(.5f,.57f,.66f);var cyan=new Color(.12f,.64f,.94f);
            m_Subspace=new GameObject("Equipped subspace storage anchor"){layer=gameObject.layer}.transform;m_Subspace.SetParent(m_Root,false);
            m_PackRings=new MeshRenderer[3];m_PackLights=new MeshRenderer[8];
            for(int i=0;i<3;i++)
            {
                float radius=.102f+i*.019f;
                var ring=Hardware(m_Subspace,EnergyShieldGeometry.Collar,new Vector3(0,0,-i*.012f),new Vector3(radius,.035f,radius),metal,i==0?silver:dark);
                ring.name="Storage capacity ring "+(i+1);ring.transform.localRotation=Quaternion.Euler(90,0,0);m_PackRings[i]=ring;
            }
            for(int i=0;i<8;i++)
            {
                float angle=i*Mathf.PI/4;var p=new Vector3(Mathf.Sin(angle)*.103f,Mathf.Cos(angle)*.103f,.006f);
                Hardware(m_Subspace,EnergyShieldGeometry.Box,p,new Vector3(.022f,.034f,.018f),metal,dark).transform.localRotation=Quaternion.Euler(0,0,-i*45);
                var lens=Hardware(m_Subspace,EnergyShieldGeometry.Box,p+Vector3.forward*.011f,new Vector3(.005f,.023f,.003f),indicator,Color.magenta);
                lens.name="Subspace anchor light";lens.transform.localRotation=Quaternion.Euler(0,0,-i*45);m_PackLights[i]=lens;
            }
            m_ApertureRenderer=Surface("Subspace storage aperture",out var aperture);aperture.SetParent(m_Subspace,false);
            aperture.GetComponent<MeshFilter>().sharedMesh=EnergyShieldGeometry.Aperture;aperture.localScale=Vector3.one*.100f;
            m_ScoutFins=new GameObject("Scout stabilizer fins").transform;m_ScoutFins.SetParent(m_Subspace,false);
            for(int side=-1;side<=1;side+=2)
                Hardware(m_ScoutFins,EnergyShieldGeometry.Housing,new Vector3(side*.128f,0,-.012f),new Vector3(.025f,.12f,.02f),metal,silver);
            Hardware(m_Device,EnergyShieldGeometry.Housing,Vector3.zero,new Vector3(.105f,.145f,.045f),metal,dark);
            Hardware(m_Device,EnergyShieldGeometry.Housing,new Vector3(0,0,.025f),new Vector3(.08f,.12f,.012f),metal,silver);
            var core=Hardware(m_Device,EnergyShieldGeometry.Collar,new Vector3(0,.012f,.035f),new Vector3(.027f,.025f,.027f),indicator,cyan);
            core.transform.localRotation=Quaternion.Euler(90,0,0);
            for(int i=0;i<3;i++)
            {
                float a=i*Mathf.PI*2/3;
                var vane=Hardware(m_Device,EnergyShieldGeometry.Box,new Vector3(Mathf.Sin(a)*.035f,.012f+Mathf.Cos(a)*.035f,.034f),new Vector3(.012f,.025f,.012f),metal,dark);
                vane.transform.localRotation=Quaternion.Euler(0,0,-i*120);
            }
            m_ChargeBars=new MeshRenderer[4];
            for(int i=0;i<4;i++)m_ChargeBars[i]=Hardware(m_Device,EnergyShieldGeometry.Box,new Vector3(-.027f+i*.018f,-.045f,.035f),new Vector3(.011f,.006f,.003f),indicator,cyan);
        }

        private void UpdateEquipment()
        {
            m_Subspace.gameObject.SetActive(m_BackpackStyle!=0);m_Device.gameObject.SetActive(m_ShieldEquipped);
            if(m_BackpackStyle!=0)
            {
                float size=m_BackpackStyle==1?.78f:m_BackpackStyle==2?1:m_BackpackStyle==4?.9f:1.12f;
                WorldPose(m_Subspace,m_PackAnchor.TransformPoint(m_PackOffset),m_PackAnchor.rotation*m_PackFrame,Vector3.one*size);
                for(int i=0;i<3;i++)m_PackRings[i].gameObject.SetActive(i<(m_BackpackStyle==4?1:m_BackpackStyle));
                m_ScoutFins.gameObject.SetActive(m_BackpackStyle==4);
                Color color=SealColor;color.a=1;
                foreach(var lamp in m_PackLights){m_EquipmentProperties.Clear();m_EquipmentProperties.SetColor("_BaseColor",color*(.7f+.3f*SealColor.a));lamp.SetPropertyBlock(m_EquipmentProperties);}
                m_EquipmentProperties.Clear();m_EquipmentProperties.SetVector("_FieldState",new Vector4(2,1.12f,100,100));m_EquipmentProperties.SetColor("_SealTint",SealColor);
                m_ApertureRenderer.SetPropertyBlock(m_EquipmentProperties);
            }
            m_ApertureRenderer.enabled=m_BackpackStyle!=0;
            WorldPose(m_Device,m_ShieldAnchor.TransformPoint(m_DeviceOffset),m_ShieldAnchor.rotation*m_DeviceFrame,Vector3.one);
            for(int i=0;i<4;i++)
            {
                bool filled=m_Charge>(i+.01f)/4;
                Color color=filled?new Color(.12f,.64f,.94f):new Color(.025f,.05f,.075f);
                if(m_Energy<=0)color=filled?new Color(.9f,.38f,.12f):new Color(.1f,.035f,.015f);
                if(m_Regenerating)color*=.65f+.35f*Mathf.Sin(Time.unscaledTime*4-i*.6f);
                m_EquipmentProperties.Clear();m_EquipmentProperties.SetColor("_BaseColor",color);m_ChargeBars[i].SetPropertyBlock(m_EquipmentProperties);
            }
        }
    }
}
