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
seawolf.zip 
Artworks layout on the git MarqueeManager 
Samples sounds
push seawolf.zip samples in /bios/mame/samples/
push seawolf.zip layout in /emulators/mame/artwork/ and unzip
push seawolf.cfg in bios/mame/cfg/
Active your joystick in Analog Mode if needed
Hide Crosshair (in mame menu -> Tab to access)
Advanced game option : 
Emulator > MAME64
Drivers > Video > BGFX
Visual Rendering > BGFX Video Filter > CRT Simulation

SKYDIVER
Artworks layout on the git MarqueeManager 
push skydiver.zip layout in /emulators/mame/artwork/ and unzip
push push seawolf.cfg in bios/mame/cfg\.cfg in bios/mame/cfg/
Advanced game option : 
Emulator > MAME64
Drivers > Video > BGFX
Visual Rendering > BGFX Video Filter > CRT Simulation




