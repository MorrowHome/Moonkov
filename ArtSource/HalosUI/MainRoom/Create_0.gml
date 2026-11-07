/// @description Insert description here
// You can write your code in this editor
x = 0;
y = 0;

flickerTime = 0;
tabletState = 100;
isViewing = false;

scene = 2;
camTarX = 1920;
camX = 1920;
buttonCD = 0;

fadePos = 600;
var created = instance_create_depth(0,0,-20,fader);
created.target = self;

camera_set_view_pos(view_camera[0],1920,0);
xpos = camera_get_view_x(view_camera[0]);

s1Click = [0,0,0]
s2Click = [0,0]
s3Click = [0,0,0]
extrude = 0;
tempext = 0;

window_set_fullscreen(true);
