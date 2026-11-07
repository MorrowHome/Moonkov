/// @description Insert description here
// You can write your code in this editor
draw_sprite(moon,0,2010,0);

draw_sprite(mainRoomBG,0,0,0);
draw_sprite(mainRoomBG,1,1920,0);
draw_sprite(mainRoomBG,2,3840,0);
draw_sprite_ext(mainRoomLights,0,0,0,1,1,0,c_white,image_alpha);
draw_sprite_ext(MRUI_left,0 + s1Click[0],330-extrude*s1Click[0],-100,1,1,0,c_white,1);
draw_sprite_ext(MRUI_left,2 + s1Click[1],330-extrude*s1Click[1],-100,1,1,0,c_white,1);
draw_sprite_ext(MRUI_left,4 + s1Click[2],330-extrude*s1Click[2],-100,1,1,0,c_white,1);

draw_sprite_ext(MRUI_mid,0 + s2Click[0],2240+extrude*0.7*s2Click[0],280,0.4,0.4,0,c_white,1);
draw_sprite_ext(MRUI_mid,2 + s2Click[1],2220+extrude*0.7*s2Click[1],240,0.4,0.4,0,c_white,1);

draw_sprite_ext(MRUI_right,0 + s3Click[0],4170+extrude*s3Click[0],-100,1,1,0,c_white,1);
draw_sprite_ext(MRUI_right,2 + s3Click[1],4170+extrude*s3Click[1],-100,1,1,0,c_white,1);
draw_sprite_ext(MRUI_right,4 + s3Click[2],4160+extrude*s3Click[2],-100,1,1,0,c_white,1);

if tabletState >= 80{
	draw_sprite_ext(tabletParts,0,xpos + 960,540,1.5,1.5,0,c_white,1);
}

else if tabletState >= 40{
	draw_sprite_ext(tabletParts,0,xpos + 960,540 + 540*(1 - tabletState/80),tabletState/80*1.5,tabletState/80*1.5,0,c_white,1);
}

else if tabletState >= 15{
	draw_sprite_ext(tabletParts,0,xpos + 960,810,0.75,0.75,0,c_white,1);
}

else if tabletState >= 0{
	draw_sprite_ext(tabletParts,0,xpos + 960,810 + (15-tabletState) * 85,0.75,0.75,0,c_white,1);
}

if scene == 1 or scene == 2{
	draw_sprite_ext(screenButton,0,xpos + 960,540,1,1,0,c_white,(100-tabletState-buttonCD*2)/200)
}

if scene == 3 or scene == 2{
	draw_sprite_ext(screenButton,0,xpos + 960,540,-1,1,0,c_white,(100-tabletState-buttonCD*2)/200)
}
