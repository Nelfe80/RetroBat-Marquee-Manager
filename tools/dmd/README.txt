DMD runtime folder / Dossier runtime DMD
=======================================

EN
--
Purpose:
This folder stores local DMD runtime files used by MarqueeManager when DMD
output is enabled, such as native DLLs, dmdext-related binaries, and hardware
runtime dependencies.

Installation:
1. Restore the DMD runtime bundle from the local MarqueeManager release package
   or from the hardware/vendor distribution used by your setup.
2. Copy the runtime files into MarqueeManager/tools/dmd.
3. Expected files are loaded directly from this folder:
   MarqueeManager/tools/dmd/dmdext.exe
   MarqueeManager/tools/dmd/DmdDevice.dll
   MarqueeManager/tools/dmd/DmdDevice64.dll
   MarqueeManager/tools/dmd/DmdDevice.ini
   MarqueeManager/tools/dmd/DmdDevice.log.config
4. Do not place those files in a bin subfolder. They must stay directly in
   MarqueeManager/tools/dmd.
5. Keep DLLs, EXEs, logs, and generated runtime files in this folder. Git only
   tracks this README.txt.
6. If DMD output is disabled in config.ini, this folder can stay empty except
   for this README.

FR
--
Role :
Ce dossier stocke les fichiers runtime DMD locaux utilises par MarqueeManager
quand la sortie DMD est activee, comme les DLL natives, binaires lies a dmdext
et dependances runtime materielles.

Installation :
1. Restaurer le bundle runtime DMD depuis le package local MarqueeManager ou
   depuis la distribution materiel/fournisseur utilisee par ton installation.
2. Copier les fichiers runtime dans MarqueeManager/tools/dmd.
3. Les fichiers attendus sont charges directement depuis ce dossier :
   MarqueeManager/tools/dmd/dmdext.exe
   MarqueeManager/tools/dmd/DmdDevice.dll
   MarqueeManager/tools/dmd/DmdDevice64.dll
   MarqueeManager/tools/dmd/DmdDevice.ini
   MarqueeManager/tools/dmd/DmdDevice.log.config
4. Ne pas placer ces fichiers dans un sous-dossier bin. Ils doivent rester
   directement dans MarqueeManager/tools/dmd.
5. Garder les DLL, EXE, logs et fichiers generes dans ce dossier. Git ne suit
   que ce README.txt.
6. Si la sortie DMD est desactivee dans config.ini, ce dossier peut rester vide
   sauf ce README.
