/// @description Insert description here
// You can write your code in this editor
if animDelay <= 0 and createdAnims < 10{
	var created = instance_create_depth(animLocX[createdAnims],animLocY[createdAnims],10-createdAnims,loginAnim);
	if createdAnims != 0{
		created.image_xscale = 0.4;
		created.image_yscale = 0.4;
		created.lifeTimer = 300 - createdAnims * 20;
	}
	animDelay = 60 - createdAnims * 8;
	createdAnims += 1;
}

if createdAnims > 9 and proceedTimer > 0{
	proceedTimer -= 1;

}

if proceedTimer == 0 and finished == false{
	image_alpha += (1-image_alpha)/6;
	if image_alpha > 0.95{
		finished = true;
	}
}

if finished == true and flickerTime < 0{
	image_alpha = 1 + random_range(-0.1,-0.4);
	flickerTime = round(random_range(15,30));
}

animDelay -= 1;
flickerTime -= 1;

if flickerTime < 10 and flickerTime % 2 == 0{
	shake = round(random_range(-20,20))
}

else if flickerTime % 2 == 0{
	shake = round(random_range(-5,5))
}
