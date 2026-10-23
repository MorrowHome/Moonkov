using UnityEngine;
using UnityEngine.Rendering;

namespace Unity.MP_FPS.DollSinger
{
    // One flat, vertex-coloured mesh. All module geometry shares a draw call;
    // buffers and renderer are reused while aiming, charging and firing.
    public sealed class HaloModuleVisual : MonoBehaviour
    {
        private readonly Vector3[] vertices = new Vector3[2048];
        private readonly Color[] colors = new Color[2048];
        private readonly int[] triangles = new int[3072];
        private Mesh mesh;
        private Material material;
        private HaloModule modules;
        private int capacity = 4, ammo = 4, target, pulseIndex;
        private int vertexCount;
        private float aim, progress, elapsed, flash;
        private bool reloading, rifle;
        private uint lastShot;
        private bool initialized;
        private static readonly Color Gold = new Color(1f, .74f, .28f);
        private static readonly Color Cyan = new Color(.3f, .94f, 1f);
        private static readonly Color Violet = new Color(.82f, .55f, 1f);

        public void Configure(bool isRifle)
        {
            rifle = isRifle;
            if (mesh) return;
            for (int i = 0, t = 0; i < vertices.Length; i += 4, t += 6)
            { triangles[t] = i; triangles[t+1] = i+1; triangles[t+2] = i+2; triangles[t+3] = i; triangles[t+4] = i+2; triangles[t+5] = i+3; }
            mesh = new Mesh { name = "Flat halo module geometry", hideFlags = HideFlags.DontSave };
            mesh.MarkDynamic();
            material = new Material(Shader.Find("DollSinger/RifleHaloLine")) { hideFlags = HideFlags.DontSave };
            material.SetFloat("_Gain", 1.6f);
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = gameObject.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
        }

