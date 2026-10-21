using System.Collections.Generic;
using Unity.MP_FPS.SpiderLab;
using Unity.MP_FPS.Moon;
using UnityEngine;
using UnityEngine.Rendering;

namespace Unity.MP_FPS
{
    // Rendering only: real collision and the authority stay on the existing map/server.
    internal sealed class BountyClashMapPresentation
    {
        private readonly GameObject m_Root;
        private readonly List<Material> m_Materials=new List<Material>();
        private readonly List<Mesh> m_Meshes=new List<Mesh>();
        private readonly List<Transform> m_Labels=new List<Transform>();
        private readonly List<GameObject> m_TargetSignals=new List<GameObject>();
        private readonly BranchCreatureRig m_Corpse;
        private readonly Material m_ObjectiveMaterial;
        private readonly GameObject m_BountyCore,m_BountySignal;
        private readonly Vector3 m_TargetPosition;
        private Vector3 m_BountyPosition;
        private readonly Texture2D m_Tissue;
        private static readonly int s_Mask=LayerMask.GetMask("Default","Ground");

        public BountyClashMapPresentation(Transform owner,ClashArenaDefinition arena)
        {
            m_Root=new GameObject("Bounty Clash / MC00 compound");m_Root.transform.SetParent(owner,false);
            var red=Material("Boundary warning lamps",new Color(.88f,.22f,.13f));
            var green=Material("Extraction lamps",new Color(.3f,.82f,.53f));
            var housing=Material("Field beacon housing",new Color(.12f,.15f,.16f),true);
            m_ObjectiveMaterial=Material("Bounty signal",new Color(.95f,.62f,.21f));
            var center=BountyClashArenaSettings.Position(arena.Center);
            for(int i=0;i<24;i++)
            {
                float angle=i*Mathf.PI*2/24;
                var p=Surface(center+new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*arena.Radius,150,300);
                Beacon("Boundary warning post",p,red,housing,.85f);
                var side=new Vector3(-Mathf.Sin(angle),0,Mathf.Cos(angle));
                Line("Boundary ground dash",new[]{p-side*.7f+Vector3.up*.07f,p+side*.7f+Vector3.up*.07f},red,.035f);
            }
            for(int i=0;i<arena.Exits.Length;i++)
            {
                var exit=BountyClashArenaSettings.Position(arena.Exits[i]);
                var zone=new GameObject(i==0?"Extraction A / south":"Extraction B / north");zone.transform.SetParent(m_Root.transform,false);
                for(int mark=0;mark<12;mark++)
                {
                    float angle=mark*Mathf.PI*2/12;var radial=new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle));
                    var a=Surface(exit+radial*(arena.ExitRadius-.5f),3,7)+Vector3.up*.07f;
                    var b=Surface(exit+radial*(arena.ExitRadius+.2f),3,7)+Vector3.up*.07f;
                    Line("Extraction perimeter dash",new[]{a,b},green,.045f).transform.SetParent(zone.transform,true);
                }
                for(int post=0;post<3;post++)
                {
                    float angle=post*Mathf.PI*2/3;
                    var p=Surface(exit+new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*2.1f,3,7);
                    Beacon("Extraction field beacon",p,green,housing,1.25f).transform.SetParent(zone.transform,true);
                }
                var label=new GameObject("Extraction identity");label.layer=2;label.transform.SetParent(zone.transform,false);
                label.transform.position=exit+Vector3.up*2.1f;
                var text=label.AddComponent<TextMesh>();text.text=i==0?"A":"B";text.anchor=TextAnchor.MiddleCenter;
                text.characterSize=.2f;text.fontSize=64;text.color=new Color(.72f,1,.83f);
                text.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");label.GetComponent<MeshRenderer>().sharedMaterial=text.font.material;
                m_Labels.Add(label.transform);
            }
            var target=BountyClashArenaSettings.Position(arena.Target);m_TargetPosition=m_BountyPosition=target;
            var floor=target-Vector3.up*.45f;
            m_TargetSignals.Add(Beacon("Fallen target field lamp",Surface(floor+Vector3.right*1.8f,2,4),m_ObjectiveMaterial,housing,.6f));
            m_TargetSignals.Add(Beacon("Fallen target field lamp",Surface(floor-Vector3.right*1.8f,2,4),m_ObjectiveMaterial,housing,.6f));
            var corpse=new GameObject("Collapsed organic target / no AI");corpse.layer=2;corpse.transform.SetParent(m_Root.transform,false);corpse.transform.position=floor;
            var shape=new BranchCreatureSettings {arms=6,reach=2,rootRadius=.2f,tipRadius=.03f,bodyRadii=new Vector3(.76f,.25f,.62f),smoothness=.16f};
            m_Corpse=new BranchCreatureRig(corpse.transform,s_Mask,shape,ProceduralMenagerieExperiment.Species.Tentacle);
            var motor=new SurfaceMotor(floor+Vector3.up*.65f,Quaternion.identity,s_Mask);
            m_Corpse.Tick(motor,shape,0,0,0,.016f);m_Corpse.SetVisible(true);
            m_Tissue=TissueTexture();
            // BranchCreatureRig allocates its skin per instance; no live Boss asset is mutated.
            var skin=corpse.GetComponentInChildren<Renderer>().sharedMaterial;
            skin.SetColor("_BaseColor",Color.white);skin.SetTexture("_BaseMap",m_Tissue);
            skin.SetFloat("_Smoothness",.16f);skin.SetFloat("_Metallic",0);
            PoseCollapsedLimbs(corpse,floor);
            foreach(var renderer in corpse.GetComponentsInChildren<Renderer>())renderer.shadowCastingMode=ShadowCastingMode.Off;
            var lobes=new[]{new Vector3(-.32f,.27f,.06f),new Vector3(.22f,.22f,-.21f),new Vector3(.05f,.34f,.28f)};
            for(int i=0;i<lobes.Length;i++)
            {
                var lobe=Part("Ruptured mantle tissue",PrimitiveType.Sphere,floor+lobes[i],new Vector3(.72f-i*.1f,.35f+i*.04f,.55f),skin);
                lobe.transform.rotation=Quaternion.Euler(12+i*13,27+i*38,-17+i*11);Roughen(lobe,.055f);
            }
            var wound=Material("Exposed tissue",new Color(.2f,.055f,.065f),true);
            for(int i=0;i<5;i++)Part("Irregular exposed tissue",PrimitiveType.Sphere,floor+new Vector3(-.3f+i*.13f,.47f-Mathf.Abs(i-2)*.035f,.05f),new Vector3(.18f,.065f,.12f),wound);
            m_BountyCore=Part("Unique ground bounty signal / cosmetic",PrimitiveType.Cube,target+Vector3.up*.15f,new Vector3(.14f,.23f,.14f),m_ObjectiveMaterial);
            m_BountyCore.transform.rotation=Quaternion.Euler(0,32,45);m_BountyCore.SetActive(false);
            m_BountySignal=Line("Ground bounty signal",new[]{target+Vector3.up*.3f,target+Vector3.up*.75f},m_ObjectiveMaterial,.025f);m_BountySignal.SetActive(false);
            // The formal map intentionally maps saturated environment colors to cobalt.
            // Preserve gameplay semantics through its existing actor/subject path.
            MoonSilverPrintSubject.Protect(m_Root);
        }
        private void PoseCollapsedLimbs(GameObject corpse,Vector3 floor)
        {
            var lengths=new[]{1.8f,1.2f,2f,1.5f,1.65f,1.1f};var turns=new[]{12f,-8f,17f,5f,-14f,4f};
            foreach(var filter in corpse.GetComponentsInChildren<MeshFilter>())
            {
                if(filter.sharedMesh.name!="Branched creature surface") {Roughen(filter.gameObject,.035f);continue;}
                var mesh=filter.sharedMesh;var vertices=mesh.vertices;var uv=new Vector2[vertices.Length];
                for(int arm=0;arm<6;arm++)
                {
                    var radial=Quaternion.Euler(0,arm*60+turns[arm],0)*Vector3.forward;var side=Vector3.Cross(Vector3.up,radial);
                    var a=floor+radial*.42f;var b=a+radial*.5f;var c=floor+radial*lengths[arm]+side*(arm%2==0?.75f:-.6f);
                    var d=floor+radial*(lengths[arm]*.62f)+side*(arm%2==0?1f:-.85f);
                    Vector3 Curve(float t) {float q=1-t;return q*q*q*a+3*q*q*t*b+3*q*t*t*c+t*t*t*d;}
                    for(int j=0;j<BranchCreatureRig.Samples;j++)
                    {
                        float t=j/(BranchCreatureRig.Samples-1f),radius=Mathf.Lerp(.2f,.025f,Mathf.Pow(t,.7f))*(1+.07f*Mathf.Sin(t*31+arm));
                        var p=Curve(t);var ground=Surface(new Vector3(p.x,floor.y,p.z),1.2f,2.4f);p.y=ground.y+radius*.72f;
                        var tangent=Curve(Mathf.Min(1,t+.02f))-Curve(Mathf.Max(0,t-.02f));var cross=Vector3.Cross(Vector3.up,tangent.normalized);
                        for(int s=0;s<=12;s++)
                        {
                            float angle=s*Mathf.PI*2/12;int index=(arm*BranchCreatureRig.Samples+j)*13+s;
                            vertices[index]=filter.transform.InverseTransformPoint(p+(cross*Mathf.Cos(angle)+Vector3.up*Mathf.Sin(angle)*.72f)*radius);
                            uv[index]=new Vector2(s/12f,t*2.3f+arm*.31f);
                        }
                    }
                }
                mesh.vertices=vertices;mesh.uv=uv;mesh.RecalculateNormals();mesh.RecalculateBounds();
            }
        }
        private void Roughen(GameObject go,float strength)
        {
            var filter=go.GetComponent<MeshFilter>();if(!filter)return;
            var mesh=Object.Instantiate(filter.sharedMesh);mesh.name="Owned irregular organic surface";m_Meshes.Add(mesh);
            var vertices=mesh.vertices;
            for(int i=0;i<vertices.Length;i++)
            {var p=vertices[i];float n=Mathf.PerlinNoise(p.x*8.7f+1.6f,p.y*7.4f+p.z*4.7f);vertices[i]=p*(1+(n-.5f)*strength*2);}
            mesh.vertices=vertices;mesh.RecalculateNormals();mesh.RecalculateBounds();filter.sharedMesh=mesh;
        }
        private static Texture2D TissueTexture()
        {
            var texture=new Texture2D(64,64,TextureFormat.RGBA32,true) {name="Owned rough organic tissue",wrapMode=TextureWrapMode.Repeat};
            var colors=new Color[64*64];
            for(int y=0;y<64;y++)for(int x=0;x<64;x++)
            {float n=Mathf.PerlinNoise(x*.17f,y*.21f);float veins=Mathf.Abs(Mathf.Sin(x*.32f+Mathf.PerlinNoise(x*.08f,y*.12f)*9));colors[y*64+x]=Color.Lerp(new Color(.07f,.035f,.055f),new Color(.27f,.14f,.17f),n*.7f+veins*.3f);}
            texture.SetPixels(colors);texture.Apply();return texture;
        }
        public void PresentObjective(float progress,bool bountyAvailable)
        {
            var color=Color.Lerp(new Color(.95f,.62f,.21f),new Color(.64f,.82f,1),Mathf.Clamp01(progress));m_ObjectiveMaterial.SetColor("_BaseColor",color);
            m_BountyCore.SetActive(bountyAvailable);m_BountySignal.SetActive(bountyAvailable);
            bool atSite=Vector3.ProjectOnPlane(m_BountyPosition-m_TargetPosition,Vector3.up).sqrMagnitude<=4;
            foreach(var signal in m_TargetSignals)signal.SetActive(progress<1 || bountyAvailable&&atSite);
        }
        public void SetBountyPosition(Vector3 position)
        {
            m_BountyPosition=position;m_BountyCore.transform.position=position+Vector3.up*.15f;
            var line=m_BountySignal.GetComponent<LineRenderer>();line.SetPosition(0,position+Vector3.up*.3f);line.SetPosition(1,position+Vector3.up*.75f);
        }
        private Material Material(string name,Color color,bool lit=false)
        {
            var material=new Material(Shader.Find(lit?"Universal Render Pipeline/Lit":"Universal Render Pipeline/Unlit")) {name=name};
            material.SetColor("_BaseColor",color);if(lit){material.SetFloat("_Smoothness",.18f);material.SetFloat("_Metallic",.1f);}m_Materials.Add(material);return material;
        }
        private GameObject Beacon(string name,Vector3 floor,Material light,Material housing,float height)
        {
            var root=new GameObject(name);root.layer=2;root.transform.SetParent(m_Root.transform,false);
            Part("Weighted field base",PrimitiveType.Cube,floor+Vector3.up*.06f,new Vector3(.3f,.12f,.26f),housing).transform.SetParent(root.transform,true);
            Part("Beacon mast",PrimitiveType.Cylinder,floor+Vector3.up*(height*.5f),new Vector3(.055f,height*.5f,.055f),housing).transform.SetParent(root.transform,true);
            Part("Beacon lamp",PrimitiveType.Cylinder,floor+Vector3.up*height,new Vector3(.11f,.08f,.11f),light).transform.SetParent(root.transform,true);return root;
        }
        private GameObject Part(string name,PrimitiveType primitive,Vector3 position,Vector3 scale,Material material)
        {
            var go=GameObject.CreatePrimitive(primitive);go.name=name;go.layer=2;go.transform.SetParent(m_Root.transform,false);go.transform.position=position;go.transform.localScale=scale;
            var collider=go.GetComponent<Collider>();collider.enabled=false;Destroy(collider);go.GetComponent<Renderer>().sharedMaterial=material;return go;
        }
        private GameObject Line(string name,Vector3[] points,Material material,float width)
        {
            var go=new GameObject(name);go.layer=2;go.transform.SetParent(m_Root.transform,false);var line=go.AddComponent<LineRenderer>();line.sharedMaterial=material;
            line.useWorldSpace=true;line.positionCount=points.Length;line.SetPositions(points);line.startWidth=line.endWidth=width;line.startColor=line.endColor=Color.white;
            line.shadowCastingMode=ShadowCastingMode.Off;line.receiveShadows=false;return go;
        }
        private static Vector3 Surface(Vector3 point,float up,float distance) => UnityEngine.Physics.Raycast(point+Vector3.up*up,Vector3.down,out var hit,distance,s_Mask,QueryTriggerInteraction.Ignore)?hit.point:point;
        public void FaceLabels(Camera camera) {if(camera)foreach(var label in m_Labels)label.rotation=Quaternion.LookRotation(label.position-camera.transform.position,Vector3.up);}
        public void Dispose()
        {
            if(m_Root)m_Root.SetActive(false);m_Corpse?.Release();foreach(var mesh in m_Meshes)Destroy(mesh);foreach(var material in m_Materials)Destroy(material);Destroy(m_Tissue);Destroy(m_Root);
        }
        private static void Destroy(Object item) {if(Application.isPlaying)Object.Destroy(item);else Object.DestroyImmediate(item);}
    }
}
