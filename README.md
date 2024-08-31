<img src="https://github.com/Nelfe80/RetroBat-Marquee-Manager/blob/master/dist/images/logo.png" style="width:100%;">
<h1>RetroBat Marquee Manager (V3.3)</h1>
<h2>A Dynamic Marquees for RetroBat with Svg support / Auto-resizing / Dynamic Scraping / RetroAchievements (WIP)</h2>
<p>This project enables <b>dynamic display of marquees</b> on a secondary topper screen for RetroBat users on Windows 8+, utilizing custom scripts to manage the display based on user interactions.
</p>
<p>Thanks to Aynshe, Bob Morane and Retrobat's community testers. </p>
<p>
    <a href="https://www.youtube.com/watch?v=7LwR_cwa0Cg" target="_blank">
        <img src="https://i.ytimg.com/vi/7LwR_cwa0Cg/hqdefault.jpg" alt="Lien vers la vidéo YouTube">
    </a>
    <a href="https://www.youtube.com/watch?v=AFS7f5RKJZo" target="_blank">
        <img src="https://i.ytimg.com/vi/AFS7f5RKJZo/hqdefault.jpg" alt="Lien vers la vidéo YouTube">
    </a>
</p>
<h2>Install</h2>
<p>
To install the Marquee Manager in RetroBat, follow these steps:<br>
- Access the Main Menu: Start by launching RetroBat. Once you're on the main screen, navigate to the Main Menu.<br>
- Updates and Downloads: In the Main Menu, look for the Updates and Downloads section. Select it to proceed.<br>
- Download Content: Within the Updates and Downloads menu, find and select the Download Content option.<br>
- Media Tab: After selecting Download Content, switch to the Media tab.<br>
- Select Marquee Manager: In the Media tab, you will see a list of media options available for download. Look for and select Marquee Manager from the list.<br>
- Use BatGui for Configuration: Once the Marquee Manager is installed, you can use BatGui to further configure the settings. This includes selecting which screen to target for the marquee display and deciding whether to activate any additional modules.<br>
</p>
</p>
<h2>Configuring config.ini File if needed</h2>
<p>
Configure config.ini ( in the folder /RetroBat/plugins/MarqueeManager/ ) to specify paths for your own marquees and other key settings like accepted formats, MPV path and ImageMagick path, etc. This file is crucial for the marquee system to function properly. (MarqueeRetroAchievements = true in config.ini file to activate RetroAchievements or MarqueeAutoScraping = true to scrap banners...)
</p>
<p>
<h3>ScreenNumber = 2</h3>
This parameter specifies which screen the marquee should be displayed on. If you have multiple monitors connected to your system, you can assign the marquee to a particular screen by setting this parameter.<br>

Example: ScreenNumber = 2 directs the marquee display to the second monitor connected to your setup.<br>
You can configure this setting manually by editing the config.ini file or through the BatGui interface, where you can select the appropriate screen number from a dropdown menu.<br>

<h3>AcceptedFormats = mp4, jpg, png</h3>
This parameter defines the types of media formats that the Marquee Manager will accept for generating marquees.<br>

mp4: Allows video files to be used as part of the marquee display.<br>
jpg: Allows JPEG image files.<br>
png: Allows PNG image files.<br>
By listing these formats, you ensure that the Marquee Manager can handle and display media in these file types, providing flexibility in the kinds of content you use for your marquees.<br>

<h3>Language = fr</h3>
This setting specifies the language used by the Marquee Manager interface and any relevant text. Setting it to fr switches the language to French, ensuring that all instructions, menus, and notifications are displayed in French.<br>

<h3>MarqueeWidth = 1920</h3>
This parameter sets the width of the marquee display in pixels.<br>

Example: MarqueeWidth = 1920 configures the marquee to have a width of 1920 pixels, which is typically the width of a Full HD screen.<br>
<h3>MarqueeHeight = 360</h3>
This parameter sets the height of the marquee display in pixels.<br>

Example: MarqueeHeight = 360 configures the marquee to have a height of 360 pixels, which is a common height for a marquee display on a secondary screen or a custom display.
</p>
<p>
<h3>MarqueeAutoConvert = false</h3>
This parameter controls whether the Marquee Manager automatically converts existing images into a new image with specific marquee size (MarqueeWidth and MarqueeHeight). When set to false, it will not perform automatic conversions.

<h3>MarqueeRetroAchievements = false</h3>
When set to false, this option disables the integration of RetroAchievements into the marquee. If you want to display RetroAchievements on the marquee, you would set this to true. If it's a dmd screen, it doesn't work, sorry.

<h3>MarqueePinballDMD = false</h3>
This parameter, when set to false, disables the feature that would allow Visual Pinball DMD (Dot Matrix Display) data to be displayed on a LCD marquee. If you are using the Marquee Manager for pinball games, you would set this to true.