        public void SetState(ushort mask, int rounds, int magazine, bool reload, float chargeProgress, int targetAmmo, uint tick, int burstIndex = 0)
        {
            modules = (HaloModule)mask; ammo = rounds; capacity = Mathf.Max(1, magazine);
            reloading = reload; progress = Mathf.Clamp01(chargeProgress); target = Mathf.Max(rounds, targetAmmo);
            if (lastShot != tick && tick != 0 && (lastShot == 0 || (int)(tick - lastShot) > 0))
            { if (initialized) flash = 1f; lastShot = tick; pulseIndex = Mathf.Clamp(burstIndex, 0, 2); }
            initialized = true;
            // Runtime state and aim setters are batched before LateUpdate. Editor
            // previews also read the mesh immediately, without a player loop.
            if (!Application.isPlaying) Rebuild();
        }
        public void SetAimBlend(float value) => aim = Mathf.Clamp01(value);
        private void LateUpdate() => Step(Time.deltaTime);
        public void Step(float dt)
        { elapsed += Mathf.Max(0, dt); flash *= Mathf.Exp(-Mathf.Max(0, dt) * 11f); Rebuild(); }
        private static Vector3 Polar(float radius, float angle)
        { float a = angle * Mathf.Deg2Rad; return new Vector3(Mathf.Cos(a)*radius, Mathf.Sin(a)*radius, -.003f); }
        private void Line(Vector3 a, Vector3 b, float width, Color color)
        {
            if (vertexCount + 4 > vertices.Length) return;
            Vector3 normal = Vector3.Cross((b-a).normalized, Vector3.forward) * width * .5f;
            int n = vertexCount; vertices[n]=a+normal; vertices[n+1]=b+normal; vertices[n+2]=b-normal; vertices[n+3]=a-normal;
            for (int i=0;i<4;i++) colors[n+i]=color;
            vertexCount += 4;
        }
        private void Arc(float radius, float start, float span, float width, Color color, int steps = 8)
        {
            for (int i=0;i<steps;i++) Line(Polar(radius,start+span*i/steps),Polar(radius,start+span*(i+1)/steps),width,color);
        }
        private static Color Fade(Color color, float alpha) { color.a = alpha; return color; }
        private void Diamond(float radius, float angle, float size, Color color)
        {
            var p = Polar(radius, angle);
            Line(p+Vector3.up*size,p+Vector3.right*size,.007f,color);
            Line(p+Vector3.right*size,p-Vector3.up*size,.007f,color);
            Line(p-Vector3.up*size,p-Vector3.right*size,.007f,color);
            Line(p-Vector3.right*size,p+Vector3.up*size,.007f,color);
        }
        private void Rebuild()
        {
            if (!mesh) return;
            vertexCount = 0;
            float radius = Mathf.Lerp(.43f, .35f, aim) + flash * .025f;
            int visible = reloading ? Mathf.FloorToInt(Mathf.Lerp(ammo, target, progress)) : ammo;
            if ((modules & HaloModule.Capacitor) != 0)
            {
                int segments = rifle ? 12 : capacity;
                int lit = Mathf.CeilToInt(Mathf.Clamp01((float)visible/capacity)*segments);
                for (int i=0;i<segments;i++) Arc(radius+.045f,90+i*360f/segments,360f/segments-6,.012f,Fade(Gold,i<lit?.95f:.16f));
            }
            if ((modules & HaloModule.MooncoreRecycler) != 0)
            {
                var silver = new Color(.68f, .81f, .94f);
                for (int i=0;i<3;i++)
                {
                    float angle=30+i*120;
                    Arc(radius+.055f,angle,88,.015f,Fade(silver,.75f+flash*.25f),12);
                    Arc(radius+.025f,angle+8,72,.01f,Fade(Cyan,.35f+flash*.65f),10);
                    Line(Polar(radius+.025f,angle+8),Polar(radius+.055f,angle+8),.018f,Fade(silver,.9f));
                    float flow=angle+80-(reloading ? (progress*2+elapsed*.4f)%1 : flash)*62;
                    Arc(radius+.04f,flow,14,.018f,Fade(Cyan,reloading?.95f:.3f+flash*.7f),3);
                }
            }
            if ((modules & HaloModule.ChargeBridge) != 0)
                for (int i=0;i<6;i++)
                {
                    float a = 30+i*60;
                    Arc(radius+.025f,a-8,16,.009f,Fade(Cyan,.8f));
                    if (reloading)
                    {
                        float wave = .45f+.55f*Mathf.Sin(elapsed*24-i*1.1f);
                        Line(Polar(radius-.085f,a),Polar(radius+.025f,a),.012f,Fade(Cyan,wave));
                    }
                }
            if ((modules & HaloModule.FocusLens) != 0)
                for (int i=0;i<4;i++)
                {
                    float a = 45+i*90;
                    float r = radius-Mathf.Lerp(0,.055f,aim);
                    Arc(r,a-27,54,.013f,Fade(Cyan,.95f));
                    Line(Polar(r+.045f,a-15),Polar(r,a),.009f,Cyan);
                    Line(Polar(r,a),Polar(r+.045f,a+15),.009f,Cyan);
                }
            if ((modules & HaloModule.FanArray) != 0)
                for (int i=0;i<6;i++)
                {
                    float a = 30+i*60;
                    float r = radius+.035f+flash*.04f;
                    Line(Polar(r-.065f,a),Polar(r+.025f,a-17),.012f,Gold);
                    Arc(r+.025f,a-17,34,.012f,Gold);
                    Line(Polar(r+.025f,a+17),Polar(r-.065f,a),.012f,Gold);
                }
            if ((modules & HaloModule.PulseController) != 0)
                for (int i=0;i<3;i++) Diamond(radius+.02f,60+i*30,.026f,Fade(Violet,i==pulseIndex?.5f+flash*.5f:.25f));
            if ((modules & HaloModule.HitMarker) != 0)
            {
                Diamond(radius+.015f,270,.032f,Fade(Violet,.85f));
                Arc(radius+.015f,252,36,.006f,Fade(Violet,.4f));
            }
            if((modules&HaloModule.BiologicalCapacity)!=0)for(int i=0;i<8;i++)Arc(radius+.09f,i*45+4,32,.008f,Fade(Gold,.7f),4);
            if((modules&HaloModule.BiologicalCharging)!=0)for(int i=0;i<4;i++)Line(Polar(radius-.04f,i*90),Polar(radius+.07f,i*90+10),.009f,Fade(Cyan,.7f));
            if((modules&HaloModule.BiologicalStability)!=0)for(int i=0;i<4;i++)Arc(radius-.06f,i*90+20,35,.008f,Fade(Cyan,.6f),4);
            if ((modules & (HaloModule.BiologicalResearch | HaloModule.AlienSurge | HaloModule.FoodEffectMask)) != 0)
            {
                float pulse = .75f + .15f * Mathf.Sin(elapsed * 3) + flash * .1f;
                var effect=(AlienFoodEffect)((ushort)modules>>12);
                Color bio=effect==AlienFoodEffect.Regeneration || effect==AlienFoodEffect.OxygenReserve ? new Color(.4f,1,.6f) :
                    effect==AlienFoodEffect.VenomPower || effect==AlienFoodEffect.ContractilePower ? new Color(1,.4f,.3f) : Violet;
                for (int i = 0; i < 6; i++)
                {
                    float angle = i * 60 + elapsed * 8;
                    Arc(radius + .06f, angle + 8, 40, .014f, Fade(bio, pulse), 6);
                    Diamond(radius + .06f, angle, .021f, Fade(bio, pulse));
                }
            }
            mesh.Clear(false);
            mesh.SetVertices(vertices,0,vertexCount); mesh.SetColors(colors,0,vertexCount);
            mesh.SetTriangles(triangles,0,vertexCount/4*6,0,true);
        }
        private void OnDestroy()
        {
            if (mesh) { if(Application.isPlaying) Destroy(mesh); else DestroyImmediate(mesh); }
            if (material) { if(Application.isPlaying) Destroy(material); else DestroyImmediate(material); }
        }
    }
}
