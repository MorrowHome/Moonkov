// 把服务器掉落的异形残骸表示成场景里的采集物模型。跟随掉落编号和落点创建外观，掉落内容由战局库存记录。
using UnityEngine;

namespace Unity.MP_FPS {
    /// <summary>为服务端现有掉落容器保留的客户端标记。
    /// CharacterIndex 为 -1 表示异形残骸，人形尸体使用 0 到 2。</summary>
    public sealed class AlienLootVisual : MonoBehaviour {
        private Material m_Material;

        public static GameObject Create(Transform parent, RaidCorpseRpc bag) {
            // Resources 中序列化的材质让发布包保留预期的无关键字 Lit 变体。
            // 从 Resources 取可随构建装入的材质，给拾取残骸复用场景一致的灯光表现。
            var template = Resources.Load<Material>("Moonkov/AlienRemains");
            if (!template || !template.shader || !template.shader.isSupported)
                throw new System.InvalidOperationException("Missing or unsupported Moonkov/AlienRemains material.");
            // 用掉落编号命名远端能看见的对象，下面挂程序拼出的残骸外形。
            var root = new GameObject("Alien remains / " + bag.LootId);
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(bag.Position, bag.Rotation);
            var view = root.AddComponent<AlienLootVisual>();
            view.m_Material = new Material(template);
            for (int i = 0; i < 5; i++) {
                var lobe = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                lobe.name = "Collapsed alien tissue";
                lobe.transform.SetParent(root.transform, false);
                float angle = i * Mathf.PI * 2 / 5;
                lobe.transform.localPosition = new Vector3(Mathf.Cos(angle) * .19f, .1f, Mathf.Sin(angle) * .19f);
                lobe.transform.localScale = new Vector3(.38f, .18f, .27f);
                lobe.GetComponent<Renderer>().sharedMaterial = view.m_Material;
                Destroy(lobe.GetComponent<Collider>());
            }
            return root;
        }

        // 这里复制了材质实例，移除掉落显示时一并释放。
        private void OnDestroy() {
            if (m_Material) Destroy(m_Material);
        }
    }
}