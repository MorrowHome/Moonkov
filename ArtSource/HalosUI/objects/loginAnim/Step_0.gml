/// @description Insert description here
// You can write your code in this editor
randomise();
textDisplay += possibleLetters[round(random_range(0,35))];
currentLineLen += 1;



if currentLineLen == target{
	textDisplay += "\n";
	target = cutLine[round(random_range(0,6))];
	currentLineLen = 0;
}

lifeTimer -= 1;
if lifeTimer < 0{
	image_alpha -= (image_alpha)/6;
	if image_alpha < 0.1{
		instance_destroy();
	}
}
