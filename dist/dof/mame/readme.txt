In the Mame bios file "\bios\mame\ini\mame.ini" :
output                    network

In Retrobat Mame
Config system : 
> Advanced System Options > Emulation > Read Ini File : YES
> Advanced System Options > Emulation > Output Game Data > To Network
> Video > Monitor Index > check your number

Config by game :

CHASEHQ
chasehq.zip rev 2 rom 
Advanced game option : 
Emulator > Libreto MAME or MAME64
Visual Rendering > Disable Artwork > No

LUNAR LANDER
llander.zip
push llander.zip layout in /saves/mame/artwork/
Advanced game option : 
Emulator > MAME64
Autoconfigure controllers : On
Visual Rendering > Disable Artwork > No

SPY HUNTER
spyhunt.zip US
push spyhunt.zip layout in /saves/mame/artwork/
Advanced game option : 
Emulator > MAME64
Visual Rendering > Disable Artwork > No

SEA WOLF
seawolf.zip 
Artworks layout on the git MarqueeManager 
Samples sounds
push seawolf.zip samples in /bios/mame/samples/
push seawolf.zip layout in /saves/mame/artwork/
push seawolf.cfg in bios/mame/cfg/
Active your joystick in Analog Mode if needed
Hide Crosshair (in mame menu -> Tab to access)
Advanced game option : 
Emulator > MAME64
Drivers > Video > BGFX
Visual Rendering > BGFX Video Filter > CRT Simulation
Visual Rendering > Disable Artwork > No

SKYDIVER
Artworks layout on the git MarqueeManager 
push skydiver.zip layout in /saves/mame/artwork/
push skydiver.cfg in in bios/mame/cfg/
Advanced game option : 
Emulator > MAME64
Drivers > Video > BGFX
Visual Rendering > Disable Artwork > No
Visual Rendering > BGFX Video Filter > CRT Simulation

AFTER BURNER 2
Artworks layout on the git MarqueeManager 
push aburner2.zip layout in /saves/mame/artwork/
push aburner2.cfg in bios/mame/cfg/
Advanced game option : 
Emulator > MAME64
Drivers > Video > BGFX
Visual Rendering > Disable Artwork > No
Visual Rendering > BGFX Video Filter > CRT Simulation

TERMINATOR 2
Artworks layout on the git MarqueeManager 
push term2.zip layout in /saves/mame/artwork/
Emulator > MAME64
Drivers > Video > BGFX
Visual Rendering > Disable Artwork > No
Visual Rendering > BGFX Video Filter > CRT Simulation

NB : (Get inputtag : mame -lx game_name)

