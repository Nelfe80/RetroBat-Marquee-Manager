In the Mame bios file "\bios\mame\ini\mame.ini" :
output                    network

Récupérer les state pour récupérer les inputtag : mame -lx nom_du_jeu

In Retrobat Mame

Config system : 
> Advanced System Options > Emulation > Read Ini File : YES
> Advanced System Options > Emulation > Output Game Data > To Network

Config by game :

CHASEHQ
chasehq.zip rev 2 rom 
Advanced game option : 
Emulator > Libreto MAME

LUNAR LANDER
llander.zip
Advanced game option : 
Emulator > MAME64
Autoconfigure controllers : On
Video > Monitor Index : 2

SPY HUNTER
spyhunt.zip US
Advanced game option : 
Emulator > MAME64
Video > Monitor Index : 2

SEA WOLF
https://youtu.be/IJBu5fGKKpE?t=168

seawolf.zip 
Artworks layout on the git MarqueeManager 
Samples sounds here (check authentic) :
https://samples.mameworld.info/Personal%20Web%20Page.htm
push seawolf.zip samples in bios/mame/samples/ to have sounds (check authentic sounds)
push seawolf.zip layout in /emulators/mame/artwork/
Hide Crosshair (in mame menu -> Tab to access)

Advanced game option : 
Emulator > MAME64
Video > Monitor Index : 2
Drivers > Video > BGFX
Visual Rendering > BGFX Video Filter > CRT Simulation
Visual Rendering > GLSL Video Filter > CRT MAME PSGS
Visual Rendering > EFFECT > Scanlines





