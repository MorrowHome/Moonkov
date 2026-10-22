using System;
using UnityEngine;

namespace Unity.MP_FPS
{
    public sealed partial class EnergyShieldPresentation
    {
        private Vector3 m_CollarOffset;
        private Quaternion m_CollarLocalFrame;
        private Vector2 m_CollarRadii;
        private float m_CollarHeight;

        private void FitCollar(Animator animator)
        {
            var anchor=m_Neck?m_Neck:m_Head;
            Vector3 axis=m_Head.position-anchor.position;
            float length=axis.magnitude;
            axis=length>.001f?axis/length:transform.up;
            Vector3 forward=Vector3.ProjectOnPlane(transform.forward,axis);
            if(forward.sqrMagnitude<.001f)forward=Vector3.ProjectOnPlane(transform.up,axis);
            Quaternion frame=Quaternion.LookRotation(forward,axis);
            // The DollSinger's high shirt collar covers the lower neck. Fit the exposed
            // upper neck below the jaw, rather than enlarging a band hidden inside the shirt.
            Vector3 center=m_Neck?Vector3.Lerp(m_Neck.position,m_Head.position,.68f):m_Head.position-transform.up*.025f;
            m_CollarHeight=Mathf.Clamp(length*.25f,.012f,.016f);
            var minimum=Vector2.one*float.PositiveInfinity;
            var maximum=Vector2.one*float.NegativeInfinity;
            int samples=0;
            var renderers=animator.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach(var renderer in renderers)
            {
                var mesh=renderer.sharedMesh;if(!mesh||renderer.name.Contains("Runtime"))continue;
                var bones=renderer.bones;int neck=Array.IndexOf(bones,m_Neck),head=Array.IndexOf(bones,m_Head);
                if(neck<0||head<0)continue;
                var weights=mesh.boneWeights;if(weights.Length!=mesh.vertexCount)continue;
                // Body skin only: neck jewellery and shoulder fabric must not enlarge the fit.
                bool[] skin=new bool[mesh.vertexCount];bool hasSkin=false;
                var materials=renderer.sharedMaterials;
                for(int sub=0;sub<Mathf.Min(mesh.subMeshCount,materials.Length);sub++)
                {
                    string name=materials[sub]?materials[sub].name:"";
                    if(!name.Equals("Body",StringComparison.OrdinalIgnoreCase)&&name.IndexOf("Skin",StringComparison.OrdinalIgnoreCase)<0)continue;
                    foreach(int index in mesh.GetTriangles(sub))skin[index]=true;
                    hasSkin=true;
                }
                var vertices=mesh.vertices;var bind=mesh.bindposes;var matrices=new Matrix4x4[bones.Length];
                if(bind.Length!=bones.Length)continue;
                for(int i=0;i<bones.Length;i++)if(bones[i])matrices[i]=bones[i].localToWorldMatrix*bind[i];
                Quaternion inverse=Quaternion.Inverse(frame);
                for(int i=0;i<vertices.Length;i++)
                {
                    if(hasSkin&&!skin[i])continue;
                    var w=weights[i];
                    float influence=NeckWeight(w.boneIndex0,w.weight0,neck,head)+NeckWeight(w.boneIndex1,w.weight1,neck,head)+
                        NeckWeight(w.boneIndex2,w.weight2,neck,head)+NeckWeight(w.boneIndex3,w.weight3,neck,head);
                    if(influence<.7f)continue;
                    Vector3 v=vertices[i];
                    Vector3 world=matrices[w.boneIndex0].MultiplyPoint3x4(v)*w.weight0+matrices[w.boneIndex1].MultiplyPoint3x4(v)*w.weight1+
                        matrices[w.boneIndex2].MultiplyPoint3x4(v)*w.weight2+matrices[w.boneIndex3].MultiplyPoint3x4(v)*w.weight3;
                    Vector3 local=inverse*(world-center);
                    if(Mathf.Abs(local.y)>m_CollarHeight*.5f+.002f||Mathf.Abs(local.x)>.065f||Mathf.Abs(local.z)>.065f)continue;
                    var point=new Vector2(local.x,local.z);minimum=Vector2.Min(minimum,point);maximum=Vector2.Max(maximum,point);samples++;
                }
            }
            m_CollarRadii=new Vector2(.024f,.027f);
            if(samples>=8)
            {
                Vector2 midpoint=(minimum+maximum)*.5f;
                center+=frame*new Vector3(midpoint.x,0,midpoint.y);
                m_CollarRadii=(maximum-minimum)*.5f+Vector2.one*.0012f;
            }
            // Store the fit in the real neck's space, including the imported FBX scale.
            m_CollarOffset=anchor.InverseTransformPoint(center);
            m_CollarLocalFrame=Quaternion.Inverse(anchor.rotation)*frame;
        }
        private static float NeckWeight(int index,float weight,int neck,int head)=>index==neck||index==head?weight:0;

