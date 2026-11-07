using UnityEngine;
using UnityEngine.Rendering;

namespace Unity.MP_FPS
{
    public sealed partial class EnergyShieldPresentation
    {
        private Transform m_Badge, m_BadgeAnchor;
        private GameObject m_BadgeModel;
        private Renderer[] m_BadgeRenderers;
        private Vector3 m_BadgeOffset;
        private Quaternion m_BadgeFrame;
        private byte m_BadgeStyle, m_BadgeBuiltStyle;

        private void FitBadge(Animator animator)
        {
            m_BadgeAnchor=animator.GetBoneTransform(HumanBodyBones.UpperChest);
            if(!m_BadgeAnchor)m_BadgeAnchor=m_Chest?m_Chest:m_Neck?m_Neck:m_Hips;
            if(!m_BadgeAnchor)return;
            var frame=Quaternion.LookRotation(transform.forward,transform.up);
            // Work in the imported bind pose. A menu/late spawn can already be
            // leaning or gesturing; that pose must not become the fit reference.
            var renderers=GetComponentsInChildren<SkinnedMeshRenderer>(true);
            Matrix4x4 anchorBind=m_BadgeAnchor.localToWorldMatrix;
            Vector3 neckBind=m_Neck?m_Neck.position:m_BadgeAnchor.position;
            foreach(var renderer in renderers)
            {
                if(!renderer.sharedMesh || renderer.name.Contains("Runtime"))continue;
                var data=EnergyShieldFittingData.For(renderer);var bones=renderer.bones;
                int anchor=System.Array.IndexOf(bones,m_BadgeAnchor),neck=System.Array.IndexOf(bones,m_Neck);
                if(anchor<0 || data.Bindposes.Length!=bones.Length)continue;
                anchorBind=renderer.localToWorldMatrix*data.Bindposes[anchor].inverse;
                if(neck>=0)neckBind=(renderer.localToWorldMatrix*data.Bindposes[neck].inverse).GetColumn(3);
                break;
            }
            var center=neckBind-transform.up*.095f-transform.right*.065f;
            var inverse=Quaternion.Inverse(frame);
            float front=float.NegativeInfinity;
            foreach(var renderer in renderers)
            {
                var mesh=renderer.sharedMesh;if(!mesh || renderer.name.Contains("Runtime"))continue;
                var data=EnergyShieldFittingData.For(renderer);
                foreach(int i in data.Torso)
                {
                    Vector3 world=renderer.localToWorldMatrix.MultiplyPoint3x4(data.Vertices[i]);
                    Vector3 local=inverse*(world-center);
                    if(Mathf.Abs(local.x)>.048f || Mathf.Abs(local.y)>.048f || Mathf.Abs(local.z)>.24f)continue;
                    front=Mathf.Max(front,local.z);
                }
            }
            if(float.IsNegativeInfinity(front))front=.09f;
            m_BadgeOffset=anchorBind.inverse.MultiplyPoint3x4(center+frame*new Vector3(0,0,front+.007f));
            m_BadgeFrame=Quaternion.Inverse(anchorBind.rotation)*frame;
            m_Badge=new GameObject("Equipped subspace chest badge"){layer=gameObject.layer}.transform;
            m_Badge.SetParent(m_Root,false);m_Badge.gameObject.SetActive(false);
        }

        private void UpdateBadge()
        {
            if(!m_Badge || !m_BadgeAnchor)return;
            m_Badge.gameObject.SetActive(m_BadgeStyle!=0);
            if(m_BadgeStyle==0)return;
            if(m_BadgeBuiltStyle!=m_BadgeStyle)
            {
                if(m_BadgeRenderers!=null)foreach(var renderer in m_BadgeRenderers)m_Hardware.Remove(renderer);
                if(m_BadgeModel){m_BadgeModel.SetActive(false);DestroyOwned(m_BadgeModel);}
                m_BadgeBuiltStyle=m_BadgeStyle;
                m_BadgeModel=PhysicalItemVisuals.Create(EnergyShieldEquipment.BadgeCode(m_BadgeStyle),m_Badge,true);
                m_BadgeRenderers=m_BadgeModel?m_BadgeModel.GetComponentsInChildren<Renderer>(true):null;
                if(m_BadgeModel)
                {
                    foreach(var node in m_BadgeModel.GetComponentsInChildren<Transform>(true))node.gameObject.layer=gameObject.layer;
                    foreach(var renderer in m_BadgeRenderers)
                    {renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;renderer.forceRenderingOff=m_Hidden;m_Hardware.Add(renderer);}
                    Unity.MP_FPS.Moon.MoonSilverPrintSubject.Protect(m_BadgeModel);
                }
            }
            WorldPose(m_Badge,m_BadgeAnchor.TransformPoint(m_BadgeOffset),m_BadgeAnchor.rotation*m_BadgeFrame,Vector3.one);
        }
    }
}
