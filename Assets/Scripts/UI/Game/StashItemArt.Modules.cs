using Unity.MP_FPS.Inventory;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MP_FPS.Client
{
    public sealed partial class StashItemArt
    {
        private void AlienMeat(bool specimen=false)
        {
            if(specimen){Box(0x526b6b,15,12,70,76);Box(0xb2cebc,20,17,60,66);}
            Shape(0x512e52,new Vector2(20,39),new Vector2(39,23),new Vector2(66,28),new Vector2(83,49),new Vector2(71,76),new Vector2(40,81),new Vector2(17,64));
            Shape(0xa85880,new Vector2(28,42),new Vector2(42,33),new Vector2(63,36),new Vector2(72,51),new Vector2(63,67),new Vector2(39,70),new Vector2(25,59));
            Line(0xe0a8b0,3,new Vector2(32,47),new Vector2(44,43),new Vector2(58,49),new Vector2(68,47));
            Line(0xe0a8b0,3,new Vector2(29,58),new Vector2(43,55),new Vector2(55,61),new Vector2(65,57));
            if(specimen){Box(0x547c71,15,12,70,9);Box(0xe0d9b8,60,70,17,9);}
        }

        private void AlienOrgan()
        {
            Ring(0x7662ab,3,50,50,37);
            Shape(0x493355,new Vector2(50,19),new Vector2(73,31),new Vector2(76,58),new Vector2(61,77),new Vector2(40,79),new Vector2(24,57),new Vector2(29,33));
            Shape(0xa176ba,new Vector2(48,27),new Vector2(65,37),new Vector2(67,55),new Vector2(55,68),new Vector2(38,65),new Vector2(33,47));
            Line(0xd4edc3,3,new Vector2(49,30),new Vector2(45,45),new Vector2(57,54),new Vector2(50,68));
            Line(0xd4edc3,2,new Vector2(45,45),new Vector2(34,48));
            Line(0xd4edc3,2,new Vector2(57,54),new Vector2(65,40));
            Ring(0xe9ca75,3,78,23,8);
        }

        private void Module(HaloModule module)
        {
            uint color=module==HaloModule.Capacitor || module==HaloModule.FanArray ? 0xA57320u :
                module==HaloModule.PulseController || module==HaloModule.HitMarker ? 0x785DA3u : 0x267E83u;
            Vector2 Point(float radius,float angle)
            { float a=angle*Mathf.Deg2Rad;return new Vector2(50+radius*Mathf.Cos(a),50+radius*Mathf.Sin(a)); }
            void Arc(float radius,float from,float span)
            {
                m_Painter.strokeColor=C(color);m_Painter.lineWidth=3*m_Scale;m_Painter.BeginPath();
                m_Painter.Arc(P(50,50),radius*m_Scale,Angle.Degrees(from),Angle.Degrees(from+span));m_Painter.Stroke();
            }
            switch(module)
            {
                case HaloModule.Capacitor:
                    for(int i=0;i<6;i++) Arc(32,i*60+5,48);
                    Arc(23,0,360); break;
                case HaloModule.ChargeBridge:
                    for(int i=0;i<6;i++) {Arc(34,i*60-9,18);Line(color,4,Point(17,i*60),Point(34,i*60));} break;
                case HaloModule.FocusLens:
                    for(int i=0;i<4;i++) {Arc(25,45+i*90-25,50);Line(color,3,Point(37,45+i*90-12),Point(25,45+i*90),Point(37,45+i*90+12));} break;
                case HaloModule.FanArray:
                    for(int i=0;i<6;i++) {Arc(37,i*60-18,36);Line(color,3,Point(37,i*60-18),Point(20,i*60),Point(37,i*60+18));} break;
                case HaloModule.PulseController:
                    Arc(28,25,310);
                    for(int i=0;i<3;i++) {var p=Point(29,220+i*50);Line(color,4,p+new Vector2(0,-7),p+new Vector2(7,0),p+new Vector2(0,7),p+new Vector2(-7,0),p+new Vector2(0,-7));} break;
                case HaloModule.HitMarker:
                    Arc(33,30,300);Line(color,4,new Vector2(50,30),new Vector2(70,50),new Vector2(50,70),new Vector2(30,50),new Vector2(50,30));break;
            }
        }
    }
}