        private void BuildCollar(Material material,Material indicator)
        {
            Color navy=new Color(.055f,.075f,.15f),gold=new Color(.83f,.65f,.32f),lavender=new Color(.51f,.43f,.76f);
            Color highlight=new Color(.75f,.68f,.93f),ink=new Color(.035f,.025f,.055f);
            float x=m_CollarRadii.x,z=m_CollarRadii.y,h=m_CollarHeight;
            CollarPiece("Midnight satin neckband",EnergyShieldGeometry.NeckBand,Vector3.zero,new Vector3(x,h,z),material,navy);
            for(int side=-1;side<=1;side+=2)
                CollarPiece("Champagne edge piping",EnergyShieldGeometry.NeckBand,Vector3.up*(side*(h*.5f-.0007f)),new Vector3(x*1.008f,.0013f,z*1.008f),material,gold,true);
            float front=z*1.085f+.002f;
            for(int side=-1;side<=1;side+=2)
            {
                var loop=CollarPiece("Lilac folded bow",EnergyShieldGeometry.BowLoop,new Vector3(side*.001f,-.004f,front+.002f),new Vector3(.016f,.014f,.018f),material,lavender);
                loop.transform.localRotation=Quaternion.Euler(0,side<0?180:0,side*10);
                var tail=CollarPiece("Notched satin ribbon",EnergyShieldGeometry.BowTail,new Vector3(side*.003f,-.006f,front+.0025f),new Vector3(.009f,.011f,.008f),material,highlight);
                tail.transform.localRotation=Quaternion.Euler(-12,0,side*22);
            }
            CollarPiece("Bow centre wrap",EnergyShieldGeometry.Sphere,new Vector3(0,-.004f,front+.004f),new Vector3(.0035f,.0045f,.0028f),material,highlight);
            var loopRing=CollarPiece("Bell suspension ring",EnergyShieldGeometry.Collar,new Vector3(0,-.010f,front+.006f),new Vector3(.0022f,.0022f,.0022f),material,gold,true);
            loopRing.transform.localRotation=Quaternion.Euler(90,0,0);
            Vector3 bell=new Vector3(0,-.017f,front+.008f);
            CollarPiece("Gold neko bell",EnergyShieldGeometry.Sphere,bell,new Vector3(.0068f,.0065f,.0058f),material,gold,true);
            for(int side=-1;side<=1;side+=2)
            {
                var ear=CollarPiece("Neko bell ear",EnergyShieldGeometry.CatEar,bell+new Vector3(side*.0041f,.004f,0),new Vector3(.0048f,.0055f,.004f),material,gold,true);
                ear.transform.localRotation=Quaternion.Euler(-8,0,-side*15);
                CollarPiece("Neko bell eye",EnergyShieldGeometry.Sphere,bell+new Vector3(side*.0022f,.001f,.00545f),new Vector3(.0007f,.0009f,.0003f),indicator,ink);
            }
            CollarPiece("Bell sound slit",EnergyShieldGeometry.Box,bell+new Vector3(0,-.0024f,.0055f),new Vector3(.0074f,.00065f,.0005f),indicator,ink);
            CollarPiece("Bell lower slot",EnergyShieldGeometry.Box,bell+new Vector3(0,-.0036f,.0049f),new Vector3(.00065f,.0028f,.0005f),indicator,ink);
            var clasp=CollarPiece("Rear gold clasp",EnergyShieldGeometry.Collar,new Vector3(0,0,-z*1.085f-.001f),new Vector3(.006f,.0013f,.0055f),material,gold,true);
            clasp.transform.localRotation=Quaternion.Euler(90,0,0);
            CollarPiece("Rear oxygen module",EnergyShieldGeometry.Box,new Vector3(0,0,-z*1.085f-.002f),new Vector3(.006f,.008f,.003f),material,navy);
            CollarPiece("Oxygen indicator",EnergyShieldGeometry.Sphere,new Vector3(x*1.085f,.001f,0),new Vector3(.0012f,.0014f,.0028f),indicator,new Color(.25f,.8f,.83f));
        }
        private MeshRenderer CollarPiece(string name,Mesh mesh,Vector3 position,Vector3 scale,Material material,Color color,bool metallic=false)
        {
            var piece=Hardware(m_Collar,mesh,position,scale,material,color);piece.name=name;
            var properties=new MaterialPropertyBlock();piece.GetPropertyBlock(properties);
            properties.SetFloat("_Metallic",metallic ? .78f : 0f);properties.SetFloat("_Smoothness",metallic ? .72f : .3f);piece.SetPropertyBlock(properties);
            return piece;
        }
        private void UpdateCollarPose()
        {
            var anchor=m_Neck?m_Neck:m_Head;
            WorldPose(m_Collar,anchor.TransformPoint(m_CollarOffset),anchor.rotation*m_CollarLocalFrame,Vector3.one);
        }
    }
}