<h3>MarqueeAutoScraping = false</h3>
When set to false, this option prevents the Marquee Manager from automatically scraping (on ScreenScraper only, don't forget to set your ScreenScraper login and password in RetroBat) new media content to get marquees ("marquee" image or "screenmarquee" image if "marquee" image doesn't exist) from ScreenScraper. Scraping refers to the process of gathering images and logos from the internet. Setting it to true would automate this process.

<h3>MarqueeAutoGeneration = true</h3>
This is a key parameter that, when set to true, enables the automatic generation of a marquee. The marquee is created using a combination of a fanart image and a scraped logo (Scrap games before the autogen please). This allows you to have a custom marquee display without manual intervention.<br>
<h4>MarqueeAutoGeneration Options</h4>
When MarqueeAutoGeneration is enabled, you can customize the marquee's appearance using the following key commands:<br><br>

F12: Aligns the logo to the right of the marquee.<br>
F11: Centers the logo within the marquee.<br>
F10: Aligns the logo to the left of the marquee.<br>
F9: Moves the fanart background vertically.<br>
F8: Slightly adjusts the fanart background vertically.<br>
F7: Resizes the logo, either increasing or decreasing its size relative to its original dimensions.<br>
F6: Adds a gradient background behind the logo. You can choose between a white, black, or no gradient at all.<br>
These parameters and options allow you to tailor the marquee display to your preferences, ensuring that the visual presentation matches your setup and aesthetic choices.
</p>
<h2>Download and install marquees</h2>
You can download marquees here then install in the default folder "\RetroBat\plugins\MarqueeManager\images\" :<br>Use the format {system_name}-{game_name}.ext. For example, for Mario on NES, use nes-mario.jpg. (game_name = rom name without ext, system_name = system folder)<br>
Launchbox Games Database : <a href="https://gamesdb.launchbox-app.com/">https://gamesdb.launchbox-app.com/</a><br>
Pixelcade Forums : <a href="https://pixelcade.org/forum/art-exchange-lcd/a-few-lcd-marquees-links/#post-2071">https://pixelcade.org/forum/art-exchange-lcd/a-few-lcd-marquees-links/#post-2071</a><br>
<p></p>
<h2>Scrapping usage</h2>
<p>If you're missing any marquee, you can activate <b>dynamic scrapping</b> (MarqueeAutoScraping = true in config.ini file), which allows the system to download marquee image (the real toppers, not the logos) of arcade machines directly if it detects that a marquee is missing, without having to go through the RetroBat scraper "screenscraper" (check your screenscraper id/password in Retrobat). Once scrapping is complete, a small message appears on your marquee's screen like "Metal Slug 3 topper successfully scraped!". If you reselect or play the game again, the marquee will be updated. If you don't see the marquee appear, there may be no marquee image for that game in the screenscraper image database. The "Screenmarquee" images are only downloaded if there is no "Marquee" image. If the scraperfailed option is enabled, failed scraping will be indicated in a scrapfailed.pool file. I encourage you to take part in the screenscraper visual enhancement if you want to see a marquee appear or put it manually in your marquee folder.</p>
<h3>How to Scrape Marquees from RetroBat</h3>
<p>
If you plan to use scraped marquees or incorporate your own custom marquee images into the system, please be aware of an issue in the scraping process. Both logos and marquee images are currently saved with the same suffix <b>-marquee</b> at the end of the file name. This can lead to confusion and potential file conflicts within the system.
</p>
<p>
To scrape marquees directly within RetroBat:
<ol>
<li>Access the scraping menu in RetroBat.</li>
<li>Choose to scrape from SCREENSCRAPER, HFSDB or ARCADEDB in the scraper options.</li>
<li>In the 'Logo Source' option, select 'Banner' to obtain real topper marquees.</li>
</ol>
This approach allows you to scrape specific marquee images that are more suited for use as toppers.
</p>
<h2>START</h2>
<h3>Launch Start.bat</h3>
<br><br>
<h2>Notes</h2>
<p>
For visual pinball, don't forget to update VPinMame
</p>
<p>
It is important to note that SVG files may require additional processing time during their first use. However, once they are converted to PNG format, you will experience smoother navigation and quicker access to these images within the system.
</p>
<p>
Ensure MPV and IMAGEMAGICK are installed in \RetroBat\plugins\MarqueeManager\ directory.
Organize your marquee images according to the structure defined in config.ini.
</p>
<h2>New Feature: DMD Screen Support</h2>
<p>
With the latest update, DMD screen support is now available only for ZeDMD (install esp32 fw : https://github.com/zesinger/ZeDMD_Updater/releases ) and create DMD ( https://www.youtube.com/watch?v=hgdIUG90M0c ) . To enable this, you need to rename `config-dmd.ini` to `config.ini` and adjust the path to `dmd.exe` in your configuration file. Make sure the path correctly points to the location of `dmd.exe` to utilize the new display capabilities.
</p>


