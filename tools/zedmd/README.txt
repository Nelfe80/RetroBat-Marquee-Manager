ZeDMD runtime folder / Dossier runtime ZeDMD
===========================================

EN
--
Purpose:
This folder stores local ZeDMD runtime files used by MarqueeManager when ZeDMD
hardware output is enabled.

Installation:
1. Restore the ZeDMD runtime package matching your hardware and driver version.
2. Copy the runtime files into MarqueeManager/tools/zedmd.
3. Expected files are loaded directly from this folder:
   MarqueeManager/tools/zedmd/zedmd.dll
   MarqueeManager/tools/zedmd/zedmd64.dll
   MarqueeManager/tools/zedmd/libserialport.dll
   MarqueeManager/tools/zedmd/libserialport64.dll
   MarqueeManager/tools/zedmd/sockpp.dll
   MarqueeManager/tools/zedmd/sockpp64.dll
4. Do not place those files in a bin subfolder. They must stay directly in
   MarqueeManager/tools/zedmd.
5. Keep native libraries, helper binaries, firmware/vendor utilities, logs, and
   generated files local to this folder.
6. Git ignores the runtime contents and tracks only this README.txt.

FR
--
Role :
Ce dossier stocke les fichiers runtime ZeDMD locaux utilises par MarqueeManager
quand la sortie materielle ZeDMD est activee.

Installation :
1. Restaurer le package runtime ZeDMD correspondant a ton materiel et a ta
   version de driver.
2. Copier les fichiers runtime dans MarqueeManager/tools/zedmd.
3. Les fichiers attendus sont charges directement depuis ce dossier :
   MarqueeManager/tools/zedmd/zedmd.dll
   MarqueeManager/tools/zedmd/zedmd64.dll
   MarqueeManager/tools/zedmd/libserialport.dll
   MarqueeManager/tools/zedmd/libserialport64.dll
   MarqueeManager/tools/zedmd/sockpp.dll
   MarqueeManager/tools/zedmd/sockpp64.dll
4. Ne pas placer ces fichiers dans un sous-dossier bin. Ils doivent rester
   directement dans MarqueeManager/tools/zedmd.
5. Garder les bibliotheques natives, binaires helpers, utilitaires
   firmware/fournisseur, logs et fichiers generes dans ce dossier.
6. Git ignore le contenu runtime et ne suit que ce README.txt.
