// 用小队舞台的相机画面做面板磨砂玻璃。对同一帧做横向和纵向模糊，各面板裁对应区域，避免为每块玻璃单独渲染相机。
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MP_FPS.Client {
    // 沿用小队相机画面。所有磨砂玻璃从同一张低分辨率模糊图取样。
    internal sealed class SquadGlass : IDisposable {
        private const int MaxEdge = 512;
        private static readonly int BlurStep = Shader.PropertyToID("_BlurStep");
        private static readonly int ApplyTint = Shader.PropertyToID("_ApplyTint");
        private readonly Image m_StageImage;
        private readonly OnlineSquadStage m_Stage;
        private readonly List<Image> m_Layers = new();
        private readonly Material m_Material;
        private RenderTexture m_Horizontal, m_Frosted;

        public SquadGlass(VisualElement screen, Image stageImage, OnlineSquadStage stage) {
            m_StageImage = stageImage;
            m_Stage = stage;
            var shader = Resources.Load<Shader>("Moonkov/UI/SquadGlass");
            if (shader == null || !shader.isSupported) return;
            m_Material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            foreach (var panel in screen.Query<VisualElement>(className: "squad-glass").ToList()) {
                var layer = new Image { pickingMode = PickingMode.Ignore, scaleMode = ScaleMode.StretchToFill };
                layer.AddToClassList("squad-glass-image");
                panel.hierarchy.Insert(0, layer);
                panel.RegisterCallback<GeometryChangedEvent, Image>(PanelGeometryChanged, layer);
                layer.RegisterCallback<GeometryChangedEvent>(LayerGeometryChanged);
                m_Layers.Add(layer);
            }
            m_Stage.FrameChanged += Render;
        }

        private void PanelGeometryChanged(GeometryChangedEvent evt, Image layer) { SetCrop(layer); }
        private void LayerGeometryChanged(GeometryChangedEvent evt) { SetCrop((Image)evt.currentTarget); }

        private void SetCrop(Image layer) {
            if (m_Frosted == null) return;
            // 玻璃所在位置和舞台图片使用同一屏幕坐标。以相对位置算取样区域，模糊图能对上背后的人物。
            Rect stage = m_StageImage.worldBound, card = layer.worldBound;
            if (stage.width <= 0 || stage.height <= 0 || card.width <= 0 || card.height <= 0) return;
            var uv = new Rect((card.xMin - stage.xMin) / stage.width, (stage.yMax - card.yMax) / stage.height,
                card.width / stage.width, card.height / stage.height);
            if (uv != layer.uv) layer.uv = uv;
        }

        private void Render(RenderTexture frame) {
            if (frame == null) {
                ReleaseTextures();
                return;
            }
            var viewport = m_StageImage.contentRect.size;
            if (viewport.x < 2 || viewport.y < 2) return;
            // 模糊不用全分辨率。缩小目标纹理并给最长一边设上限，在小队持续渲染时控制额外开销。
            float scale = Mathf.Min(.4f, MaxEdge / Mathf.Max(viewport.x, viewport.y));
            int width = Mathf.Max(1, Mathf.RoundToInt(viewport.x * scale));
            int height = Mathf.Max(1, Mathf.RoundToInt(viewport.y * scale));
            if (m_Frosted == null || m_Frosted.width != width || m_Frosted.height != height) {
                ReleaseTextures();
                m_Horizontal = CreateTexture("Squad glass horizontal", width, height);
                m_Frosted = CreateTexture("Squad glass backdrop", width, height);
            }
            // 临时改变 GPU 当前渲染目标前先记住原值。模糊之后恢复，后续界面可以照常绘制。
            var previous = RenderTexture.active;
            m_Material.SetVector(BlurStep, new Vector4(3.4f / viewport.x, 0, 0, 0));
            m_Material.SetFloat(ApplyTint, 0);
            // 第一遍只横向模糊，第二遍纵向并加玻璃染色。所有玻璃层最后复用同一张处理过的图。
            Graphics.Blit(frame, m_Horizontal, m_Material, 0);
            m_Material.SetVector(BlurStep, new Vector4(0, 3.4f / viewport.y, 0, 0));
            m_Material.SetFloat(ApplyTint, 1);
            Graphics.Blit(m_Horizontal, m_Frosted, m_Material, 0);
            RenderTexture.active = previous;
            foreach (var layer in m_Layers) {
                if (layer.image != m_Frosted) layer.image = m_Frosted;
                SetCrop(layer);
            }
        }

        private static RenderTexture CreateTexture(string name, int width, int height) {
            return new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32) {
                name = name, hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear
            };
        }

        private void ReleaseTextures() {
            if (m_Frosted == null) return;
            // 准备释放纹理时先断开 UI 对它的引用。舞台下次出图可以提供新的目标，不留已销毁的资源。
            foreach (var layer in m_Layers) layer.image = null;
            m_Horizontal.Release();
            m_Frosted.Release();
            Destroy(m_Horizontal);
            Destroy(m_Frosted);
            m_Horizontal = m_Frosted = null;
        }

        private static void Destroy(UnityEngine.Object value) {
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(value);
            else
                UnityEngine.Object.DestroyImmediate(value);
        }

        public void Dispose() {
            m_Stage.FrameChanged -= Render;
            ReleaseTextures();
            foreach (var layer in m_Layers) {
                layer.parent.UnregisterCallback<GeometryChangedEvent, Image>(PanelGeometryChanged);
                layer.UnregisterCallback<GeometryChangedEvent>(LayerGeometryChanged);
                layer.RemoveFromHierarchy();
            }
            m_Layers.Clear();
            if (m_Material != null) Destroy(m_Material);
        }
    }
}