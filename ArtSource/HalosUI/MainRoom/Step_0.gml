/// @description Insert description here
// You can write your code in this editor
xpos = camera_get_view_x(view_camera[0]);
s1Click = [0,0,0]
s2Click = [0,0]
s3Click = [0,0,0]
tempext = extrude;

if flickerTime > 5{
	image_alpha = random_range(0.6,0.8);
	flickerTime = 0;
}

flickerTime += 1;

if isViewing == false{
	if fadePos < 600{
		fadePos += 50;
	}

	if fadePos == 600 and tabletState > 0{
		tabletState -= 1;
	}
}

else{
	if tabletState < 100{
		tabletState += 1;
	}
}

if scene == 1 and tabletState == 0{
	if buttonCD <= 0 and mouse_x > xpos + 1720 and mouse_x < xpos + 1920 and mouse_y > 350 and mouse_y < 700{
		scene = 2;
		camTarX = 1920;
		buttonCD = 60;
	}

	if mouse_x > 460 and mouse_x < 685 and mouse_y > 245 and mouse_y < 415{
		s1Click[1] = 1;
		extrude += 10;

		if mouse_check_button_pressed(mb_left){
			isViewing = true;
		}
	}
	if mouse_x > 720 and mouse_x < 1015 and mouse_y > 655 and mouse_y < 880{
		s1Click[0] = 1;
		extrude += 10;

		if mouse_check_button_pressed(mb_left){
			isViewing = true;
		}
	}
	if mouse_x > 1075 and mouse_x < 1420 and mouse_y > 430 and mouse_y < 655{
		s1Click[2] = 1;
		extrude += 10;

		if mouse_check_button_pressed(mb_left){
			isViewing = true;
		}
	}

}

if scene == 2 and tabletState == 0{
	if buttonCD <= 0 and mouse_x > xpos and mouse_x < xpos + 200 and mouse_y > 350 and mouse_y < 700{
		scene = 1;
		camTarX = 0;
		buttonCD = 60;
	}

	if buttonCD <= 0 and mouse_x > xpos + 1720 and mouse_x < xpos + 1920 and mouse_y > 350 and mouse_y < 700{
		scene = 3;
		camTarX = 3840;
		buttonCD = 60;
	}
	if mouse_x > 2300 and mouse_x < 2510 and mouse_y > 250 and mouse_y < 485{
		s2Click[1] = 1;
		extrude += 10;

		if mouse_check_button_pressed(mb_left){
			isViewing = true;
		}
	}
	if mouse_x > 2415 and mouse_x < 2710 and mouse_y > 575 and mouse_y < 755{
		s2Click[0] = 1;
		extrude += 10;

		if mouse_check_button_pressed(mb_left){
			isViewing = true;
		}
	}
}

if scene == 3 and tabletState == 0{
	if buttonCD <= 0 and mouse_x > xpos and mouse_x < xpos + 200 and mouse_y > 350 and mouse_y < 700{
		scene = 2;
		camTarX = 1920;
		buttonCD = 60;
	}
	if mouse_x > 4290 and mouse_x < 4650 and mouse_y > 135 and mouse_y < 390{
		s3Click[2] = 1;
		extrude += 10;

		if mouse_check_button_pressed(mb_left){
			isViewing = true;
		}
	}
	if mouse_x > 4710 and mouse_x < 5010 and mouse_y > 680 and mouse_y < 850{
		s3Click[1] = 1;
		extrude += 10;

		if mouse_check_button_pressed(mb_left){
			isViewing = true;
		}
	}
	if mouse_x > 5025 and mouse_x < 5250 and mouse_y > 440 and mouse_y < 650{
		s3Click[0] = 1;
		extrude += 10;

		if mouse_check_button_pressed(mb_left){
			isViewing = true;
		}
	}
}

if tempext == extrude{
	extrude = extrude/2;
}

if extrude > 30{
	extrude = 30;
}




if buttonCD > 0{
	buttonCD -= 1;
}

camera_set_view_pos(view_camera[0],camX,0);
camX += (camTarX-camX)/2;
