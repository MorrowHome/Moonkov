using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Unity.MP_FPS.Moon
{
    [Serializable]
    public sealed class MoonSurfaceDetailSettings
    {
        public bool enabled = true;
        public int seed = 7319;
        [Range(0, 1600)] public int shallowCraters = 768;
        [Range(0, 40)] public int landformCraters = 18;
        [Range(.02f, .25f)] public float craterDepthRatio = .15f;
        [Range(0, 700)] public int fragmentsPerTile = 420;
        [Range(1, 6)] public int tileRadius = 5;
        [Range(.02f, .24f)] public float largestFragment = .22f;
        [Range(0, 1200)] public int outcropRocks = 480;
    }

    /// <summary>
    /// Runtime-only near-field geology. Seeded terrain modifications run on every
    /// peer, including headless servers, so the collider matches. Small fragments
    /// have no colliders and are streamed in combined meshes around the local view.
    /// No source TerrainData, authored rocks, or gameplay state is modified.
    /// </summary>
    internal sealed class MoonSurfaceDetail
    {
        private const float TileSize = 12;
        private readonly Terrain terrain;
        private readonly TerrainCollider collider;
        private readonly TerrainData source, data;
        private readonly MoonSurfaceDetailSettings settings;
        private readonly Vector3 origin, size;
        private readonly Material material;
        private readonly Transform parent;
        private readonly List<Crater> craters = new();
        private readonly Dictionary<Vector2Int, GameObject> tiles = new();
        private readonly List<Vector2Int> expired = new();
        private readonly MoonRockMesh rockShapes;
        private readonly Vector3 protectedSpawn;
        private readonly List<GameObject> outcrops = new();
        private Camera camera;
        private float nextCamera;
        private Texture2D geometry, impactGeology;
        private Color[] impactPixels;
        private const int ImpactResolution = 1024;
        private struct Crater { public Vector2 center; public float radius, freshness; }

        public MoonSurfaceDetail(Terrain near, Transform detailRoot, Material rock, MoonSurfaceDetailSettings configuration, Vector3 spawn)
        {
            terrain = near; parent = detailRoot; material = rock; settings = configuration;
            protectedSpawn = spawn;
            rockShapes = new MoonRockMesh(settings.seed);
            origin = near.transform.position; source = near.terrainData; size = source.size;
            collider = near.GetComponent<TerrainCollider>();
            data = Object.Instantiate(source);
            data.name = source.name + " (runtime shallow craters)";
            terrain.terrainData = data;
            if (collider) collider.terrainData = data;
            CreateCraters();
            CreateOutcrops();
        }

        private void CreateCraters()
        {
            var random = new System.Random(settings.seed);
            int resolution = data.heightmapResolution;
            float sx = size.x / (resolution - 1), sz = size.z / (resolution - 1);
            // One height upload, rather than hundreds of Terrain rebuild requests.
            float[,] heights = data.GetHeights(0,0,resolution,resolution);
            bool render = SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;
            if (render) impactPixels = new Color[ImpactResolution*ImpactResolution];
            int large = Mathf.Clamp(settings.landformCraters,0,40);
            int small = Mathf.Clamp(settings.shallowCraters,0,1600);
            for (int i = 0; i < large+small; i++)
            {
                bool landmark = i<large;
                var crater = new Crater
                {
                    center = new Vector2(Range(random,48,size.x-48),Range(random,48,size.z-48)),
                    radius = landmark ? Range(random,9,22) : Mathf.Lerp(.8f,5,Mathf.Pow((float)random.NextDouble(),1.8f)),
                    freshness = (float)random.NextDouble()
                };
                if (!landmark) crater.center = new Vector2(Range(random,10,size.x-10),Range(random,10,size.z-10));
                // Two readable foreground/midground landmarks bracket the entry
                // corridor. The spawn plateau and old rock contact patches remain intact.
                if (i<2 && landmark)
                {
                    crater.center = new Vector2(protectedSpawn.x-origin.x+(i==0?-38:46),
                        protectedSpawn.z-origin.z+(i==0?54:93));
                    crater.radius = i==0 ? 13 : 19;
                    crater.freshness = i==0 ? .85f : .55f;
                }
                if (source.GetInterpolatedNormal(crater.center.x/size.x,crater.center.y/size.z).y < .72f) continue;
                craters.Add(crater);
                float extent = crater.radius * 1.8f;
                int x0 = Mathf.Max(0,Mathf.FloorToInt((crater.center.x-extent)/sx));
                int z0 = Mathf.Max(0,Mathf.FloorToInt((crater.center.y-extent)/sz));
                int x1 = Mathf.Min(resolution-1,Mathf.CeilToInt((crater.center.x+extent)/sx));
                int z1 = Mathf.Min(resolution-1,Mathf.CeilToInt((crater.center.y+extent)/sz));
                for (int z=z0;z<=z1;z++)
                    for (int x=x0;x<=x1;x++)
                    {
                        Vector2 local = new Vector2(x*sx,z*sz);
                        Vector2 delta = local-crater.center;
                        float angle = Mathf.Atan2(delta.y,delta.x);
                        float asymmetry=1+.07f*Mathf.Sin(angle*3+crater.center.x)+.035f*Mathf.Sin(angle*7);
                        float r = delta.magnitude/(crater.radius*asymmetry);
                        float bowl = -Mathf.Pow(Mathf.Max(0,1-r*r),2);
                        float width = Mathf.Lerp(.3f,.14f,crater.freshness);
                        float rim = Mathf.Exp(-Mathf.Pow((r-1)/width,2)) * Mathf.Lerp(.24f,.48f,crater.freshness);
                        float fade = 1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(1.4f,1.8f,r));
                        Vector3 world = origin+new Vector3(local.x,0,local.y);
                        float protection = Mathf.SmoothStep(0,1,Mathf.InverseLerp(23,31,
                            Vector2.Distance(local,new Vector2(protectedSpawn.x-origin.x,protectedSpawn.z-origin.z))));
                        protection *= RockClearance(world,landmark ? 2.5f : .8f);
                        heights[z,x] = Mathf.Clamp01(heights[z,x] + (bowl+rim)*fade*crater.radius
                            * Mathf.Clamp(settings.craterDepthRatio,.02f,.25f)*protection/size.y);
                    }
                if (render) StampImpact(crater);
            }
            data.SetHeightsDelayLOD(0,0,heights);
            data.SyncHeightmap();
            if (render)
            {
                impactGeology = new Texture2D(ImpactResolution,ImpactResolution,TextureFormat.RGBA32,true,true)
                    {name="Moon impact facies (runtime)",wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Trilinear};
                impactGeology.SetPixels(impactPixels);impactGeology.Apply(true,true);
                // Retain the CPU mask for O(1) rubble placement; it replaces scanning every crater per fragment.
            }
        }

        private void StampImpact(Crater crater)
        {
            float extent=crater.radius*3.5f;
            int x0=Mathf.Max(0,Mathf.FloorToInt((crater.center.x-extent)/size.x*ImpactResolution));
            int x1=Mathf.Min(ImpactResolution-1,Mathf.CeilToInt((crater.center.x+extent)/size.x*ImpactResolution));
            int z0=Mathf.Max(0,Mathf.FloorToInt((crater.center.y-extent)/size.z*ImpactResolution));
            int z1=Mathf.Min(ImpactResolution-1,Mathf.CeilToInt((crater.center.y+extent)/size.z*ImpactResolution));
            for (int z=z0;z<=z1;z++)
                for (int x=x0;x<=x1;x++)
                {
                    Vector2 delta=new Vector2((x+.5f)/ImpactResolution*size.x,(z+.5f)/ImpactResolution*size.z)-crater.center;
                    float angle=Mathf.Atan2(delta.y,delta.x),r=delta.magnitude/crater.radius;
                    float rays=Mathf.Pow(.5f+.5f*Mathf.Sin(angle*11+Mathf.Sin(angle*5)*1.8f),5);
                    float ring=Mathf.Exp(-Mathf.Pow((r-1.06f)/.28f,2));
                    float ejecta=r>1 ? Mathf.Exp(-(r-1)*1.1f)*rays*crater.freshness : 0;
                    float rubble=Mathf.Exp(-Mathf.Pow((r-1.4f)/.55f,2))*(.45f+.55f*crater.freshness);
                    float basin=(1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.25f,.9f,r)))*.65f;
                    int index=z*ImpactResolution+x;
                    Color old=impactPixels[index];
                    impactPixels[index]=new Color(Mathf.Max(old.r,Mathf.Clamp01(ring*.7f+ejecta)),
                        Mathf.Max(old.g,rubble),Mathf.Max(old.b,basin),1);
                }
        }

        private Color SampleImpact(float x,float z)
        {
            if (impactPixels != null)
                return impactPixels[Mathf.Clamp((int)(z/size.z*ImpactResolution),0,ImpactResolution-1)*ImpactResolution
                    +Mathf.Clamp((int)(x/size.x*ImpactResolution),0,ImpactResolution-1)];
            // Server geometry uses the same analytic field without allocating a GPU texture.
            float rubble=0;
            foreach (Crater crater in craters)
            {
                float r=Vector2.Distance(new Vector2(x,z),crater.center)/crater.radius;
                rubble=Mathf.Max(rubble,Mathf.Exp(-Mathf.Pow((r-1.4f)/.55f,2))*(.45f+.55f*crater.freshness));
            }
            return new Color(0,rubble,0,1);
        }

        private Dictionary<Vector2Int,List<Bounds>> rockCells;
        private bool BlockedByRock(Vector3 position,float radius)
        {
            if (rockCells == null)
            {
                // Collider local bounds are identical on server and client and do
                // not depend on renderer culling or physics synchronization.
                MeshCollider[] rocks = parent.GetComponentsInChildren<MeshCollider>(true);
                rockCells = new Dictionary<Vector2Int,List<Bounds>>();
                foreach (MeshCollider rock in rocks)
                {
                    if (!rock.sharedMesh) continue;
                    Bounds b = rock.sharedMesh.bounds;
                    Matrix4x4 matrix = rock.transform.localToWorldMatrix;
                    Vector3 e = b.extents;
                    Vector3 a = matrix.MultiplyVector(new Vector3(e.x,0,0));
                    Vector3 c = matrix.MultiplyVector(new Vector3(0,e.y,0));
                    Vector3 d = matrix.MultiplyVector(new Vector3(0,0,e.z));
                    b = new Bounds(matrix.MultiplyPoint3x4(b.center),2*new Vector3(
                        Mathf.Abs(a.x)+Mathf.Abs(c.x)+Mathf.Abs(d.x),
                        Mathf.Abs(a.y)+Mathf.Abs(c.y)+Mathf.Abs(d.y),
                        Mathf.Abs(a.z)+Mathf.Abs(c.z)+Mathf.Abs(d.z)));
                    Vector2Int first=RockCell(b.min),last=RockCell(b.max);
                    for (int z=first.y;z<=last.y;z++)
                        for (int x=first.x;x<=last.x;x++)
                        {
                            var key=new Vector2Int(x,z);
                            if (!rockCells.TryGetValue(key,out var cell))
                            {cell=new List<Bounds>();rockCells.Add(key,cell);}
                            cell.Add(b);
                        }
                }
            }
            Vector2Int from=RockCell(position-Vector3.one*radius),to=RockCell(position+Vector3.one*radius);
            for (int z=from.y;z<=to.y;z++)
                for (int x=from.x;x<=to.x;x++)
                    if (rockCells.TryGetValue(new Vector2Int(x,z),out var cell))
                        foreach (Bounds b in cell)
                            if (position.x+radius>b.min.x && position.x-radius<b.max.x
                                && position.z+radius>b.min.z && position.z-radius<b.max.z) return true;
            return false;
        }

        private static Vector2Int RockCell(Vector3 position) =>
            new(Mathf.FloorToInt(position.x/TileSize),Mathf.FloorToInt(position.z/TileSize));

        private float RockClearance(Vector3 position,float margin)
        {
            if (rockCells==null) BlockedByRock(position,0);
            float closest=margin;
            Vector2Int first=RockCell(position-Vector3.one*margin),last=RockCell(position+Vector3.one*margin);
            for (int z=first.y;z<=last.y;z++)
                for (int x=first.x;x<=last.x;x++)
                    if (rockCells.TryGetValue(new Vector2Int(x,z),out var cell))
                        foreach (Bounds b in cell)
                        {
                            float dx=Mathf.Max(Mathf.Max(b.min.x-position.x,0),position.x-b.max.x);
                            float dz=Mathf.Max(Mathf.Max(b.min.z-position.z,0),position.z-b.max.z);
                            closest=Mathf.Min(closest,Mathf.Sqrt(dx*dx+dz*dz));
                        }
            return Mathf.SmoothStep(0,1,closest/margin);
        }

        private struct Outcrop { public Vector3 position,normal; public float diameter,freshness; public int seed; }
        private void CreateOutcrops()
        {
            var random=new System.Random(settings.seed^89173);
            var groups=new Dictionary<Vector2Int,List<Outcrop>>();
            var landmarks=craters.FindAll(c=>c.radius>8);
            if (landmarks.Count==0 || settings.outcropRocks<=0) return;
            int target=Mathf.Clamp(settings.outcropRocks,0,1200),accepted=0;
            var occupied=new Dictionary<Vector2Int,List<Outcrop>>();
            for (int attempt=0;attempt<target*5 && accepted<target;attempt++)
            {
                Crater crater=landmarks[random.Next(landmarks.Count)];
                float angle=Range(random,0,Mathf.PI*2),radius=crater.radius*Range(random,1.05f,2.3f);
                Vector2 p=crater.center+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*radius;
                if (p.x<3 || p.y<3 || p.x>size.x-3 || p.y>size.z-3) continue;
                Vector3 position=origin+new Vector3(p.x,data.GetInterpolatedHeight(p.x/size.x,p.y/size.z),p.y);
                if (Vector2.Distance(p,new Vector2(protectedSpawn.x-origin.x,protectedSpawn.z-origin.z))<28) continue;
                float diameter=Mathf.Lerp(.35f,2.6f,Mathf.Pow((float)random.NextDouble(),2));
                Vector3 normal=data.GetInterpolatedNormal(p.x/size.x,p.y/size.z);
                if (normal.y<.78f || BlockedByRock(position,diameter*.55f)) continue;
                Vector2Int occupiedCell=RockCell(position);
                bool overlap=false;
                for (int z=-1;z<=1;z++)
                    for (int x=-1;x<=1;x++)
                        if (occupied.TryGetValue(occupiedCell+new Vector2Int(x,z),out var neighbours))
                            foreach (Outcrop other in neighbours)
                                if ((position-other.position).sqrMagnitude<Mathf.Pow((diameter+other.diameter)*.55f,2)) overlap=true;
                if (overlap) continue;
                var rock=new Outcrop {position=position-normal*diameter*.12f,normal=normal,
                    diameter=diameter,freshness=crater.freshness,seed=random.Next()};
                if (!occupied.TryGetValue(occupiedCell,out var cluster))
                {cluster=new List<Outcrop>();occupied.Add(occupiedCell,cluster);}
                cluster.Add(rock);
                var key=new Vector2Int(Mathf.FloorToInt(p.x/32),Mathf.FloorToInt(p.y/32));
                if (!groups.TryGetValue(key,out var group)) {group=new List<Outcrop>();groups.Add(key,group);}
                group.Add(rock);accepted++;
            }
            bool render=SystemInfo.graphicsDeviceType!=GraphicsDeviceType.Null;
            foreach (var pair in groups)
            {
                Vector3 chunkOrigin=origin+new Vector3(pair.Key.x*32,0,pair.Key.y*32);
                var vertices=new List<Vector3>();var uv=new List<Vector2>();
                var colors=new List<Color>();var triangles=new List<int>();
                foreach (Outcrop rock in pair.Value)
                    rockShapes.Append(new System.Random(rock.seed),rock.position-chunkOrigin,rock.normal,
                        rock.diameter,rock.freshness,vertices,uv,colors,triangles);
                var mesh=new Mesh {name=$"Moon ejecta outcrop {pair.Key}",indexFormat=IndexFormat.UInt32};
                mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.SetColors(colors);mesh.SetTriangles(triangles,0);
                mesh.RecalculateNormals();mesh.RecalculateTangents();mesh.RecalculateBounds();
                var chunk=new GameObject(mesh.name) {hideFlags=HideFlags.DontSave};
                chunk.transform.SetParent(parent,false);chunk.transform.position=chunkOrigin;
                chunk.AddComponent<MeshFilter>().sharedMesh=mesh;
                // Larger silhouettes are actual cover on every peer. They are
                // never camera-streamed and never disappear from the server's collider world.
                chunk.AddComponent<MeshCollider>().sharedMesh=mesh;
                if (render)
                {
                    var renderer=chunk.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;
                    var block=new MaterialPropertyBlock();block.SetFloat("_ProceduralRock",1);
                    renderer.SetPropertyBlock(block);
                }
                mesh.UploadMeshData(true);outcrops.Add(chunk);
            }
        }

        public void BindGeometry(IEnumerable<Material> materials)
        {
            // A local height/normal field supplies bounce-light positions and
            // terrain visibility. It is created once after the collider changes.
            const int resolution = 513;
            int hr = data.heightmapResolution;
            float[,] heights = data.GetHeights(0,0,hr,hr);
            Color[] pixels = new Color[resolution*resolution];
            for (int z=0;z<resolution;z++)
                for (int x=0;x<resolution;x++)
                {
                    int hx = Mathf.RoundToInt(x*(hr-1f)/(resolution-1));
                    int hz = Mathf.RoundToInt(z*(hr-1f)/(resolution-1));
                    int left=Mathf.Max(0,hx-1),right=Mathf.Min(hr-1,hx+1);
                    int down=Mathf.Max(0,hz-1),up=Mathf.Min(hr-1,hz+1);
                    float dx=(heights[hz,right]-heights[hz,left])*size.y/((right-left)*size.x/(hr-1));
                    float dz=(heights[up,hx]-heights[down,hx])*size.y/((up-down)*size.z/(hr-1));
                    Vector3 n=new Vector3(-dx,1,-dz).normalized;
                    pixels[z*resolution+x]=new Color(origin.y+heights[hz,hx]*size.y,n.x,n.y,n.z);
                }
            geometry=new Texture2D(resolution,resolution,TextureFormat.RGBAFloat,false,true)
                { name="Moon local bounce geometry (runtime)",wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Bilinear };
            geometry.SetPixels(pixels);geometry.Apply(false,true);
            foreach (Material target in materials)
            {
                target.SetTexture("_LocalTerrainGeometry",geometry);
                target.SetVector("_LocalTerrainRect",new Vector4(origin.x,origin.z,size.x,size.z));
                target.SetFloat("_LocalTerrainReady",1);
                target.SetTexture("_ImpactGeology",impactGeology);
                target.SetFloat("_ImpactReady",1);
            }
        }

        public void Tick()
        {
            if (!material || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return;
            if (Time.unscaledTime>=nextCamera) { camera=Camera.main;nextCamera=Time.unscaledTime+1; }
            if (!camera || camera.cameraType!=CameraType.Game) return;
            Vector3 local=camera.transform.position-origin;
            Vector2Int center=new(Mathf.FloorToInt(local.x/TileSize),Mathf.FloorToInt(local.z/TileSize));
            int radius=Mathf.Clamp(settings.tileRadius,1,6);
            expired.Clear();
            foreach (var pair in tiles)
                if (Mathf.Abs(pair.Key.x-center.x)>radius || Mathf.Abs(pair.Key.y-center.y)>radius) expired.Add(pair.Key);
            foreach (Vector2Int key in expired) { Release(tiles[key]);tiles.Remove(key); }
            // Two tiles per frame, closest first; no complete rebuild on crossing a tile boundary.
            int built=0;
            for (int ring=0;ring<=radius;ring++)
                for (int z=-ring;z<=ring;z++)
                    for (int x=-ring;x<=ring;x++)
                    {
                        if (Mathf.Max(Mathf.Abs(x),Mathf.Abs(z))!=ring) continue;
                        Vector2Int key=center+new Vector2Int(x,z);
                        if (tiles.ContainsKey(key) || key.x<0 || key.y<0 || (key.x+1)*TileSize>size.x || (key.y+1)*TileSize>size.z) continue;
                        tiles.Add(key,CreateTile(key));
                        if (++built>=2) return;
                    }
        }

        private GameObject CreateTile(Vector2Int key)
        {
            int seed=unchecked(settings.seed ^ key.x*73856093 ^ key.y*19349663);
            var random=new System.Random(seed);
            var vertices=new List<Vector3>();var uv=new List<Vector2>();var triangles=new List<int>();
            var colors=new List<Color>();
            Vector3 tileOrigin=origin+new Vector3(key.x*TileSize,0,key.y*TileSize);
            for (int i=0;i<Mathf.Clamp(settings.fragmentsPerTile,0,700);i++)
            {
                float x=key.x*TileSize+Range(random,0,TileSize),z=key.y*TileSize+Range(random,0,TileSize);
                Color geology=SampleImpact(x,z);
                float freshness=geology.r, rim=geology.g;
                Vector3 normal=data.GetInterpolatedNormal(x/size.x,z/size.z);
                float cluster=Mathf.PerlinNoise(x*.12f+17,z*.12f+39);
                float density=Mathf.Lerp(.24f,.95f,Mathf.Clamp01(rim+cluster*.55f))*Mathf.InverseLerp(.5f,.94f,normal.y);
                if (random.NextDouble()>density) continue;
                // Most fragments are tiny. Fresh ejecta carries a sparse larger tail.
                float diameter=Mathf.Lerp(.045f,Mathf.Clamp(settings.largestFragment,.02f,.24f),
                    Mathf.Pow((float)random.NextDouble(),1.6f))*Mathf.Lerp(.85f,1,freshness);
                Vector3 world=origin+new Vector3(x,data.GetInterpolatedHeight(x/size.x,z/size.z),z);
                if (BlockedByRock(world,diameter)) continue;
                Vector3 position=world-tileOrigin-normal*diameter*.12f;
                rockShapes.Append(random,position,normal,diameter,freshness,vertices,uv,colors,triangles);
            }
            var mesh=new Mesh {name=$"Moon fragments {key.x},{key.y}",indexFormat=IndexFormat.UInt32};
            mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.SetColors(colors);mesh.SetTriangles(triangles,0);
            if (vertices.Count>0) {mesh.RecalculateNormals();mesh.RecalculateTangents();mesh.RecalculateBounds();}
            mesh.UploadMeshData(true);
            var tile=new GameObject(mesh.name) {hideFlags=HideFlags.DontSave};
            tile.transform.SetParent(parent,false);tile.transform.position=tileOrigin;
            tile.AddComponent<MeshFilter>().sharedMesh=mesh;
            var renderer=tile.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;
            renderer.shadowCastingMode=ShadowCastingMode.On;renderer.receiveShadows=true;
            var properties=new MaterialPropertyBlock();
            properties.SetFloat("_FragmentMode",1);
            properties.SetFloat("_ProceduralRock",1);
            properties.SetVector("_GroundPlane",new Vector4(0,1,0,100000));
            properties.SetFloat("_RockVariation",Range(random,.8f,1.05f));
            renderer.SetPropertyBlock(properties);
            return tile;
        }

        private static float Range(System.Random random,float min,float max) => min+(max-min)*(float)random.NextDouble();
        private static void Release(GameObject tile)
        {
            if (!tile) return;
            Object.Destroy(tile.GetComponent<MeshFilter>().sharedMesh);Object.Destroy(tile);
        }
        public void Dispose()
        {
            foreach (GameObject tile in tiles.Values) Release(tile);
            tiles.Clear();
            foreach (GameObject chunk in outcrops) Release(chunk);
            outcrops.Clear();
            if (terrain) terrain.terrainData=source;
            if (collider) collider.terrainData=source;
            if (geometry) Object.Destroy(geometry);
            if (impactGeology) Object.Destroy(impactGeology);
            Object.Destroy(data);
        }
    }
}
