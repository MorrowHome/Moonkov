// 动作专用道具上的挂点与可动零件。RaidActionPresentation 用这些点对齐手和嘴，也可驱动注射器等道具上的活动部件。
using UnityEngine;

namespace Unity.MP_FPS {
    // 动作模型是库存实物的变体，挂点坐标以米配置。
    // 原掉落和图标用的预制体保持原状。
    public sealed class RaidActionProp : MonoBehaviour {
        public Transform Grip, Contact, Cap, Plunger;
        public GameObject Sealed, Opened;
        public GameObject Needle;
        public float PlungerTravel;

        [System.Serializable]
        public struct Part {
            public string Name;
            public Transform Transform;
        }

        public Part[] Parts;
        public bool Authored;

        // 按名称找只在动作版本存在的零件。食品和针剂的道具不必共用同一骨架。
        public Transform FindPart(string name) {
            if (Parts != null)
                foreach (var part in Parts)
                    if (part.Name == name)
                        return part.Transform;
            if (name == "Cap") return Cap;
            if (name == "Plunger") return Plunger;
            if (name == "Sealed" && Sealed) return Sealed.transform;
            if ((name == "Opened" || name == "Body") && Opened) return Opened.transform;
            if (name == "Needle" && Needle) return Needle.transform;
            return null;
        }

        // 根据当前动作阶段修改零件姿势，例如针剂在完成注射时推进部件。
        public void Present(float phase, bool injecting) {
            bool open = phase >= .18f;
            if (Sealed) Sealed.SetActive(!open);
            if (Opened) Opened.SetActive(!Sealed || open);
            if (Cap) Cap.gameObject.SetActive(!open);
            if (Needle) Needle.SetActive(open);
            if (Plunger)
                Plunger.localPosition = Vector3.down * (injecting
                    ? PlungerTravel * Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.46f, .72f, phase))
                    : 0);
        }
    }
}