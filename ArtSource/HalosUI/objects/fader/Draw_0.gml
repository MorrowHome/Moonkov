/// @description Insert description here
// You can write your code in this editor
draw_sprite_ext(blackScreen,0,xpos,540-target.fadePos,1,-1,0,c_white,1);
draw_sprite_ext(blackScreen,0,xpos,540+target.fadePos,1,1,0,c_white,1);

draw_sprite_ext(lightFader,0,xpos,540-target.fadePos,1,1,0,c_white,opac);
draw_sprite_ext(lightFader,0,xpos,540+target.fadePos,1,-1,0,c_white,opac);