using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Unity.MP_FPS.Moon.Editor
{
    /// <summary>
    /// Bounded render clusters for author-confirmed fixed architecture, plus exact
    /// box collision substitution. No distance toggles, mesh decimation, room guesses
    /// or changes to the source FBX. The pre-change prefab is backed up once.
    /// </summary>
    public static class MC00CpuOptimizationTools
    {
        static string Generated => MC00GameExportTools.CurrentFolder + "/Meshes/CpuClusters";
        [Serializable] class AuthorMap { public Owner[] objects; }
        [Serializable] class Owner { public string output_object, owner_path; }
        [Serializable] public class Report
        {
            public int renderersBefore, renderersAfter, castersBefore, castersAfter;
            public int sourcesClustered, clusters, boxesConverted, meshCollidersBefore, meshCollidersAfter;
            public int trianglesBefore, trianglesAfter;
            public string backup;
        }

        [MenuItem("Tools/Moon Environment/MC00 Game Export/3 - Optimize candidate CPU submission")]
        public static void Optimize()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play before authoring.");
            var root = PrefabUtility.LoadPrefabContents(MC00GameExportTools.CurrentPrefabPath);
            var readable = new Dictionary<Mesh, Mesh>();
            bool editing = false;
            try
            {
                if (root.GetComponent<BuildingRenderClusters>()) throw new InvalidOperationException("CPU clusters already exist; restore the backup before rebuilding.");
                var manifest = JsonUtility.FromJson<MC00GameExportTools.Manifest>(File.ReadAllText(MC00GameExportTools.CurrentSource + "/unity-manifest.json"));
                var interactive = new HashSet<string>(StringComparer.Ordinal);
                string switchMapPath = MC00GameExportTools.CurrentSource + "/AuthorNotes/MC00_LIGHT_SWITCH_MAP_V2.json";
                if (File.Exists(switchMapPath))
                {
                    var map = JsonUtility.FromJson<MC00LightSwitchTools.Mapping>(File.ReadAllText(switchMapPath));
                    foreach (var button in map.switches ?? Array.Empty<MC00LightSwitchTools.Switch>())
                    {
                        if (!string.IsNullOrEmpty(button.button_object)) interactive.Add(button.button_object);
                        if (!string.IsNullOrEmpty(button.indicator_object)) interactive.Add(button.indicator_object);
                    }
                    foreach (var lamp in map.lights ?? Array.Empty<MC00LightSwitchTools.Lamp>())
                        foreach (var surface in lamp.emissive_targets ?? Array.Empty<MC00LightSwitchTools.Emission>())
                            if (!string.IsNullOrEmpty(surface.renderer_object)) interactive.Add(surface.renderer_object);
                }
                var owners = JsonUtility.FromJson<AuthorMap>(File.ReadAllText(MC00GameExportTools.CurrentSource + "/AuthorNotes/MC00_SOURCE_TO_OUTPUT.json"))
                    .objects.Where(o=>!string.IsNullOrEmpty(o.output_object)).GroupBy(o => o.output_object)
                    .ToDictionary(g => g.Key, g => g.Select(o => o.owner_path).Distinct().Single());
                var render = root.GetComponentsInChildren<Transform>(true).Single(t => t.name == "MC00_Render");
                var sources = render.GetComponentsInChildren<MeshRenderer>(true);
                var filters = sources.ToDictionary(r => r.name, r => r.GetComponent<MeshFilter>());
                var report = new Report {
                    renderersBefore=sources.Count(r=>r.enabled), castersBefore=sources.Count(Casts),
                    trianglesBefore=sources.Where(r=>r.enabled).Sum(r=>Triangles(r.GetComponent<MeshFilter>().sharedMesh)),
                    meshCollidersBefore=root.GetComponentsInChildren<MeshCollider>(true).Length,
                    backup="Library/BuildValidation/BeforeMC00Cpu-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")
                };
                Directory.CreateDirectory(report.backup);
                File.Copy(MC00GameExportTools.CurrentPrefabPath, report.backup+"/MC00GameExport.prefab");
                Directory.CreateDirectory(Generated);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                var buckets = new SortedDictionary<string, List<MeshRenderer>>(StringComparer.Ordinal);
                foreach (var g in manifest.geometry)
                {
                    // The collision contract explicitly identifies these as fixed
                    // structure. Unclassified objects and all interactive props stay independent.
                    if (!g.coveredByProxy || !owners.TryGetValue(g.name, out var owner)) continue;
                    var f=filters[g.name]; var r=f.GetComponent<MeshRenderer>();
                    // Author classifications may change between deliveries. Explicit
                    // interactive names and their children must never join fixed clusters.
                    if (f.GetComponentsInParent<Transform>(true).Any(t => interactive.Contains(t.name))) continue;
                    if (!r.enabled || !r.gameObject.activeInHierarchy || f.sharedMesh.subMeshCount!=1 || r.sharedMaterials.Length!=1 ||
                        !r.sharedMaterial || r.sharedMaterial.renderQueue>=2500 || r.GetComponentInParent<LODGroup>() ||
                        f.transform.localToWorldMatrix.determinant<=0 || r.lightmapIndex>=0 && r.lightmapIndex<65534) continue;
                    var size=r.bounds.size;
                    if (Mathf.Max(size.x,Mathf.Max(size.y,size.z))>8) continue;
                    var center=root.transform.InverseTransformPoint(r.bounds.center);
                    string key=owner+"|"+AssetDatabase.GetAssetPath(r.sharedMaterial)+"|"+r.shadowCastingMode+"|"+r.receiveShadows+"|"+
                        r.gameObject.layer+"|"+r.renderingLayerMask+"|"+r.lightProbeUsage+"|"+r.reflectionProbeUsage+"|"+
                        string.Join(",",f.sharedMesh.GetVertexAttributes().Select(a=>a.ToString()))+"|"+
                        Mathf.FloorToInt(center.x/4)+","+Mathf.FloorToInt(center.y/4)+","+Mathf.FloorToInt(center.z/4);
                    if (!buckets.TryGetValue(key,out var list)) buckets[key]=list=new List<MeshRenderer>();
                    list.Add(r);
                }
                var clusterRoot=new GameObject("Fixed Architecture Render Clusters"); clusterRoot.transform.SetParent(root.transform,false);
                var groups=new List<BuildingRenderClusters.Group>();
                AssetDatabase.StartAssetEditing(); editing=true;
                foreach (var bucket in buckets.Values)
                {
                    var chunk=new List<MeshRenderer>(); Bounds bounds=default; int vertices=0;
                    foreach (var r in bucket.OrderBy(r=>r.name,StringComparer.Ordinal))
                    {
                        var next=chunk.Count==0?r.bounds:bounds; next.Encapsulate(r.bounds);
                        int v=r.GetComponent<MeshFilter>().sharedMesh.vertexCount;
                        if (chunk.Count>0 && (vertices+v>16000 || chunk.Count>=64 || Max(next.size)>8))
                        { Flush(chunk,clusterRoot.transform,groups,readable); chunk.Clear(); vertices=0; next=r.bounds; }
                        chunk.Add(r); vertices+=v; bounds=next;
                    }
                    Flush(chunk,clusterRoot.transform,groups,readable);
                }
                AssetDatabase.StopAssetEditing(); editing=false;
                var controller=root.AddComponent<BuildingRenderClusters>(); controller.Configure(groups.ToArray());
                var boxCache=new Dictionary<Mesh,bool>();
                foreach (var collider in render.GetComponentsInChildren<MeshCollider>(true))
                {
                    if (!collider.enabled || collider.convex || collider.isTrigger || !collider.sharedMesh || collider.GetComponentInParent<Rigidbody>()) continue;
                    if (!Orthogonal(collider.transform.localToWorldMatrix)) continue;
                    if (!boxCache.TryGetValue(collider.sharedMesh,out bool isBox))
                    {
                        // Most complex shapes are rejected without copying their data.
                        isBox=collider.sharedMesh.vertexCount<=48 && Triangles(collider.sharedMesh)==12 && IsExactBox(Read(collider.sharedMesh,readable));
                        boxCache[collider.sharedMesh]=isBox;
                    }
                    if (!isBox) continue;
                    var box=collider.gameObject.AddComponent<BoxCollider>();
                    box.center=collider.sharedMesh.bounds.center; box.size=collider.sharedMesh.bounds.size;
                    box.sharedMaterial=collider.sharedMaterial; box.contactOffset=collider.contactOffset;
                    box.includeLayers=collider.includeLayers; box.excludeLayers=collider.excludeLayers;
                    box.layerOverridePriority=collider.layerOverridePriority;
                    UnityEngine.Object.DestroyImmediate(collider); report.boxesConverted++;
                }
                report.clusters=groups.Count; report.sourcesClustered=groups.Sum(g=>g.sources.Length);
                var enabled=root.GetComponentsInChildren<MeshRenderer>(true).Where(r=>r.enabled).ToArray();
                report.renderersAfter=enabled.Length; report.castersAfter=enabled.Count(Casts);
                report.trianglesAfter=enabled.Sum(r=>Triangles(r.GetComponent<MeshFilter>().sharedMesh));
                report.meshCollidersAfter=root.GetComponentsInChildren<MeshCollider>(true).Length;
                if (report.trianglesAfter!=report.trianglesBefore) throw new InvalidOperationException("Render geometry changed during clustering.");
                PrefabUtility.SaveAsPrefabAsset(root,MC00GameExportTools.CurrentPrefabPath);
                AssetDatabase.SaveAssets();
                File.WriteAllText(MC00GameExportTools.CurrentSource+"/CpuOptimizationReport.json",JsonUtility.ToJson(report,true));
                Debug.Log("MC00 CPU authoring: "+JsonUtility.ToJson(report));
            }
            finally
            {
                if(editing) AssetDatabase.StopAssetEditing();
                foreach(var mesh in readable.Values) UnityEngine.Object.DestroyImmediate(mesh);
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        static void Flush(List<MeshRenderer> sources, Transform parent, List<BuildingRenderClusters.Group> groups, Dictionary<Mesh,Mesh> readable)
        {
            if(sources.Count<3) return;
            var go=new GameObject("Architecture Cluster "+groups.Count.ToString("D4")); go.transform.SetParent(parent,false);
            var first=sources[0]; go.layer=first.gameObject.layer;
            var combines=sources.Select(r=>new CombineInstance {
                mesh=Read(r.GetComponent<MeshFilter>().sharedMesh,readable), subMeshIndex=0,
                transform=go.transform.worldToLocalMatrix*r.transform.localToWorldMatrix }).ToArray();
            var mesh=new Mesh {name=go.name,indexFormat=IndexFormat.UInt32};
            mesh.CombineMeshes(combines,true,true,false); mesh.RecalculateBounds();
            if(Triangles(mesh)!=sources.Sum(r=>Triangles(r.GetComponent<MeshFilter>().sharedMesh))) throw new InvalidOperationException("Cluster lost triangles.");
            AssetDatabase.CreateAsset(mesh,Generated+"/"+go.name.Replace(' ','_')+".asset");
            go.AddComponent<MeshFilter>().sharedMesh=mesh;
            var renderer=go.AddComponent<MeshRenderer>(); renderer.sharedMaterial=first.sharedMaterial;
            renderer.shadowCastingMode=first.shadowCastingMode; renderer.receiveShadows=first.receiveShadows;
            renderer.renderingLayerMask=first.renderingLayerMask; renderer.lightProbeUsage=first.lightProbeUsage;
            renderer.reflectionProbeUsage=first.reflectionProbeUsage;
            groups.Add(new BuildingRenderClusters.Group {combined=renderer,sources=sources.ToArray()});
        }

        static Mesh Read(Mesh source,Dictionary<Mesh,Mesh> cache)
        {
            if(cache.TryGetValue(source,out var result)) return result;
            result=new Mesh {name=source.name+" CPU authoring copy"};
            using(var all=MeshUtility.AcquireReadOnlyMeshData(source))
            {
                var data=all[0]; result.SetVertexBufferParams(data.vertexCount,source.GetVertexAttributes());
                for(int stream=0;stream<data.vertexBufferCount;stream++)
                {var bytes=data.GetVertexData<byte>(stream);result.SetVertexBufferData(bytes,0,0,bytes.Length,stream);}
                if(source.indexFormat==IndexFormat.UInt16)
                {var indices=data.GetIndexData<ushort>();result.SetIndexBufferParams(indices.Length,IndexFormat.UInt16);result.SetIndexBufferData(indices,0,0,indices.Length);}
                else
                {var indices=data.GetIndexData<uint>();result.SetIndexBufferParams(indices.Length,IndexFormat.UInt32);result.SetIndexBufferData(indices,0,0,indices.Length);}
                result.subMeshCount=data.subMeshCount;
                for(int i=0;i<data.subMeshCount;i++) result.SetSubMesh(i,data.GetSubMesh(i),MeshUpdateFlags.DontRecalculateBounds);
                result.bounds=source.bounds;
            }
            cache[source]=result; return result;
        }

        static bool IsExactBox(Mesh mesh)
        {
            var v=mesh.vertices; var t=mesh.triangles; var b=mesh.bounds;
            if(b.size.x<.0001f||b.size.y<.0001f||b.size.z<.0001f) return false;
            var corners=new HashSet<int>(); var cornerIds=new int[v.Length];
            for(int vertex=0;vertex<v.Length;vertex++)
            {
                var p=v[vertex];
                int bits=0;
                for(int a=0;a<3;a++)
                {
                    float epsilon=Mathf.Max(1e-7f,b.size[a]*1e-5f);
                    if(Mathf.Abs(p[a]-b.min[a])<=epsilon) {}
                    else if(Mathf.Abs(p[a]-b.max[a])<=epsilon) bits|=1<<a;
                    else return false;
                }
                corners.Add(bits); cornerIds[vertex]=bits;
            }
            if(corners.Count!=8) return false;
            var area=new double[6]; var edges=new Dictionary<int,int>();
            for(int i=0;i<t.Length;i+=3)
            {
                var a=v[t[i]];var c=v[t[i+1]];var d=v[t[i+2]];
                for(int side=0;side<3;side++)
                {
                    int p=cornerIds[t[i+side]],q=cornerIds[t[i+(side+1)%3]];
                    if(p==q)return false;
                    int edge=Math.Min(p,q)*8+Math.Max(p,q);
                    edges.TryGetValue(edge,out int count);edges[edge]=count+1;
                }
                float triangle=Vector3.Cross(c-a,d-a).magnitude*.5f; if(triangle<=0) return false;
                int face=-1;
                for(int axis=0;axis<3;axis++)
                {
                    float epsilon=Mathf.Max(1e-7f,b.size[axis]*1e-5f);
                    if(Mathf.Abs(a[axis]-c[axis])>epsilon||Mathf.Abs(a[axis]-d[axis])>epsilon) continue;
                    face=axis*2+(Mathf.Abs(a[axis]-b.max[axis])<=epsilon?1:0);break;
                }
                if(face<0) return false; area[face]+=triangle;
            }
            for(int a=0;a<3;a++)
            {double expected=b.size[(a+1)%3]*b.size[(a+2)%3];if(Math.Abs(area[a*2]-expected)>expected*1e-4||Math.Abs(area[a*2+1]-expected)>expected*1e-4)return false;}
            return edges.Values.All(count=>count==2);
        }
        static bool Orthogonal(Matrix4x4 m)
        {Vector3 x=m.GetColumn(0),y=m.GetColumn(1),z=m.GetColumn(2);return Mathf.Abs(Vector3.Dot(x.normalized,y.normalized))<1e-5f&&Mathf.Abs(Vector3.Dot(x.normalized,z.normalized))<1e-5f&&Mathf.Abs(Vector3.Dot(y.normalized,z.normalized))<1e-5f;}
        static int Triangles(Mesh m) {int n=0;for(int i=0;i<m.subMeshCount;i++)n+=(int)m.GetIndexCount(i)/3;return n;}
        static bool Casts(MeshRenderer r)=>r.enabled&&r.shadowCastingMode!=ShadowCastingMode.Off;
        static float Max(Vector3 v)=>Mathf.Max(v.x,Mathf.Max(v.y,v.z));
    }
}
