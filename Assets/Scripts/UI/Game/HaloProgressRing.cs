using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MP_FPS.Client
{
    // The same four sprites and rest layout as RifleHaloVisual. No rotor animation.
    public sealed class HaloProgressRing : VisualElement
    {
        private struct Point
        {
            public Vector2 Position, UV;
            public Point(Vector2 position,Vector2 uv) {Position=position;UV=uv;}
        }
        private sealed class Group
        {
            public Texture Texture;
            public readonly List<Point[]> Pieces = new List<Point[]>();
        }
        private readonly Group[] m_Groups = new Group[4];
        private readonly Point[] m_ClipA = new Point[12], m_ClipB = new Point[12];
        private readonly List<Vertex> m_Vertices = new List<Vertex>(2048);
        private readonly List<ushort> m_Indices = new List<ushort>(4096);
        private readonly Color m_Color;
        private float m_Radius, m_Progress;
        public float Progress => m_Progress;
        public HaloProgressRing()
        {
            name="haloProgressRing";pickingMode=PickingMode.Ignore;
            var art=Resources.Load<HaloProgressArtwork>("Moonkov/UI/HaloProgressArtwork");
            if(!art || !art.Petal || !art.OuterArc || !art.InnerArc || !art.Cell)
                throw new System.InvalidOperationException("Missing formal halo progress artwork");
            m_Color=art.Color;
            Build(0,art.Petal,6,.171f,1);
            Build(1,art.OuterArc,6,.135f,1);
            Build(2,art.InnerArc,3,.09f,1);
            Build(3,art.Cell,30,.114f,.9f);
            generateVisualContent+=Draw;
        }
        public void SetProgress(float value)
        {
            value=float.IsFinite(value) ? Mathf.Clamp01(value) : 0;
            if(m_Progress==value)return;
            m_Progress=value;MarkDirtyRepaint();
        }
        private void Build(int index,Sprite sprite,int count,float radius,float scale)
        {
            var group=new Group {Texture=sprite.texture};m_Groups[index]=group;
            var rect=sprite.textureRect;
            // Tight sprites trim transparent padding. Preserve their pivot and true pixel scale.
            var low=(sprite.textureRectOffset-sprite.pivot)/sprite.pixelsPerUnit;
            var high=low+rect.size/sprite.pixelsPerUnit;
            var corners=new[] {new Vector2(low.x,low.y),new Vector2(high.x,low.y),
                new Vector2(high.x,high.y),new Vector2(low.x,high.y)};
            var uv=new[] {new Vector2(rect.xMin,rect.yMin),new Vector2(rect.xMax,rect.yMin),
                new Vector2(rect.xMax,rect.yMax),new Vector2(rect.xMin,rect.yMax)};
            for(int i=0;i<count;i++)
            {
                float angle=i*Mathf.PI*2/count;float c=Mathf.Cos(angle),s=Mathf.Sin(angle);
                var piece=new Point[4];
                for(int j=0;j<4;j++)
                {
                    var p=corners[j]*scale+new Vector2(radius,0);
                    var position=new Vector2(c*p.x-s*p.y,-s*p.x-c*p.y);
                    piece[j]=new Point(position,new Vector2(uv[j].x/sprite.texture.width,uv[j].y/sprite.texture.height));
                    m_Radius=Mathf.Max(m_Radius,position.magnitude);
                }
                group.Pieces.Add(piece);
            }
        }
        private static float Cross(Vector2 a,Vector2 b)=>a.x*b.y-a.y*b.x;
        private static int Clip(Point[] input,int count,Point[] output,Vector2 boundary,bool positive)
        {
            if(count==0)return 0;
            int written=0;var previous=input[count-1];float previousSide=Cross(boundary,previous.Position)*(positive ? 1 : -1);
            for(int i=0;i<count;i++)
            {
                var current=input[i];float side=Cross(boundary,current.Position)*(positive ? 1 : -1);
                if((side>=0)!=(previousSide>=0))
                {
                    float t=previousSide/(previousSide-side);
                    output[written++]=new Point(Vector2.LerpUnclamped(previous.Position,current.Position,t),Vector2.LerpUnclamped(previous.UV,current.UV,t));
                }
                if(side>=0)output[written++]=current;
                previous=current;previousSide=side;
            }
            return written;
        }
        private void Append(Point[] points,int count,Color color,Vector2 center,float scale)
        {
            if(count<3)return;
            ushort start=(ushort)m_Vertices.Count;
            for(int i=0;i<count;i++)
            {
                var p=center+points[i].Position*scale;
                m_Vertices.Add(new Vertex {position=new Vector3(p.x,p.y,Vertex.nearZ),tint=(Color32)color,uv=points[i].UV});
            }
            // Source quads are clockwise in Unity's y-up coordinates; UI uses y-down.
            for(int i=1;i<count-1;i++) {m_Indices.Add(start);m_Indices.Add((ushort)(start+i+1));m_Indices.Add((ushort)(start+i));}
        }
        private void Draw(MeshGenerationContext context)
        {
            float diameter=Mathf.Min(contentRect.width,contentRect.height);
            if(diameter<=0 || m_Radius<=0)return;
            float scale=diameter*.5f/m_Radius;var center=contentRect.center;
            foreach(var group in m_Groups)
            {
                m_Vertices.Clear();m_Indices.Clear();
                foreach(var piece in group.Pieces)
                {
                    Append(piece,4,new Color(.23f,.25f,.28f,.85f),center,scale);
                    if(m_Progress>=1) {Append(piece,4,m_Color,center,scale);continue;}
                    float end=m_Progress*360;
                    // Intersect fixed quadrant wedges, so fill stays correct beyond a half-circle.
                    for(float from=0;from<end;from+=90)
                    {
                        float a=(from-90)*Mathf.Deg2Rad,b=(Mathf.Min(end,from+90)-90)*Mathf.Deg2Rad;
                        int count=Clip(piece,4,m_ClipA,new Vector2(Mathf.Cos(a),Mathf.Sin(a)),true);
                        count=Clip(m_ClipA,count,m_ClipB,new Vector2(Mathf.Cos(b),Mathf.Sin(b)),false);
                        Append(m_ClipB,count,m_Color,center,scale);
                    }
                }
                if(m_Indices.Count==0)continue;
                var mesh=context.Allocate(m_Vertices.Count,m_Indices.Count,group.Texture);
                foreach(var vertex in m_Vertices)mesh.SetNextVertex(vertex);
                foreach(var index in m_Indices)mesh.SetNextIndex(index);
            }
        }
    }
}
