using Unity.MP_FPS.Inventory;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MP_FPS.Client
{
    public sealed partial class StashItemArt
    {
        private void SpeciesOrgan(int species)
        {
            uint color=species==4 ? 0xbf8b4eu : species==5 || species==7 ? 0x678d68u : species==6 ? 0x5ba1a0u : 0xa675abu;
            if(species==0)
            {
                Shape(color,new Vector2(50,26),new Vector2(66,43),new Vector2(62,68),new Vector2(38,68),new Vector2(34,43));
                for(int i=0;i<4;i++) {float y=34+i*12;Line(color,4,new Vector2(39,y),new Vector2(21,y-9),new Vector2(12,y+4));Line(color,4,new Vector2(61,y),new Vector2(79,y-9),new Vector2(88,y+4));}
            }
            else if(species==1)
                Line(color,13,new Vector2(29,21),new Vector2(65,21),new Vector2(75,36),new Vector2(61,49),new Vector2(34,49),new Vector2(24,64),new Vector2(39,79),new Vector2(73,79));
            else if(species==2)
            {for(int i=0;i<4;i++){Ring(color,10,29+i*14,55+Mathf.Sin(i)*8,14);Ring(0xd8ddb1,2,29+i*14,55+Mathf.Sin(i)*8,5);}}
            else if(species==3)
            {
                Shape(color,new Vector2(50,27),new Vector2(66,18),new Vector2(77,32),new Vector2(72,56),new Vector2(50,73),new Vector2(28,56),new Vector2(23,32),new Vector2(34,18));
                for(int i=0;i<4;i++)Line(color,5,new Vector2(34+i*10,61),new Vector2(19+i*20,78),new Vector2(14+i*22,88));
            }
            else if(species==4)
            {Box(color,40,18,20,65);for(int i=0;i<6;i++){float y=23+i*10;Line(color,3,new Vector2(43,y),new Vector2(27,y-4),new Vector2(20,y+5));Line(color,3,new Vector2(57,y),new Vector2(73,y-4),new Vector2(80,y+5));}}
            else if(species==5)
            {Shape(color,new Vector2(50,12),new Vector2(75,52),new Vector2(71,75),new Vector2(50,87),new Vector2(29,75),new Vector2(25,52));Ring(0xb7d3a0,4,50,63,13);}
            else if(species==6)
            {Shape(color,new Vector2(16,74),new Vector2(20,46),new Vector2(38,22),new Vector2(63,26),new Vector2(82,54),new Vector2(87,76),new Vector2(58,85),new Vector2(28,83));Ring(0xb2e6cf,4,47,53,12);Ring(0xb2e6cf,3,68,68,6);}
            else if(species==7)
            {Ring(color,8,50,40,20);Line(color,6,new Vector2(50,54),new Vector2(48,73),new Vector2(34,89));Line(color,5,new Vector2(49,65),new Vector2(68,72),new Vector2(78,88));Line(color,4,new Vector2(37,52),new Vector2(24,68),new Vector2(17,86));}
            else
            {Ring(color,8,50,46,22);for(int i=0;i<6;i++){float a=i*Mathf.PI/3;var p=new Vector2(50+Mathf.Cos(a)*23,46+Mathf.Sin(a)*23);Line(color,4,p,new Vector2(50+Mathf.Cos(a)*40,46+Mathf.Sin(a)*35));}Line(color,6,new Vector2(51,67),new Vector2(65,82),new Vector2(84,81));}
            Ring(0xe0d5b6,2,50,50,8);
        }
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
                case HaloModule.MooncoreRecycler:
                    color=0x9ED7E0u;
                    for(int i=0;i<3;i++)
                    {
                        float angle=30+i*120;
                        Arc(34,angle,82); Arc(24,angle+8,66);
                        Line(0xB8C9DAu,4,Point(24,angle+8),Point(34,angle+8));
                        Line(color,3,Point(30,angle+68),Point(34,angle+82),Point(40,angle+71));
                    }
                    break;
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
