using Unity.Mathematics;
using Unity.NetCode;
using UnityEngine;
using UnityEngine.Rendering;

namespace Unity.MP_FPS
{
    public struct ClientHaloMarkRpc : IRpcCommand
    {
        public int OwnerNetworkId;
        public uint ShotTick;
        public float3 Position;
    }

    // All human players in the current cooperative raid share these marks.
    // Fixed impact points, depth-tested in the world; no tracking or wall reveal.
    public sealed class HaloHitMarkerFeedback : MonoBehaviour
    {
        private sealed class Mark
        {
            public Transform Root;
            public LineRenderer Line;
            public int Owner;
            public uint Tick;
            public float Until;
        }
        private readonly Mark[] marks = new Mark[8];
        private Material material;
        private Camera camera;
        public void Present(ClientHaloMarkRpc rpc)
        {
            if (rpc.OwnerNetworkId <= 0 || !math.all(math.isfinite(rpc.Position))) return;
            if (!PlayerGhostManager.TryGetInstanceByRole(MultiplayerRole.ClientOwned, out var manager) ||
                !manager.TryGetPlayersByRole(MultiplayerRole.ClientOwned, out var players) || players.Count == 0) return;
            var local = players[0];
            if (!local || !local.GhostGameObject || local.GhostGameObject.ReadGhostComponentData<PredictedPlayerGhost>().CurrentHealth <= 0) return;
            camera = local.GetPlayerCamera();
            if (!material) material = new Material(Shader.Find("DollSinger/RifleHaloLine")) { hideFlags = HideFlags.DontSave };
            int slot = -1;
            float oldest = float.MaxValue;
            for (int i=0;i<marks.Length;i++)
            {
                var existing=marks[i];
                if (existing != null && existing.Owner == rpc.OwnerNetworkId)
                {
                    if (existing.Tick == rpc.ShotTick || (int)(rpc.ShotTick-existing.Tick) <= 0) return;
                    slot=i; break;
                }
                float until=existing?.Until ?? 0;
                if (until<oldest) { oldest=until; slot=i; }
            }
            var mark=marks[slot];
            if (mark == null)
            {
                var root=new GameObject("Confirmed teammate hit position"); root.transform.SetParent(transform,false);
                var line=root.AddComponent<LineRenderer>(); line.sharedMaterial=material; line.useWorldSpace=false;
                line.alignment=LineAlignment.TransformZ; line.positionCount=5; line.widthMultiplier=.018f;
                line.shadowCastingMode=ShadowCastingMode.Off; line.receiveShadows=false;
                line.SetPositions(new[] { Vector3.up*.18f, Vector3.right*.18f, Vector3.down*.18f, Vector3.left*.18f, Vector3.up*.18f });
                mark=marks[slot]=new Mark {Root=root.transform,Line=line};
            }
            mark.Owner=rpc.OwnerNetworkId; mark.Tick=rpc.ShotTick; mark.Until=Time.unscaledTime+3f;
            mark.Root.position=(Vector3)rpc.Position+Vector3.up*.12f; mark.Root.gameObject.SetActive(true);
        }
        private void LateUpdate()
        {
            foreach(var mark in marks)
            {
                if(mark==null || !mark.Root.gameObject.activeSelf) continue;
                float remaining=mark.Until-Time.unscaledTime;
                if(remaining<=0) {mark.Root.gameObject.SetActive(false);continue;}
                if(camera)
                {
                    mark.Root.rotation=camera.transform.rotation;
                    mark.Root.localScale=Vector3.one*Mathf.Clamp(Vector3.Distance(camera.transform.position,mark.Root.position)*.025f,.7f,3f);
                }
                var color=new Color(.82f,.55f,1f,Mathf.Min(1,remaining/.5f));
                mark.Line.startColor=mark.Line.endColor=color;
            }
        }
        private void OnDestroy()
        { if(material) { if(Application.isPlaying) Destroy(material); else DestroyImmediate(material); } }
    }
}
