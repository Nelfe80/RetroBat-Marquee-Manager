using RetroBatMarqueeManager.Core.Interfaces;
using RetroBatMarqueeManager.Core.Models.RetroAchievements;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace RetroBatMarqueeManager.Application.Services
{
    public class ImageConversionService
    {
        private readonly IConfigService _config;
        private readonly IProcessService _processService;
        private readonly ILogger<ImageConversionService> _logger;
        private readonly IOverlayTemplateService _templateService;
        
        // EN: Cache for DMD scrolling text frames to avoid redundant CPU usage
        // FR: Cache pour les frames de texte défilant DMD afin d'éviter une utilisation CPU redondante
        private static readonly Dictionary<string, List<byte[]>> _dmdFramesCache = new Dictionary<string, List<byte[]>>();
        private static readonly SemaphoreSlim _gifGenerationLock = new SemaphoreSlim(1, 1);

        public ImageConversionService(IConfigService config, IProcessService processService, ILogger<ImageConversionService> logger, IOverlayTemplateService templateService)
        {
            _config = config;
            _processService = processService;
            _logger = logger;
            _templateService = templateService;
            
            EnsureCacheDirectory();
        }

        private void EnsureCacheDirectory()
        {
            try
            {
                if (!Directory.Exists(_config.CachePath))
                {
                    Directory.CreateDirectory(_config.CachePath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to create cache directory {_config.CachePath}: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Processes an image (SVG, PNG, JPG) to match Marquee requirements:
        /// - Resized to MarqueeWidth x MarqueeHeight
        /// - Transparent background replaced with MarqueeBackgroundColor (Black)
        /// - Centered and Extented to fill the area
        /// </summary>
        public string ProcessImage(string sourcePath, string subFolder = "")
        {
            if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath)) return sourcePath;
            
            // BYPASS: Do not attempt to convert Video files or GIFs (let MPV handle them)
            var ext = Path.GetExtension(sourcePath).TrimStart('.').ToLowerInvariant();
            var skippedFormats = new HashSet<string> { "mp4", "avi", "mkv", "webm", "mov", "gif" };
            if (skippedFormats.Contains(ext))
            {
                // _logger.LogDebug($"Skipping conversion for video/animated file: {sourcePath}");
                return sourcePath;
            }

            // EN: Check Config: If AutoConvert is disabled AND it's not an SVG (SVGs always need conversion), return original
            // FR: VÃ©rifier Config: Si AutoConvert est dÃ©sactivÃ© ET ce n'est pas un SVG (les SVG nÃ©cessitent toujours une conversion), retourner l'original
            if (!_config.MarqueeAutoConvert && ext != "svg")
            {
                return sourcePath;
            }

            // Determines target path
            // Cache Structure: _cache/{subFolder}/{filename}
            // If subFolder is empty, use root _cache (or throw?)
            
            string fileName = Path.GetFileNameWithoutExtension(sourcePath);
            string cacheDir = _config.CachePath;
            
            if (!string.IsNullOrEmpty(subFolder))
            {
                cacheDir = Path.Combine(_config.CachePath, subFolder);
            }
            
            // Ensure directory
            if (!Directory.Exists(cacheDir)) Directory.CreateDirectory(cacheDir);

            // EN: Use simple fixed filename for logo cache
            // FR: Utiliser un nom de fichier simple fixe pour le cache logo
            string uniqueName = $"{fileName}.png";
            
            // Systems folder uses same simple naming
            if (subFolder == "systems")
            {
                uniqueName = $"{fileName}.png";
            }
            
            string targetPath = Path.Combine(cacheDir, uniqueName);

            // If target exists, return it (unless source is newer? optimize later)
            if (File.Exists(targetPath))
            {
                 // Check dates?
                 if (File.GetLastWriteTime(sourcePath) <= File.GetLastWriteTime(targetPath))
                 {
                     return targetPath;
                 }
            }

            return ConvertImage(sourcePath, targetPath) ? targetPath : sourcePath;
        }

        private bool ConvertImage(string source, string target)
        {
            try
            {
                // Ensure output directory exists
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);

                var w = _config.MarqueeWidth;
                var h = _config.MarqueeHeight;
                var bg = _config.MarqueeBackgroundColor;

                var startInfo = new ProcessStartInfo
                {
                    FileName = _config.IMPath,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(_config.IMPath)
                };

                // Check for Custom Command
                string customCmd = _config.IMConvertCommand;
                var sourceExt = Path.GetExtension(source).TrimStart('.').ToLowerInvariant();
                
                if (sourceExt == "svg" && !string.IsNullOrEmpty(_config.IMConvertCommandSVG))
                {
                    customCmd = _config.IMConvertCommandSVG;
                }

                if (!string.IsNullOrEmpty(customCmd))
                {
                     // Substitutions
                     var processedCmd = customCmd
                        .Replace("{source}", source)
                        .Replace("{target}", target)
                        .Replace("{width}", w.ToString())
                        .Replace("{height}", h.ToString())
                        .Replace("{background}", bg);

                     startInfo.Arguments = processedCmd;
                }
                else
                {
                    // Default Logic
                    var args = new List<string>
                    {
                        "-background", "none",
                        "-density", "300",
                        source,
                        "-resize", $"{w}x{h}",
                        "-background", bg,
                        "-flatten",
                        "-gravity", "center",
                        "-extent", $"{w}x{h}",
                        target
                    };
                    
                    // Assign ArgumentList
                    foreach(var arg in args) startInfo.ArgumentList.Add(arg);
                }

                using var process = Process.Start(startInfo);
                if (process == null) return false;

                process.WaitForExit(10000); 

                if (process.ExitCode != 0)
                {
                    var err = process.StandardError.ReadToEnd();
                    _logger.LogError($"ImageMagick Error: {err}");
                    return false;
                }

                // Robust Check: Wait for file to be truly ready (size stable + readable)
                if (WaitForFile(target))
                {
                    return true;
                }
                
                _logger.LogError($"File {target} was not ready after conversion.");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error converting image {source}: {ex.Message}");
                return false;
            }
        }
        
       /// <summary>
        /// Processes an image for DMD (128x32 usually)
        /// FR: Traite une image pour le DMD (128x32 gÃ©nÃ©ralement)
        /// </summary>
        public string? ProcessDmdImage(string sourcePath, string subFolder, string? system = null, string? gameName = null, int offsetX = 0, int offsetY = 0, bool isHardcore = false, bool forceRegenerate = false)
        {
            if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath)) return sourcePath;
            var ext = Path.GetExtension(sourcePath).TrimStart('.').ToLowerInvariant();
            if (ext == "gif") return sourcePath; // Passthrough existing GIFs
            
            bool isVideo = new[] { "mp4", "avi", "webm", "mkv" }.Contains(ext);

            // Fix: For videos (especially generated ones), prefer the source filename to preserve specific versions (e.g. "Sonic The Hedgehog I")
            if (string.IsNullOrEmpty(gameName) || isVideo)
            {
                 gameName = Path.GetFileNameWithoutExtension(sourcePath);
            }
            // UN-SANITIZED FILENAME for cache consistency with Composition logic
            // DMD drivers on Windows generally handle spaces/parens fine in file paths if quoted.
            // Previous aggressive sanitization caused mismatch (WipEout_Pure vs WipEout Pure...).
            string safeName = gameName; // Use the name as is (Windows file system allows spaces/parens)
            
            // Only strip characters that are ILLEGAL on filesystem
            foreach (var c in Path.GetInvalidFileNameChars()) safeName = safeName.Replace(c, '_');

            // User Request: medias\cache\dmd\[GenerateMarqueeVideoFolder]\[system]\[GameName].gif
            // _config.CachePath is .../medias/_cache
            
            string dmdCacheDir = Path.Combine(_config.CachePath, "dmd");
            
            // If system is provided, we use the new structure requested by user
            // Fix: Only use "generated_videos" folder if it IS actually a video.
            // Static images (logos) should typically go to defaults or standard system folder?
            // Actually, if system is present but it's a static image, let's keep legacy behavior (cache/dmd/[system] or defaults?)
            // The previous logic put EVERYTHING (logos too) in generated_videos if system was present.
            if (!string.IsNullOrEmpty(system) && isVideo)
            {
                var folderName = _config.GenerateMarqueeVideoFolder;
                if (string.IsNullOrWhiteSpace(folderName)) folderName = "generated_videos";
                
                // Override subFolder logic if system is present (assuming subFolder "defaults" is legacy/fallback)
                dmdCacheDir = Path.Combine(dmdCacheDir, folderName, system);
            }
            else if (!string.IsNullOrEmpty(system))
            {
                 // Static image with system context -> cache/dmd/[system]
                 dmdCacheDir = Path.Combine(dmdCacheDir, system);
            }
            else if (!string.IsNullOrEmpty(subFolder)) 
            {
                // Legacy path logic
                dmdCacheDir = Path.Combine(dmdCacheDir, subFolder);
            }
            
            if (!Directory.Exists(dmdCacheDir)) Directory.CreateDirectory(dmdCacheDir);

            // If video, target is GIF. If static, target is PNG.
            string targetExt = isVideo ? ".gif" : ".png";
            string suffix = isHardcore ? "_hc" : "";
            string uniqueName = $"{safeName}{suffix}{targetExt}";
            string targetPath = Path.Combine(dmdCacheDir, uniqueName);

            if (!forceRegenerate && File.Exists(targetPath))
            {
                 // EN: Optimization for System Logos - trust cache if exists (skip timestamp check to avoid latency)
                 // FR: Optimisation pour logos systÃ¨me - faire confiance au cache s'il existe (sauter vÃ©rif date)
                 // System logos are unlikely to change frequently.
                 bool isSystemLogo = !isVideo && (subFolder == "systems" || (system != null && subFolder != "overlays" && subFolder != "generated_videos")); 

                 if (isSystemLogo)
                 {
                      _logger.LogInformation($"[DMD Cache] HIT (System Optimization): Using existing {targetPath} for {sourcePath}");
                      return targetPath;
                 }

                 var srcTime = File.GetLastWriteTime(sourcePath);
                 var tgtTime = File.GetLastWriteTime(targetPath);

                 if (srcTime <= tgtTime)
                 {
                     _logger.LogInformation($"[DMD Cache] HIT (Valid): {targetPath} (Src: {srcTime} <= Tgt: {tgtTime})");
                     return targetPath;
                 }
                 
                 _logger.LogInformation($"[DMD Cache] MISS (Stale): Source {sourcePath} ({srcTime}) is newer than {targetPath} ({tgtTime}). Re-converting.");
            }
            else
            {
                 _logger.LogInformation($"[DMD Cache] MISS (New): Generating {targetPath} from {sourcePath}");
            }

            if (isVideo)
            {
                // If conversion succeeds, return target. 
                // If fails, we CANNOT return sourcePath (mp4) because dmdext crashes.
                
                if (ConvertVideoToGif(sourcePath, targetPath)) // Assuming ConvertVideoToGif is a method in this class
                {
                    return targetPath;
                }
                
                // Fallback: If conversion failed (timeout?), but we have an old file, use it!
                if (File.Exists(targetPath))
                {
                    _logger.LogWarning($"DMD Video Conversion failed for {sourcePath}, using existing cached file (stale): {targetPath}");
                    return targetPath;
                }
                
                _logger.LogError($"DMD Video Conversion failed and no cache available for: {sourcePath}");
                return null;
            }

            return ConvertDmdImage(sourcePath, targetPath, isHardcore) ? targetPath : sourcePath;
        }

        private string? _ffmpegPath = null;
        private string FindFfmpeg()
        {
            if (_ffmpegPath != null) return _ffmpegPath;

            // 1. Check tools/ffmpeg/ffmpeg.exe (Standard)
            // _config.MPVPath is .../tools/mpv/mpv.exe
            var toolsDir = Path.GetDirectoryName(Path.GetDirectoryName(_config.MPVPath));
            if (string.IsNullOrEmpty(toolsDir)) toolsDir = "tools";

            var pathsToCheck = new[] {
                Path.Combine(toolsDir, "ffmpeg", "ffmpeg.exe"),
                Path.Combine(toolsDir, "mpv", "ffmpeg.exe"), // Sometimes bundled
                "ffmpeg.exe", // System path
                "ffmpeg" 
            };

            foreach(var p in pathsToCheck)
            {
                if (File.Exists(p)) 
                {
                    _ffmpegPath = p;
                    return p;
                }
                // Check if system command works? (too slow)
            }
            
            // Allow system path implicitly if not found explicitly?
            return "ffmpeg";
        }

        private bool ConvertVideoToGif(string source, string target)
        {
            try
            {
                var w = _config.DmdWidth;
                var h = _config.DmdHeight;
                var ffmpeg = FindFfmpeg();

                var startInfo = new ProcessStartInfo
                {
                    FileName = ffmpeg,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory // Use App Base Dir
                };

                // FFmpeg High Quality GIF:
                // fps=15: Limit framerate
                // scale=...: Resize to cover (forcing aspect ratio if needed or padding)
                // We want to FILL 128x32.
                // scale=128:32:force_original_aspect_ratio=decrease,pad=128:32:(ow-iw)/2:(oh-ih)/2
                // OR simple scale=128:32 (stretch)
                // Let's use simple stretch for DMD usually, or pad.
                // Let's try Aspect Ratio Preserving Pad:
                // scale=128:32:force_original_aspect_ratio=decrease,pad=128:32:-1:-1:color=black
                
                // High Quality (Palettegen) - Restored because fast filter looked bad
                // We now read stderr so this shouldn't hang anymore.
                // Filter: FPS -> Scale -> Pad -> Split -> PaletteGen -> PaletteUse
                string filterObj = $"fps=25,scale={w}:{h}:force_original_aspect_ratio=decrease,pad={w}:{h}:(ow-iw)/2:(oh-ih)/2:color=black,split[s0][s1];[s0]palettegen[p];[s1][p]paletteuse";

                var args = new List<string>
                {
                    "-y", // Overwrite
                    "-i", source,
                    "-vf", filterObj,
                    "-loop", "0", // Infinite loop
                    target
                };
                
                foreach(var arg in args) startInfo.ArgumentList.Add(arg);
                
                // FR: Mise Ã  jour du log pour reflÃ©ter la rÃ©alitÃ© (High Quality)
                _logger.LogInformation($"Converting Video to GIF (High Quality): {source} -> {target}");
                
                using var process = new Process { StartInfo = startInfo };
                
                process.StartInfo.RedirectStandardError = true;
                process.StartInfo.RedirectStandardOutput = true; // UseOutput?
                process.ErrorDataReceived += (s, e) => { if (e.Data != null) _logger.LogWarning($"[FFMPEG STDERR] {e.Data}"); };

                process.Start();
                process.BeginErrorReadLine();
                
                if (process == null) return false;

                if (!process.WaitForExit(15000)) // Reduced to 15s
                {
                     _logger.LogWarning($"Video conversion timed out (15s) for {source}. Killing process.");
                     try { process.Kill(); } catch { }
                     return false;
                }

                if (process.ExitCode != 0)
                {
                    // Look out! StandardError might have been consumed if redirected?
                    // But we set RedirectStandardError=true.
                    // The original code was reading it.
                    var err = process.StandardError.ReadToEnd();
                    _logger.LogError($"FFmpeg Error ({process.ExitCode}): {err}");

                    // Fix: If input file is invalid (moov atom not found), delete it to prevent infinite failure loops on retry
                    if (err.Contains("moov atom not found") || err.Contains("Invalid data found"))
                    {
                        _logger.LogWarning($"[Self-Healing] Detected corrupt source file '{source}'. Deleting it to force regeneration on next run.");
                        try { File.Delete(source); } catch (Exception exDel) { _logger.LogError($"Failed to delete corrupt file: {exDel.Message}"); }
                    }
                    
                    return false;
                }

                return WaitForFile(target);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error converting Video to GIF {source}: {ex.Message}");
                // If finding ffmpeg failed (System.ComponentModel.Win32Exception)
                if (ex.Message.Contains("find the file"))
                {
                    _logger.LogError("FFmpeg executable not found. Please install FFmpeg in 'tools/ffmpeg/ffmpeg.exe' or add to PATH.");
                }
                return false;
            }
        }

        /// <summary>
        /// Generates a Marquee-style video (Wide) from a standard game video by cropping and overlaying the logo.
        /// FR: GÃ©nÃ¨re une vidÃ©o format Marquee (Large) depuis une vidÃ©o standard en tronquant et superposant le logo.
        /// </summary>
        public string? GenerateMarqueeVideo(string sourceVideo, string logoPath, string system, string gameName)
        {
            _logger.LogWarning($"[DEBUG CHECK] Inside GenerateMarqueeVideo: gameName='{gameName}'");


            if (string.IsNullOrEmpty(sourceVideo) || !File.Exists(sourceVideo)) return null;

            try
            {
                // User Request: videos in medias/[GenerateMarqueeVideoFolder]/[system]/[game].mp4
                // _config.CachePath is .../medias/_cache. We want .../medias/[GenerateMarqueeVideoFolder]/[system]
                var subFolder = _config.GenerateMarqueeVideoFolder;
                if (string.IsNullOrWhiteSpace(subFolder)) subFolder = "generated_videos"; // Fallback to safe default if empty
                
                var parentDir = Directory.GetParent(_config.CachePath);
                if (parentDir == null) return null; // Should not happen if CachePath is valid

                string videoCacheDir = Path.Combine(parentDir.FullName, subFolder, system);
                if (!Directory.Exists(videoCacheDir)) Directory.CreateDirectory(videoCacheDir);

                string targetPath = Path.Combine(videoCacheDir, $"{gameName}.mp4");

                // Check cache reuse
                if (File.Exists(targetPath))
                {
                    // Fix: Check for zero-byte or small corrupted files
                    var info = new FileInfo(targetPath);
                    if (info.Length < 1024) // < 1KB is likely trash or touch
                    {
                         _logger.LogWarning($"[VideoGen] Found small/empty cached file ({info.Length} bytes). Deleting: {targetPath}");
                         try { File.Delete(targetPath); } catch {}
                    }
                    else if (File.GetLastWriteTime(sourceVideo) <= File.GetLastWriteTime(targetPath))
                    {
                        var logoTime = File.Exists(logoPath) ? File.GetLastWriteTime(logoPath) : DateTime.MinValue;
                        if (logoTime <= File.GetLastWriteTime(targetPath))
                        {
                            return targetPath;
                        }
                    }
                }

                _logger.LogInformation($"[VideoGen] Generating marquee video for {gameName}...");

                var ffmpeg = FindFfmpeg();
                var mw = _config.MarqueeWidth;
                var mh = _config.MarqueeHeight;

                // Heuristic for cropping:
                // We want to scale to width, then crop height.
                // To keep the character (often in the lower-middle), we crop with an offset.
                // y = (scaled_h - mh) * 0.5 (Center) -> User wants "character zone". 
                // Let's use 60% down for slightly lower focus.
                
                // (Old logoH calculation removed)
                
                // FFmpeg filter complex:
                // [0:v] : Video input
                // [1:v] : Logo input (optional)
                // 1. Scale video to marquee width (aspect ratio preserved)
                // 2. Crop to marquee height (using character-centric vertical offset)
                // 3. Scale logo to 80% height
                // 4. Overlay logo at top-left (with slight padding)

                string filter;
                var inputs = new List<string> { "-i", sourceVideo };
                bool hasLogo = !string.IsNullOrEmpty(logoPath) && File.Exists(logoPath);

                if (hasLogo)
                {
                    inputs.Add("-i");
                    inputs.Add(logoPath);
                    
                    // EN: Adaptive Scaling based on Aspect Ratio
                    double ar = (double)mw / mh;
                    double heightFactor = (ar > 2.5) ? 0.8 : 0.4;
                    int maxW = (int)(mw * 0.90);
                    int maxH = (int)(mh * heightFactor);
                    
                    _logger.LogInformation($"[VideoGen] Adaptive Scaling: AR={ar:F2}, HeightFactor={heightFactor}, MaxBox={maxW}x{maxH}");
                    
                    filter = $"[0:v]scale={mw}:{mh}:force_original_aspect_ratio=increase,crop={mw}:{mh}:(iw-ow)/2:(ih-oh)*0.4,format=yuv420p[base];" +
                             $"[1:v]scale={maxW}:{maxH}:force_original_aspect_ratio=decrease[logo];" +
                             $"[base][logo]overlay=10:10,format=yuv420p";
                }
                else
                {
                    filter = $"scale={mw}:{mh}:force_original_aspect_ratio=increase,crop={mw}:{mh}:(iw-ow)/2:(ih-oh)*0.4,format=yuv420p";
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = ffmpeg,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory
                };

                var args = new List<string> { "-y" }; // Overwrite
                args.AddRange(inputs);
                
                // Use -filter_complex for multiple inputs (video + logo), -vf for single input
                if (hasLogo)
                {
                    args.Add("-filter_complex"); args.Add(filter);
                }
                else
                {
                    args.Add("-vf"); args.Add(filter);
                }
                // --- FFmpeg HW Encoding ---
                // If config is set (h264_nvenc, h264_amf, h264_qsv), use it.
                var hwEnc = _config.FfmpegHwEncoding;
                if (!string.IsNullOrWhiteSpace(hwEnc))
                {
                    _logger.LogInformation($"[VideoGen] Using HW Encoder: {hwEnc}");
                    args.Add("-c:v");
                    args.Add(hwEnc);
                    // HW encoders often handle bitrate/quality differently than CRF. 
                    // To keep it simple, we let the encoder use its defaults or add simple preset
                    // args.Add("-preset"); args.Add("fast"); 
                }
                else
                {
                     args.Add("-c:v"); args.Add("libopenh264");
                     args.Add("-preset"); args.Add("veryfast"); 
                     args.Add("-crf"); args.Add("23"); 
                }
                args.Add("-an"); // Remove audio (Marquee is silent)
                args.Add(targetPath);

                foreach (var arg in args) startInfo.ArgumentList.Add(arg);

                using (var process = Process.Start(startInfo))
                {
                    if (process == null) return null;
                    
                    // Consume stderr to prevent hang - Log as Warning to be visible
                    process.ErrorDataReceived += (s, e) => { if (e.Data != null) _logger.LogWarning($"[FFmpeg Gen] {e.Data}"); };
                    process.BeginErrorReadLine();

                    if (!process.WaitForExit(60000)) // 60s timeout for video generation
                    {
                        _logger.LogWarning($"[VideoGen] Timeout generating video for {gameName}");
                        try { process.Kill(); } catch { }
                        return null;
                    }

                    if (process.ExitCode != 0)
                    {
                        _logger.LogError($"[VideoGen] FFmpeg failed with code {process.ExitCode}");
                        return null;
                    }
                }

                if (File.Exists(targetPath) && new FileInfo(targetPath).Length > 0)
                {
                    _logger.LogInformation($"[VideoGen] Successfully generated: {targetPath}");
                    return targetPath;
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[VideoGen] Error generating marquee video: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// EN: Generate marquee video with custom offsets for crop/zoom/logo position
        /// FR: GÃ©nÃ©rer vidÃ©o marquee avec offsets personnalisÃ©s pour crop/zoom/position logo
        /// </summary>
        public string? GenerateMarqueeVideoWithOffsets(string sourceVideo, string logoPath, string system, string gameName, 
            int cropX, int cropY, double zoom, int logoX, int logoY, double logoScale, 
            double startTime = 0.0, double endTime = 0.0)
        {


            if (string.IsNullOrEmpty(sourceVideo) || !File.Exists(sourceVideo)) return null;

            try
            {
                var subFolder = _config.GenerateMarqueeVideoFolder;
                if (string.IsNullOrWhiteSpace(subFolder)) subFolder = "generated_videos";
                
                var parentDir = Directory.GetParent(_config.CachePath);
                if (parentDir == null) return null;

                string videoCacheDir = Path.Combine(parentDir.FullName, subFolder, system);
                if (!Directory.Exists(videoCacheDir)) Directory.CreateDirectory(videoCacheDir);

                string targetPath = Path.Combine(videoCacheDir, $"{gameName}.mp4");

                _logger.LogInformation($"[VideoGen] Generating marquee video with custom offsets for {gameName}...");
                _logger.LogInformation($"[VideoGen] Offsets: Crop({cropX},{cropY}) Zoom={zoom:F2} Logo({logoX},{logoY}) Scale={logoScale:F2} Time({startTime:F1}-{endTime:F1})");

                var ffmpeg = FindFfmpeg();
                var mw = _config.MarqueeWidth;
                var mh = _config.MarqueeHeight;

                // EN: Calculate crop position with offsets - default is (iw-ow)/2:(ih-oh)*0.4
                // FR: Calculer position crop avec offsets - dÃ©faut est (iw-ow)/2:(ih-oh)*0.4
                // IMPORTANT: Invert signs to match ImageMagick preview behavior
                // ImageMagick moves the background, FFmpeg moves the crop window
                var invertedCropX = -cropX;
                var invertedCropY = -cropY;
                var cropXExpr = invertedCropX >= 0 ? $"(iw-ow)/2+{invertedCropX}" : $"(iw-ow)/2{invertedCropX}";
                var cropYExpr = invertedCropY >= 0 ? $"(ih-oh)*0.4+{invertedCropY}" : $"(ih-oh)*0.4{invertedCropY}";
                
                // EN: Apply zoom by scaling before crop
                // FR: Appliquer zoom en scalant avant crop
                var scaleW = zoom > 0 ? (int)(mw * zoom) : mw;
                var scaleH = zoom > 0 ? (int)(mh * zoom) : mh;

                // EN: Calculate logo size with scale (default is 80% of marquee height)
                // FR: Calculer taille logo avec scale (dÃ©faut est 80% de hauteur marquee)
                int logoH = (int)(mh * 0.8 * logoScale);

                string filter;
                var inputs = new List<string>();
                
                // Input Seeking (faster) - apply -ss before -i
                if (startTime > 0)
                {
                    inputs.Add("-ss");
                    inputs.Add(startTime.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
                
                // Standard end time handling (can be input or output, input is faster if copy, but we re-encode)
                // If using -ss before input, -to is relative to input start (so it becomes Duration if used as output option, or absolute timestamp?)
                // Actually -ss before -i seeks. Then timestamps reset to 0. So -to should be duration (EndTime - StartTime).
                // Or better, use -t (duration).
                string? durationArg = null;
                if (endTime > startTime && endTime > 0)
                {
                    durationArg = (endTime - startTime).ToString(System.Globalization.CultureInfo.InvariantCulture);
                }

                inputs.Add("-i");
                inputs.Add(sourceVideo);
                
                bool hasLogo = !string.IsNullOrEmpty(logoPath) && File.Exists(logoPath);

                if (hasLogo)
                {
                    inputs.Add("-i");
                    inputs.Add(logoPath);
                    
                    filter = $"[0:v]scale={scaleW}:{scaleH}:force_original_aspect_ratio=increase,crop={mw}:{mh}:{cropXExpr}:{cropYExpr},format=yuv420p[base];" +
                             $"[1:v]scale=-1:{logoH}[logo];" +
                             $"[base][logo]overlay={logoX}:{logoY},format=yuv420p";
                    
                    _logger.LogInformation($"[VideoGen] FFmpeg filter: {filter}");
                }
                else
                {
                    filter = $"scale={scaleW}:{scaleH}:force_original_aspect_ratio=increase,crop={mw}:{mh}:{cropXExpr}:{cropYExpr},format=yuv420p";
                    _logger.LogInformation($"[VideoGen] FFmpeg filter: {filter}");
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = ffmpeg,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory
                };

                var args = new List<string> { "-y" };
                args.AddRange(inputs);
                
                // Apply Duration if set
                if (durationArg != null)
                {
                    args.Add("-t");
                    args.Add(durationArg);
                }

                
                if (hasLogo)
                {
                    args.Add("-filter_complex"); args.Add(filter);
                }
                else
                {
                    args.Add("-vf"); args.Add(filter);
                }
                args.Add("-c:v"); args.Add("libopenh264");
                args.Add("-preset"); args.Add("veryfast");
                args.Add("-crf"); args.Add("23");
                args.Add("-an");
                args.Add(targetPath);

                foreach (var arg in args) startInfo.ArgumentList.Add(arg);

                using (var process = Process.Start(startInfo))
                {
                    if (process == null) return null;
                    
                    process.ErrorDataReceived += (s, e) => { if (e.Data != null) _logger.LogWarning($"[FFmpeg Gen] {e.Data}"); };
                    process.BeginErrorReadLine();

                    if (!process.WaitForExit(30000))
                    {
                        _logger.LogWarning($"[VideoGen] Timeout generating video for {gameName}");
                        try { process.Kill(); } catch { }
                        return null;
                    }

                    if (process.ExitCode != 0)
                    {
                        _logger.LogError($"[VideoGen] FFmpeg failed with code {process.ExitCode}");
                        return null;
                    }
                }

                if (File.Exists(targetPath) && new FileInfo(targetPath).Length > 0)
                {
                    _logger.LogInformation($"[VideoGen] Successfully generated with custom offsets: {targetPath}");
                    return targetPath;
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[VideoGen] Error generating video with offsets: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Hybrid Loader: Use System.Drawing for bitmap formats, fallback to ImageMagick for SVG.
        /// </summary>
        private Bitmap? LoadBitmapWithSvgSupport(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;

            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".svg")
            {
                // Convert SVG to PNG in memory stream using ImageMagick
                try 
                {
                    // Temporary file for conversion output
                    string tempPng = Path.Combine(Path.GetTempPath(), $"dmd_svg_{Guid.NewGuid()}.png");
                    
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = _config.IMPath,
                        Arguments = $"-background none \"{path}\" -resize {_config.DmdWidth}x{_config.DmdHeight} \"{tempPng}\"", 
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };

                    using (var process = Process.Start(startInfo))
                    {
                        if (process == null) return null;
                        process.WaitForExit(5000);
                        if (process.ExitCode == 0 && File.Exists(tempPng))
                        {
                            // Load the converted PNG
                            // Copy to MemoryStream to avoid file locking issues when deleting
                            using var fs = new FileStream(tempPng, FileMode.Open, FileAccess.Read);
                            var ms = new MemoryStream();
                            fs.CopyTo(ms);
                            ms.Position = 0;
                            
                            try { File.Delete(tempPng); } catch {}
                            
                            return new Bitmap(ms);
                        }
                    }
                    _logger.LogError($"Failed to convert SVG: {path}");
                    return null;
                }
                catch (Exception ex)
                {
                     _logger.LogError($"SVG Conversion Error for {path}: {ex.Message}");
                     return null;
                }
            }
            else
            {
                // Native Load
                // Use FileStream to avoid locking the file if possible, or just copy to MemoryStream
                // Bitmap(path) sometimes locks the file.
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var ms = new MemoryStream();
                fs.CopyTo(ms);
                ms.Position = 0;
                return new Bitmap(ms);
            }
        }

        private bool ConvertDmdImage(string source, string target, bool isHardcore = false)
        {
            try
            {
                var w = _config.DmdWidth > 0 ? _config.DmdWidth : 128;
                var h = _config.DmdHeight > 0 ? _config.DmdHeight : 32;

                using (var bitmap = new Bitmap(w, h, PixelFormat.Format32bppArgb))
                using (var g = Graphics.FromImage(bitmap))
                {
                    // EN: Set background color / FR: DÃ©finir la couleur de fond
                    var bgColorStr = _config.MarqueeBackgroundColor;
                    Color bgColor = Color.Black;
                    try 
                    { 
                        if (!string.IsNullOrEmpty(bgColorStr) && !bgColorStr.Equals("None", StringComparison.OrdinalIgnoreCase))
                            bgColor = ColorTranslator.FromHtml(bgColorStr); 
                    } catch { }

                    g.Clear(bgColor);
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.SmoothingMode = SmoothingMode.AntiAlias;

                    using (var srcImg = LoadBitmapWithSvgSupport(source))
                    {
                        if (srcImg != null)
                        {
                            // EN: Calculate best fit / FR: Calculer le meilleur ajustement
                            float ratio = Math.Min((float)w / srcImg.Width, (float)h / srcImg.Height);
                            int targetW = (int)(srcImg.Width * ratio);
                            int targetH = (int)(srcImg.Height * ratio);
                            int targetX = (w - targetW) / 2;
                            int targetY = (h - targetH) / 2;

                            // EN: Draw image / FR: Dessiner l'image
                            g.DrawImage(srcImg, targetX, targetY, targetW, targetH);

                            if (isHardcore)
                            {
                                // EN: Draw Golden Border around the actual image area / FR: Dessiner cadre dorÃ© autour de l'image
                                using (var penGold = new Pen(Color.Gold, 1)) // 1px for DMD is enough
                                {
                                    g.DrawRectangle(penGold, targetX, targetY, targetW - 1, targetH - 1);
                                    
                                    // EN: Internal highlight / FR: Ã‰clat interne
                                    using (var penInner = new Pen(Color.FromArgb(150, Color.LightYellow), 1))
                                    {
                                        g.DrawRectangle(penInner, targetX + 1, targetY + 1, targetW - 3, targetH - 3);
                                    }
                                }
                            }
                        }
                    } // End using srcImg

                    bitmap.Save(target, ImageFormat.Png);
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error converting DMD image {source} (Hardcore={isHardcore}): {ex.Message}");
                return false;
            }
        }

        public string? GenerateMpvAchievementOverlay(string badgePath, string title, string description, int points, bool isHardcore = false, string subFolder = "overlays", bool forceRegenerate = false)
        {
            try
            {
                int w = _config.MarqueeWidth;
                int h = _config.MarqueeHeight;
                if (w <= 0) w = 1920; 
                if (h <= 0) h = 360;

                // EN: Determine subfolder based on Hardcore status if using default "overlays"
                // FR: DÃ©terminer le sous-dossier selon le statut Hardcore si "overlays" par dÃ©faut est utilisÃ©
                if (subFolder == "overlays" && isHardcore)
                {
                    subFolder = "hc_overlays";
                }

                string cacheDir = Path.Combine(_config.CachePath, subFolder);
                if (!Directory.Exists(cacheDir)) Directory.CreateDirectory(cacheDir);

                // Unique filename based on title content to cache it
                string cleanTitle = string.Join("_", title.Split(Path.GetInvalidFileNameChars()));
                string timestamp = forceRegenerate ? $"_{DateTime.Now.Ticks}" : "";
                string targetPath = Path.Combine(cacheDir, $"ach_overlay_{cleanTitle}_{points}_{w}x{h}{timestamp}.png");

                // Reuse cache? (Maybe not if badge changes? Badge is usually stable per achievement)
                if (!forceRegenerate && File.Exists(targetPath)) return targetPath;

                using (var bitmap = new Bitmap(w, h))
                using (var g = Graphics.FromImage(bitmap))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                    g.Clear(Color.Transparent); // Transparent background

                    var overlay = GetOverlayItem("unlock", false);
                    int boxX, boxY, boxWidth, boxHeight;

                    if (overlay != null)
                    {
                        if (!overlay.IsEnabled) return string.Empty; // Skip hidden element
                        boxX = overlay.X;
                        boxY = overlay.Y;
                        boxWidth = overlay.Width;
                        boxHeight = overlay.Height;
                    }
                    else
                    {
                        // EN: Layout Logic - Fallback strategy
                        // FR: Logique de mise en page - StratÃ©gie de secours
                        double aspectRatio = (double)w / (double)h;
                        bool isUltrawide = aspectRatio > 3.0;

                        float boxHeightFactor = isUltrawide ? 0.60f : 0.30f;
                        boxHeight = (int)(h * boxHeightFactor);
                        boxY = (h - boxHeight) / 2;

                        int contentPaddingTemp = boxHeight / 10;
                        int badgeSizeTemp = boxHeight - (contentPaddingTemp * 2);
                        float textScaleFactorTemp = badgeSizeTemp / 150.0f;
                        int titleSizeTemp = Math.Max(12, (int)(32 * textScaleFactorTemp));
                        int descSizeTemp = Math.Max(10, (int)(22 * textScaleFactorTemp));
                        int pointsSizeTemp = Math.Max(10, (int)(24 * textScaleFactorTemp));

                        using (var titleFontTemp = new Font("Segoe UI", titleSizeTemp, FontStyle.Bold, GraphicsUnit.Pixel))
                        using (var descFontTemp = new Font("Segoe UI", descSizeTemp, FontStyle.Regular, GraphicsUnit.Pixel))
                        using (var pointsFontTemp = new Font("Segoe UI", pointsSizeTemp, FontStyle.Bold, GraphicsUnit.Pixel))
                        {
                            var titleMeas = g.MeasureString(title, titleFontTemp);
                            var descMeas = g.MeasureString(description, descFontTemp);
                            var pointsMeas = g.MeasureString($"{points} pts", pointsFontTemp);
                            float maxTextWidth = Math.Max(titleMeas.Width, Math.Max(descMeas.Width, pointsMeas.Width));

                            int minBoxWidth = (int)(w * 0.4f);
                            int maxBoxWidth = (int)(w * 0.9f);
                            int textAreaWidth = (int)maxTextWidth + (contentPaddingTemp * 2);
                            int requiredWidth = badgeSizeTemp + textAreaWidth + (contentPaddingTemp * 3);
                            boxWidth = Math.Clamp(requiredWidth, minBoxWidth, maxBoxWidth);
                            boxX = (w - boxWidth) / 2;
                        }
                    }

                    int contentPadding = boxHeight / 10;
                    int badgeSize = boxHeight - (contentPadding * 2);

                    // EN: Proportional text sizing based on box height (or Config override)
                    // FR: Taille de texte proportionnelle (ou surcharge config)
                    int titleSize, descSize, pointsSize;
                    
                    if (overlay != null && overlay.FontSize > 0)
                    {
                         titleSize = (int)overlay.FontSize;
                         descSize = Math.Max(10, (int)(titleSize * 0.7));
                         pointsSize = Math.Max(10, (int)(titleSize * 0.75));
                    }
                    else
                    {
                        titleSize = Math.Max(12, (int)(boxHeight * 0.20)); 
                        descSize = Math.Max(10, (int)(boxHeight * 0.14));
                        pointsSize = Math.Max(10, (int)(boxHeight * 0.15));
                    }

                    string fontFamily = _config.RAFontFamily;
                    if (string.IsNullOrEmpty(fontFamily)) fontFamily = "Segoe UI";

                    using (var titleFont = new Font(fontFamily, titleSize, FontStyle.Bold, GraphicsUnit.Pixel))
                    using (var descFont = new Font(fontFamily, descSize, FontStyle.Regular, GraphicsUnit.Pixel))
                    using (var pointsFont = new Font(fontFamily, pointsSize, FontStyle.Bold, GraphicsUnit.Pixel))
                    using (var brushWhite = new SolidBrush(Color.White))
                    using (var brushGold = new SolidBrush(GetColorFromHex(overlay?.TextColor ?? "", Color.Gold)))
                    {
                        Color bgColor = Color.FromArgb(220, 30, 30, 30); // Default semi-transparent grey
                        using (var brushBox = new SolidBrush(bgColor))
                        using (var penBorder = new Pen(isHardcore ? Color.Gold : Color.Silver, 2))
                        {
                            if (bgColor.A > 0)
                            {
                                g.FillRectangle(brushBox, boxX, boxY, boxWidth, boxHeight);
                            }
                            g.DrawRectangle(penBorder, boxX, boxY, boxWidth, boxHeight);
                        }

                        // Content positioning
                        int badgeX = boxX + contentPadding;
                        int badgeY = boxY + contentPadding;

                        DrawBadgeWithFrame(g, badgePath, badgeX, badgeY, badgeSize, isHardcore, false);

                        int textX = badgeX + badgeSize + contentPadding;
                        int textWidth = boxWidth - (badgeSize + contentPadding * 3);

                        // Title
                        g.DrawString(title, titleFont, brushGold, new RectangleF(textX, badgeY + (badgeSize * 0.05f), textWidth, badgeSize * 0.4f));

                        // Description
                        g.DrawString(description, descFont, brushWhite, new RectangleF(textX, badgeY + (badgeSize * 0.40f), textWidth, badgeSize * 0.4f));

                        // Points
                        string pointsText = $"{points} pts";
                        g.DrawString(pointsText, pointsFont, brushGold, new RectangleF(textX, badgeY + (badgeSize * 0.75f), textWidth, badgeSize * 0.3f));
                    }
                    bitmap.Save(targetPath, ImageFormat.Png);
                }
                return targetPath;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[ImageConversion] GenerateMpvAchievementOverlay Error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// EN: Generate overlay for Challenge (Timer or Progress)
        /// FR: GÃ©nÃ©rer overlay pour DÃ©fi (Timer ou ProgrÃ¨s)
        /// </summary>
        public string GenerateChallengeOverlay(ChallengeState state, int width, int height, bool isHardcore, bool forceRegenerate = false)
        {
            var fileName = $"challenge_{state.AchievementId}_{DateTime.Now.Ticks}.png";
            string subFolder = isHardcore ? "hc_overlays" : "overlays";
            var tempPath = Path.Combine(_config.CachePath, subFolder, fileName);
            if (!Directory.Exists(Path.GetDirectoryName(tempPath)))
                Directory.CreateDirectory(Path.GetDirectoryName(tempPath)!);
            
            // EN: If forceRegenerate is true, we rely on the unique Ticks to generate a new file.
            // But we can also check if by some chance it exists (unlikely with Ticks)
            if (forceRegenerate && File.Exists(tempPath)) File.Delete(tempPath);

            try
            {
                using (var bmp = new Bitmap(width, height))
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

                    // Design: Semi-transparent box in top-right or top-center?
                    // MPV usually overlays on top-left by default unless configured.
                    // We'll draw a nice box.
                    
                    int boxWidth = 350;
                    int boxHeight = 120; // Increased to fit description
                    int margin = 20;
                    
                    // EN: Use "challenge" overlay item for all types (badge presence handled internally)
                    // FR: Utiliser l'item d'overlay "challenge" pour tous les types (présence de badge gérée en interne)
                    var overlay = GetOverlayItem("challenge", false);
                    int x, y;
                    int finalBoxWidth = boxWidth;
                    int finalBoxHeight = boxHeight;

                    if (overlay != null)
                    {
                        if (!overlay.IsEnabled) return string.Empty;
                        x = overlay.X;
                        y = overlay.Y;
                        finalBoxWidth = overlay.Width;
                        finalBoxHeight = overlay.Height;
                    }
                    else
                    {
                        // Position: Center-Left as requested by user
                        x = margin;
                        y = (height - boxHeight) / 2;
                    }

                    // Background & Border
                    // EN: Use consistent gray background instead of following TextColor (which is often gold)
                    // FR: Utiliser un fond gris cohérent au lieu de suivre TextColor (qui est souvent doré)
                    Color bgColor = Color.FromArgb(180, 40, 40, 40); 
                    
                    // EN: Measure text for dynamic frame expansion
                    // FR: Mesurer le texte pour l'expansion dynamique du cadre
                    string fontFamily = _config.RAFontFamily;
                    if (string.IsNullOrEmpty(fontFamily)) fontFamily = "Arial";

                    int fontSizeTitle, fontSizeDesc, fontSizeStatus;

                    if (overlay != null && overlay.FontSize > 0)
                    {
                        fontSizeTitle = (int)overlay.FontSize;
                        fontSizeDesc = Math.Max(7, (int)(fontSizeTitle * 0.7));
                        fontSizeStatus = Math.Max(9, (int)(fontSizeTitle * 0.8));
                    }
                    else
                    {
                        fontSizeTitle = Math.Max(8, (int)(finalBoxHeight * 0.12));
                        fontSizeDesc = Math.Max(7, (int)(finalBoxHeight * 0.08));
                        fontSizeStatus = Math.Max(9, (int)(finalBoxHeight * 0.14));
                    }

                    string title = state.Title.Length > 25 ? state.Title.Substring(0, 23) + "..." : state.Title;
                    string status = state.Progress;
                    if (state.Type == ChallengeType.Timer || state.Type == ChallengeType.Leaderboard)
                    {
                        var elapsed = DateTime.Now - state.StartTime;
                        status = $"TIME: {(int)elapsed.TotalMinutes:D2}:{elapsed.Seconds:D2}";
                    }
                    else if (string.IsNullOrEmpty(status))
                    {
                        status = "ACTIVE";
                    }

                    using (var fTitle = new Font(fontFamily, fontSizeTitle, FontStyle.Bold))
                    using (var fStatus = new Font(fontFamily, fontSizeStatus, FontStyle.Bold))
                    {
                        var titleSize = g.MeasureString(title, fTitle);
                        var statusSize = g.MeasureString(status, fStatus);
                        
                        // EN: Auto-expand frame if text is too large
                        // FR: Agrandir automatiquement le cadre si le texte est trop grand
                        int badgeSpace = finalBoxHeight; // rough estimate
                        int neededWidth = (int)Math.Max(titleSize.Width, statusSize.Width) + badgeSpace + 30;
                        if (neededWidth > finalBoxWidth)
                        {
                            finalBoxWidth = neededWidth;
                        }
                    }

                    if (bgColor.A > 0)
                    {
                        using (var brush = new SolidBrush(bgColor))
                        {
                            g.FillRectangle(brush, x, y, finalBoxWidth, finalBoxHeight);
                        }
                    }

                    Color borderColor = isHardcore ? Color.Gold : Color.Silver;
                    using (var pen = new Pen(borderColor, 2))
                    {
                        g.DrawRectangle(pen, x, y, finalBoxWidth, finalBoxHeight);
                    }

                    // Badge (Proportional to box height)
                    int badgeMargin = (int)(finalBoxHeight * 0.08); 
                    int badgeSize = finalBoxHeight - (badgeMargin * 2);
                    
                    bool hasBadge = !string.IsNullOrEmpty(state.BadgePath) && File.Exists(state.BadgePath);

                    if (hasBadge)
                    {
                        using (var badgeImg = Image.FromFile(state.BadgePath!))
                        {
                            g.DrawImage(badgeImg, x + badgeMargin, y + badgeMargin, badgeSize, badgeSize);
                        }
                    }
                    else if (state.Type == ChallengeType.Leaderboard)
                    {
                        // EN: Fallback for Leaderboard: Draw Trophy Emoji
                        // FR: Repli pour Leaderboard : Dessiner Emoji Trophée
                        using (var fontBadge = new Font("Segoe UI Emoji", badgeSize * 0.8f, FontStyle.Regular))
                        using (var brushBadge = new SolidBrush(Color.Gold))
                        {
                            // Center emoji
                            var emoji = "🏆";
                            var size = g.MeasureString(emoji, fontBadge);
                            float emX = x + badgeMargin + (badgeSize - size.Width) / 2;
                            float emY = y + badgeMargin + (badgeSize - size.Height) / 2;
                            g.DrawString(emoji, fontBadge, brushBadge, emX, emY);
                        }
                        hasBadge = true; // Treat as having badge for text positioning
                    }

                    int textX = x + badgeMargin + badgeSize + 10;
                    if (!hasBadge) textX = x + margin; // Shift text left if no badge/icon
                    int textWidth = finalBoxWidth - (badgeSize + badgeMargin + 20);

                    using (var fontTitle = new Font(fontFamily, fontSizeTitle, FontStyle.Bold))
                    using (var fontDesc = new Font(fontFamily, fontSizeDesc, FontStyle.Italic))
                    using (var fontStatus = new Font(fontFamily, fontSizeStatus, FontStyle.Bold))
                    using (var brushText = new SolidBrush(Color.White))
                    using (var brushStatus = new SolidBrush(GetColorFromHex(overlay?.TextColor ?? "", Color.Gold)))
                    {
                        // Title
                        g.DrawString(title, fontTitle, brushStatus, textX, y + (int)(finalBoxHeight * 0.1));
                        
                        // Description (Multi-line)
                        if (!string.IsNullOrEmpty(state.Description))
                        {
                            var descRect = new RectangleF(textX, y + (int)(finalBoxHeight * 0.3), textWidth, (int)(finalBoxHeight * 0.45));
                            g.DrawString(state.Description, fontDesc, brushText, descRect);
                        }

                        // Status / Timer
                        g.DrawString(status, fontStatus, brushText, textX, y + (int)(finalBoxHeight * 0.75));
                    }
                    
                    bmp.Save(tempPath, ImageFormat.Png);
                }
                return tempPath;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[ImageConversion] GenerateChallengeOverlay Error: {ex.Message}");
                return string.Empty;
            }
        }

        /// <summary>
        /// EN: Generate DMD frames for Challenge (Static Badge + Text)
        /// FR: GÃ©nÃ©rer frames DMD pour DÃ©fi (Badge statique + Texte)
        /// </summary>
        public List<byte[]> GenerateDmdChallengeFrames(ChallengeState state, int width, int height, bool useGrayscale, string? ribbonPath = null)
        {
             // Debug Log for Leaderboard Issue
             _logger.LogInformation($"[ImageConversion] GenerateDmdChallengeFrames: ID={state.AchievementId}, Type={state.Type}, Progress='{state.Progress}', Ribbon={!string.IsNullOrEmpty(ribbonPath)}");

             var frames = new List<byte[]>();
             try
             {
                 using (var bmp = new Bitmap(width, height))
                 using (var g = Graphics.FromImage(bmp))
                 {
                     g.Clear(Color.Black);
                     g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.SingleBitPerPixelGridFit;
                     if (useGrayscale) g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

                      // 1. Draw Ribbon FIRST (if present) - Full size 128x32 to avoid distortion
                      if (!string.IsNullOrEmpty(ribbonPath) && File.Exists(ribbonPath))
                      {
                          try
                          {
                              using (var ribbonImg = Image.FromFile(ribbonPath))
                              {
                                  g.DrawImage(ribbonImg, 0, 0, width, height);
                              }
                          }
                          catch { }
                      }

                      // Design: Similar to GenerateDmdChallengeImage
                                             bool checkBadge = !string.IsNullOrEmpty(state.BadgePath) && File.Exists(state.BadgePath); string overlayType = checkBadge ? "challenge" : "challenge";
                      var overlay = GetOverlayItem(overlayType, true);

                      int boxX = 0;
                      int boxY = 0;
                      int boxW = width;
                      int boxH = !string.IsNullOrEmpty(ribbonPath) ? height / 2 : height;

                      if (overlay != null && overlay.IsEnabled)
                      {
                          boxX = overlay.X;
                          boxY = overlay.Y;
                          boxW = overlay.Width;
                          boxH = overlay.Height;
                      }

                      // Badge (Left)
                      int badgeSize = boxH; 
                      bool hasBadge = !string.IsNullOrEmpty(state.BadgePath) && File.Exists(state.BadgePath);

                      Color lbColor = GetLeaderboardColor(state.AchievementId);

                      if (hasBadge)
                      {
                          using (var badgeImg = Image.FromFile(state.BadgePath!))
                          {
                              g.DrawImage(badgeImg, boxX, boxY, badgeSize, badgeSize);
                          }
                      }
                      else if (state.Type == ChallengeType.Leaderboard)
                      {
                          // Fallback Trophy for DMD
                          // EN: Increased size to 0.6f for better visibility
                          // FR: Taille augmentée à 0.6f pour une meilleure visibilité
                          float emSize = badgeSize * 0.6f;
                          using (var fontBadge = new Font("Segoe UI Emoji", emSize, FontStyle.Regular))
                          using (var brushBadge = new SolidBrush(lbColor))
                          {
                              var emoji = "🏆";
                              var size = g.MeasureString(emoji, fontBadge);
                              float emX = boxX + (badgeSize - size.Width) / 2;
                              // EN: Manual vertical offset adjustment (+1) to align visually with text
                              // FR: Ajustement manuel de l'offset vertical (+1) pour s'aligner visuellement avec le texte
                              float emY = boxY + (badgeSize - size.Height) / 2 + 1; 
                              g.DrawString(emoji, fontBadge, brushBadge, emX, emY);
                          }
                          hasBadge = true; 
                      }

                       // Text (Right)
                       // EN: Periodic alternation (3s Title / 3s Timer)
                       // FR: Alternance périodique (3s Titre / 3s Timer)
                       var elapsedSinceStart = DateTime.Now - state.StartTime;
                       bool showTitle = state.Type == ChallengeType.Leaderboard && (DateTime.Now.Second % 6 < 3);

                       string text = "";
                       string? titleText = null;

                       if (state.Type == ChallengeType.Timer || state.Type == ChallengeType.Leaderboard)
                       {
                           var elapsed = DateTime.Now - state.StartTime;
                           text = $"{(int)elapsed.TotalMinutes:D2}:{elapsed.Seconds:D2}";
                           if (showTitle) titleText = state.Title ?? "Leaderboard";
                       }
                       else
                       {
                           text = string.IsNullOrEmpty(state.Progress) ? "ACTIVE" : state.Progress;
                       }

                       float tx = boxX + badgeSize + 1;
                       if (!hasBadge) tx = boxX + 2;

                       if (showTitle && !string.IsNullOrEmpty(titleText))
                      {
                          if (string.IsNullOrEmpty(ribbonPath))
                          {
                              using (var fontTitle = new Font("Arial", 6, FontStyle.Bold))
                              using (var fontTimer = new Font("Arial", 9, FontStyle.Bold))
                              using (var brushTitle = new SolidBrush(lbColor))
                              using (var brushTimer = new SolidBrush(lbColor))
                              {
                                  if (titleText.Length > 20) titleText = titleText.Substring(0, 17) + "...";
                                  g.DrawString(titleText, fontTitle, brushTitle, tx, boxY + 2);
                                  g.DrawString(text, fontTimer, brushTimer, tx, boxY + 16);
                              }
                          }
                          else
                          {
                              using (var font = new Font("Arial", 8, FontStyle.Bold))
                              using (var brush = new SolidBrush(lbColor))
                              {
                                  if (titleText.Length > 20) titleText = titleText.Substring(0, 17) + "...";
                                  var size = g.MeasureString(titleText, font);
                                  float ty = boxY + (boxH - size.Height) / 2;
                                  g.DrawString(titleText, font, brush, tx, ty);
                              }
                          }
                      }
                      else
                      {
                          int fontSize = (overlay != null && overlay.FontSize > 0) ? (int)overlay.FontSize : (!string.IsNullOrEmpty(ribbonPath) ? 9 : 10);
                          using (var font = new Font("Arial", fontSize, FontStyle.Bold))
                          using (var brush = new SolidBrush(state.Type == ChallengeType.Leaderboard ? lbColor : Color.White))
                          {
                              var size = g.MeasureString(text, font);
                              if (!hasBadge) tx = boxX + (boxW - size.Width) / 2;
                              float ty = boxY + (boxH - size.Height) / 2;
                              g.DrawString(text, font, brush, tx, ty);
                          }
                      }
                     
                     frames.Add(GetRawDmdBytes(bmp, width, height, useGrayscale));
                 }
                 // Return single frame (static) or multiples if we animated? Static for now is efficient.
                 // Maybe 2 frames to ensure refresh?
             }
             catch (Exception ex)
             {
                 _logger.LogError($"[ImageConversion] GenerateDmdChallengeFrames Error: {ex.Message}");
             }
             return frames;
        }

        /// <summary>
        /// EN: Generate DMD image for Challenge (Static Badge + Text) returning path
        /// FR: GÃ©nÃ©rer image DMD pour DÃ©fi (Badge statique + Texte) retournant chemin
        /// </summary>
        public string GenerateDmdChallengeImage(ChallengeState state, int width, int height, bool useGrayscale, bool isHardcore = false, string? ribbonPath = null)
        {
             var fileName = $"dmd_challenge_{state.AchievementId}_{DateTime.Now.Ticks}.png";
             var folder = isHardcore ? "hc_overlays" : "overlays";
             var tempPath = Path.Combine(_config.CachePath, folder, fileName);
             if (!Directory.Exists(Path.GetDirectoryName(tempPath)))
                 Directory.CreateDirectory(Path.GetDirectoryName(tempPath)!);

             try
             {
                 using (var bmp = new Bitmap(width, height))
                 using (var g = Graphics.FromImage(bmp))
                 {
                     g.Clear(Color.Black);
                     g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.SingleBitPerPixelGridFit;
                     if (useGrayscale) g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

                      // 1. Draw Ribbon FIRST (if present) - Full size 128x32 to avoid distortion
                      // FR: Dessiner le Ruban en PREMIER (si présent) - Taille réelle 128x32 pour éviter la déformation
                      if (!string.IsNullOrEmpty(ribbonPath) && File.Exists(ribbonPath))
                      {
                          try
                          {
                              using (var ribbonImg = Image.FromFile(ribbonPath))
                              {
                                  // EN: The ribbon image generated for DMD is already 128x32 with 16px height content at the bottom.
                                  // EN: Drawing it full-size ensures no distortion.
                                  g.DrawImage(ribbonImg, 0, 0, width, height);
                              }
                          }
                          catch { /* Ignore ribbon load errors */ }
                      }

                       // Design: Static frame with badge and text
                                             bool checkBadge = !string.IsNullOrEmpty(state.BadgePath) && File.Exists(state.BadgePath); string overlayType = checkBadge ? "challenge" : "challenge";
                      var overlay = GetOverlayItem(overlayType, true);
                      int boxX, boxY, boxW, boxH;

                      if (overlay != null && overlay.IsEnabled)
                      {
                          boxX = overlay.X;
                          boxY = overlay.Y;
                          boxW = overlay.Width;
                          boxH = overlay.Height;
                      }
                      else
                      {
                          boxX = 0;
                          boxY = 0;
                          boxW = width;
                          boxH = !string.IsNullOrEmpty(ribbonPath) ? height / 2 : height; // EN: Half height if ribbon present / FR: Demi-hauteur si ruban présent
                      }

                      // Badge (Left)
                      int badgeSize = boxH; 
                      bool hasBadge = !string.IsNullOrEmpty(state.BadgePath) && File.Exists(state.BadgePath);

                      Color lbColor = GetLeaderboardColor(state.AchievementId);

                      if (hasBadge)
                      {
                          using (var badgeImg = Image.FromFile(state.BadgePath!))
                          {
                              g.DrawImage(badgeImg, boxX, boxY, badgeSize, badgeSize);
                          }
                      }
                      else if (state.Type == ChallengeType.Leaderboard)
                      {
                          // Fallback Trophy for DMD
                          // EN: Increased size to 0.6f as requested for better visibility
                          // FR: Taille augmentée à 0.6f pour une meilleure visibilité
                          float emSize = badgeSize * 0.6f;
                          using (var fontBadge = new Font("Segoe UI Emoji", emSize, FontStyle.Regular))
                          using (var brushBadge = new SolidBrush(lbColor))
                          {
                              var emoji = "🏆";
                              var size = g.MeasureString(emoji, fontBadge);
                              float emX = boxX + (badgeSize - size.Width) / 2;
                              float emY = boxY + (badgeSize - size.Height) / 2 + 1; 
                              g.DrawString(emoji, fontBadge, brushBadge, emX, emY);
                          }
                          hasBadge = true;
                      }

                      // Text (Right)
                      // EN: Periodic alternation (3s Title / 3s Timer) to ensure all names are seen in rotation
                      // FR: Alternance périodique (3s Titre / 3s Timer) pour assurer que tous les noms sont vus durant la rotation
                      var elapsedSinceStart = DateTime.Now - state.StartTime;
                      bool showTitle = state.Type == ChallengeType.Leaderboard && (DateTime.Now.Second % 6 < 3);

                      string text = "";
                      string? titleText = null;

                      if (state.Type == ChallengeType.Timer || state.Type == ChallengeType.Leaderboard)
                      {
                          var elapsed = DateTime.Now - state.StartTime;
                          text = $"{(int)elapsed.TotalMinutes:D2}:{elapsed.Seconds:D2}";
                          if (showTitle) titleText = state.Title ?? "Leaderboard";
                      }
                      else
                      {
                          text = string.IsNullOrEmpty(state.Progress) ? "ACTIVE" : state.Progress;
                      }

                      // Dynamic Border Color based on Hardcore
                      Color borderColor = isHardcore ? Color.Gold : Color.Silver;
                      
                      float tx = boxX + badgeSize + 1; // EN: Moved closer to badge
                      if (!hasBadge) tx = boxX + 2;

                       if (showTitle && !string.IsNullOrEmpty(titleText))
                      {
                          // EN: 2-line layout (Only if no ribbon, otherwise it overflows)
                          // FR: Layout sur 2 lignes (uniquement si pas de ruban, sinon ça déborde)
                          if (string.IsNullOrEmpty(ribbonPath))
                          {
                              using (var fontTitle = new Font("Arial", 6, FontStyle.Bold))
                              using (var fontTimer = new Font("Arial", 9, FontStyle.Bold))
                              using (var brushTitle = new SolidBrush(lbColor))
                              using (var brushTimer = new SolidBrush(lbColor))
                              {
                                  if (titleText.Length > 20) titleText = titleText.Substring(0, 17) + "...";
                                  g.DrawString(titleText, fontTitle, brushTitle, tx, boxY + 2);
                                  g.DrawString(text, fontTimer, brushTimer, tx, boxY + 16);
                              }
                          }
                          else
                          {
                              // EN: Ribbon present: Show Title ONLY during initial phase in top 16px
                              // FR: Ruban présent : Afficher le TITRE UNIQUEMENT pendant la phase initiale dans les 16px du haut
                              using (var font = new Font("Arial", 8, FontStyle.Bold))
                              using (var brush = new SolidBrush(lbColor))
                              {
                                  if (titleText.Length > 20) titleText = titleText.Substring(0, 17) + "...";
                                  var size = g.MeasureString(titleText, font);
                                  float ty = boxY + (boxH - size.Height) / 2;
                                  g.DrawString(titleText, font, brush, tx, ty);
                              }
                          }
                      }
                      else
                      {
                          // EN: Standard centered layout
                          // FR: Layout centré standard
                          // EN: Use smaller font if ribbon is present to ensure fit in 16px
                          int fontSize = (overlay != null && overlay.FontSize > 0) ? (int)overlay.FontSize : (!string.IsNullOrEmpty(ribbonPath) ? 9 : 10);
                          using (var font = new Font("Arial", fontSize, FontStyle.Bold))
                          using (var brush = new SolidBrush(state.Type == ChallengeType.Leaderboard ? lbColor : borderColor))
                          {
                              var size = g.MeasureString(text, font);
                              if (!hasBadge) tx = boxX + (boxW - size.Width) / 2;
                              
                              float ty = boxY + (boxH - size.Height) / 2;
                              g.DrawString(text, font, brush, tx, ty);
                          }
                      }
                      
                      bmp.Save(tempPath, ImageFormat.Png);
                 }
                 return tempPath;
             }
             catch (Exception ex)
             {
                 _logger.LogError($"[ImageConversion] GenerateDmdChallengeImage Error: {ex.Message}");
                 return string.Empty;
             }
        }

        private Color GetLeaderboardColor(int id)
        {
            // EN: Generate various shades of yellow/orange based on ID
            // FR: Générer différentes nuances de jaune/orange basées sur l'ID
            var colors = new[] 
            { 
                Color.Gold, 
                Color.Orange, 
                Color.DarkOrange, 
                Color.Goldenrod, 
                Color.Yellow, 
                Color.OrangeRed,
                Color.Khaki
            };
            int index = Math.Abs(id.GetHashCode() % colors.Length);
            return colors[index];
        }

        public List<byte[]> GenerateDmdScrollingTextFrames(string text, int width, int height, bool useGrayscale, int yOffset = 0, int? targetHeight = null, string? textColor = null, float? fontSizeParam = null)
        {
            if (string.IsNullOrEmpty(text)) return new List<byte[]>();

            // EN: Generate cache key based on parameters
            // FR: Générer une clé de cache basée sur les paramètres
            string cacheKey = $"{text}_{width}_{height}_{useGrayscale}_{yOffset}_{targetHeight}_{textColor}_{fontSizeParam}";
            lock (_dmdFramesCache)
            {
                if (_dmdFramesCache.TryGetValue(cacheKey, out var cachedFrames))
                {
                    _logger.LogDebug($"[ImageConversion] Returning cached DMD scrolling frames for: {text}");
                    return cachedFrames;
                }
            }

            var frames = new List<byte[]>();
            try
            {
                int drawHeight = targetHeight ?? height;
                using (var measureBmp = new Bitmap(1, 1))
                using (var gMeasure = Graphics.FromImage(measureBmp))
                {
                    gMeasure.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
                    
                    float fontSize = fontSizeParam > 0 ? fontSizeParam.Value : (drawHeight <= 16 ? 10 : 12);
                    Font font;
                    try { font = new Font("Segoe UI Emoji", fontSize, FontStyle.Bold); }
                    catch { font = new Font("Arial", fontSize - 2, FontStyle.Bold); }

                    using (font)
                    {
                        var textSize = gMeasure.MeasureString(text, font);
                        int textTotalWidth = (int)textSize.Width + 20; 
                        int bmpWidth = Math.Max(width, textTotalWidth + width);

                        using (var fullTextBmp = new Bitmap(bmpWidth, drawHeight))
                        using (var gText = Graphics.FromImage(fullTextBmp))
                        {
                            gText.Clear(Color.Transparent); // Allow persistent layer to show through
                            gText.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit; 
                            gText.SmoothingMode = SmoothingMode.AntiAlias;
                            
                            Color drawColor = Color.Gold;
                            if (!string.IsNullOrEmpty(textColor))
                            {
                                try { drawColor = ColorTranslator.FromHtml(textColor); } catch { }
                            }

                            using (var brush = new SolidBrush(drawColor))
                            {
                                float y = (drawHeight - textSize.Height) / 2;
                                gText.DrawString(text, font, brush, width, y + 1);
                            }

                            int maxScroll = textTotalWidth + width; 
                            for (int x = 0; x < maxScroll; x += 2) // Step 2 for faster scroll 
                            {
                                using (var frameBmp = new Bitmap(width, height))
                                using (var gFrame = Graphics.FromImage(frameBmp))
                                {
                                    gFrame.Clear(Color.Transparent);
                                    gFrame.DrawImage(fullTextBmp, new Rectangle(0, yOffset, width, drawHeight), new Rectangle(x, 0, width, drawHeight), GraphicsUnit.Pixel);
                                    frames.Add(GetRawDmdBytes(frameBmp, width, height, useGrayscale));
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[ImageConversion] GenerateDmdScrollingTextFrames Error: {ex.Message}");
            }
            lock (_dmdFramesCache)
            {
                // EN: Limit cache size to prevent memory bloat (keep last 20 unique scrolls)
                if (_dmdFramesCache.Count > 20) _dmdFramesCache.Clear();
                _dmdFramesCache[cacheKey] = frames;
            }
            return frames;
        }


        public byte[] GenerateDmdStaticTextFrame(string text, int width, int height, bool useGrayscale, int yOffset = 0, int? targetHeight = null)
        {
            try
            {
                int drawHeight = targetHeight ?? height;
                using (var bitmap = new Bitmap(width, height)) // The final bitmap is full height
                using (var g = Graphics.FromImage(bitmap))
                {
                    g.Clear(Color.Black);
                    g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
                    
                    // EN: Use a smaller font if we are in split mode (half height)
                    // FR: Utiliser une police plus petite si on est en mode divisÃ© (demi hauteur)
                    float fontSize = drawHeight <= 16 ? 10 : 12;
                    Font font;
                    try { font = new Font("Segoe UI Emoji", fontSize, FontStyle.Bold); }
                    catch { font = new Font("Arial", fontSize - 2, FontStyle.Bold); }

                    using (font)
                    {
                        var textSize = g.MeasureString(text, font);
                        float x = (width - textSize.Width) / 2;
                        // Position vertically within the targetHeight, then offset by yOffset
                        float y = yOffset + (drawHeight - textSize.Height) / 2;
                        
                        using (var brush = new SolidBrush(Color.Gold))
                        {
                            g.DrawString(text, font, brush, x, y + 1);
                        }
                    }
                    return GetRawDmdBytes(bitmap, width, height, useGrayscale);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[ImageConversion] GenerateDmdStaticTextFrame Error: {ex.Message}");
                return new byte[0];
            }
        }

        public byte[] GenerateDmdPersistentScoreFrame(string scoreText, int width, int height, bool useGrayscale)
        {
            try
            {
                using (var bitmap = new Bitmap(width, height))
                using (var g = Graphics.FromImage(bitmap))
                {
                    g.Clear(Color.Black);
                    g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
                    
                    // Specific Small Font for Score
                    Font font;
                    try { font = new Font("Arial", 8, FontStyle.Bold); }
                    catch { font = new Font(FontFamily.GenericSansSerif, 8, FontStyle.Bold); }

                    using (font)
                    {
                        var textSize = g.MeasureString(scoreText, font);
                        var overlay = GetOverlayItem("score", true);
                        float x, y;
                        
                        if (overlay != null && overlay.IsEnabled)
                        {
                            x = overlay.X + (overlay.Width - textSize.Width) / 2;
                            y = overlay.Y + (overlay.Height - textSize.Height) / 2;
                        }
                        else
                        {
                            // Position: Top-Right with 2px margin
                            x = width - textSize.Width - 2;
                            y = 1;
                        }
                        
                        using (var brush = new SolidBrush(Color.Gold))
                        {
                            // EN: Nudge text down slightly for visual centering
                            float verticalNudge = 1.0f;
                            g.DrawString(scoreText, font, brush, x, y + verticalNudge);
                        }
                    }
                    return GetRawDmdBytes(bitmap, width, height, useGrayscale);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[ImageConversion] GenerateDmdPersistentScoreFrame Error: {ex.Message}");
                return new byte[0];
            }
        }

        /// <summary>
        /// Generates a composite image for DMD (128x32 usually)
        /// Fanart + Logo with Resize
        /// </summary>
        public string ProcessDmdComposition(string fanartPath, string logoPath, string subFolder, int offsetX = 0, int offsetY = 0, int logoOffsetX = 0, int logoOffsetY = 0, bool isPreview = false, bool forceRegenerate = false)
        {
            try
            {
                var w = _config.DmdWidth;
                var h = _config.DmdHeight;
                
                string dmdCacheDir = Path.Combine(_config.CachePath, "dmd");
                if (!string.IsNullOrEmpty(subFolder)) dmdCacheDir = Path.Combine(dmdCacheDir, subFolder);
                if (!Directory.Exists(dmdCacheDir)) Directory.CreateDirectory(dmdCacheDir);

                var logoName = Path.GetFileNameWithoutExtension(logoPath);
                // EN: Use simple fixed filename (offsets stored in offsets.json)
                // FR: Utiliser un nom de fichier simple fixe (offsets stockÃ©s dans offsets.json)
                string uniqueName = $"{logoName}_composed.png";

                if (isPreview) uniqueName = $"preview_dmd_{logoName}_{DateTime.Now.Ticks}.png";

                string targetPath = Path.Combine(dmdCacheDir, uniqueName);
                if (!forceRegenerate && !isPreview && File.Exists(targetPath))
                {
                    _logger.LogInformation($"[DMD CACHE REUSE] Found existing: {uniqueName}");
                    return targetPath;
                }

                _logger.LogInformation($"Generating DMD composition (Fanart Off: {offsetX},{offsetY}) [Preview:{isPreview}]: {targetPath}");

                var layout = _config.MarqueeLayout.ToLowerInvariant();
                var logoH = (int)(h * 0.9);
                
                var args = new List<string>
                {
                    "-density", "96",
                    "-size", $"{w}x{h}", "xc:black", // Opaque black canvas (matches Python)
                    "-units", "PixelsPerInch"
                };

                // 1. Fanart Layer + Dark Overlay
                if (!string.IsNullOrEmpty(fanartPath) && File.Exists(fanartPath))
                {
                    args.Add("(");
                    args.Add(fanartPath);
                    args.Add("-resize"); args.Add($"{w}x{h}^");
                    args.Add("-gravity"); args.Add("Center");
                    args.Add("-extent"); args.Add($"{w}x{h}");
                    args.Add(")");
                    args.Add("-gravity"); args.Add("Center");
                    args.Add("-geometry"); args.Add($"{(offsetX >= 0 ? "+" : "")}{offsetX}{(offsetY >= 0 ? "+" : "")}{offsetY}");
                    args.Add("-composite");

                    // Dark overlay
                    args.Add("(");
                    args.Add("-size"); args.Add($"{w}x{h}");
                    args.Add("xc:black");
                    args.Add("-alpha"); args.Add("set");
                    args.Add("-channel"); args.Add("A");
                    args.Add("-evaluate"); args.Add("set"); args.Add("50%");
                    args.Add(")");
                    args.Add("-gravity"); args.Add("Center");
                    args.Add("-composite");
                }

                // 2. Gradient Layer
                if (layout.Contains("gradient"))
                {
                    if (layout == "gradient-left")
                    {
                        args.Add("(");
                        args.Add("-size"); args.Add($"{h}x{w}");
                        args.Add("gradient:black-none");
                        args.Add("-rotate"); args.Add("-90");
                        args.Add(")");
                        args.Add("-gravity"); args.Add("Center");
                        args.Add("-composite");
                    }
                    else if (layout == "gradient-right")
                    {
                        args.Add("(");
                        args.Add("-size"); args.Add($"{h}x{w}");
                        args.Add("gradient:none-black");
                        args.Add("-rotate"); args.Add("-90");
                        args.Add(")");
                        args.Add("-gravity"); args.Add("Center");
                        args.Add("-composite");
                    }
                    else if (layout == "gradient-standard")
                    {
                        var halfW = w / 2;
                        var remainderW = w - halfW;

                        args.Add("(");
                        args.Add("(");
                        args.Add("-size"); args.Add($"{h}x{halfW}");
                        args.Add("gradient:black-none");
                        args.Add("-rotate"); args.Add("-90");
                        args.Add(")");
                        args.Add("(");
                        args.Add("-size"); args.Add($"{h}x{remainderW}");
                        args.Add("gradient:none-black");
                        args.Add("-rotate"); args.Add("-90");
                        args.Add(")");
                        args.Add("+append");
                        args.Add(")");
                        args.Add("-gravity"); args.Add("Center");
                        args.Add("-composite");
                    }
                }

                // 3. Logo Layer (with transparent background)
                if (File.Exists(logoPath))
                {
                    var logoW = (int)(w * 0.9);
                    args.Add("(");
                    args.Add("-background"); args.Add("none"); // CRITICAL: Transparent background for SVG/PNG
                    args.Add(logoPath);
                    args.Add("-resize"); args.Add($"{logoW}x{logoH}>");
                    args.Add(")");

                    if (layout == "gradient-left")
                    {
                        args.Add("-gravity"); args.Add("West");
                        // Base padding +2, plus offset
                        int fx = 2 + logoOffsetX;
                        int fy = 0 + logoOffsetY;
                        args.Add("-geometry"); args.Add($"{(fx >= 0 ? "+" : "")}{fx}{(fy >= 0 ? "+" : "")}{fy}");
                    }
                    else if (layout == "gradient-right")
                    {
                        args.Add("-gravity"); args.Add("East");
                        int fx = 2 + logoOffsetX; // Normally logic might invert X for East? Let's assume standard offset adds to X.
                        int fy = 0 + logoOffsetY;
                        args.Add("-geometry"); args.Add($"{(fx >= 0 ? "+" : "")}{fx}{(fy >= 0 ? "+" : "")}{fy}");
                    }
                    else
                    {
                        args.Add("-gravity"); args.Add("Center");
                        int fx = 0 + logoOffsetX;
                        int fy = 0 + logoOffsetY;
                        args.Add("-geometry"); args.Add($"{(fx >= 0 ? "+" : "")}{fx}{(fy >= 0 ? "+" : "")}{fy}");
                    }

                    args.Add("-composite");
                }

                args.Add(targetPath);

                var startInfo = new ProcessStartInfo
                {
                    FileName = _config.IMPath,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(_config.IMPath)
                };

                foreach (var arg in args) startInfo.ArgumentList.Add(arg);

                using var process = Process.Start(startInfo);
                if (process == null) return logoPath;

                process.WaitForExit(10000);

                if (WaitForFile(targetPath))
                {
                    return targetPath;
                }

                if (process.ExitCode != 0)
                {
                    _logger.LogError($"ImageMagick DMD Composition Error: {process.StandardError.ReadToEnd()}");
                    return logoPath;
                }

                return File.Exists(targetPath) ? targetPath : logoPath;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error in ProcessDmdComposition: {ex.Message}");
                return logoPath;
            }
        }
        
        public string GenerateComposition(string fanartPath, string logoPath, string subFolder, int offsetX = 0, int offsetY = 0, int logoOffsetX = 0, int logoOffsetY = 0, double fanartScale = 1.0, double logoScale = 1.0, bool isPreview = false)
        {
            if (string.IsNullOrEmpty(fanartPath) || !File.Exists(fanartPath)) return logoPath;
            if (string.IsNullOrEmpty(logoPath) || !File.Exists(logoPath)) return fanartPath; 
            
            // Wait for inputs to be ready
            if (!WaitForFile(fanartPath) || !WaitForFile(logoPath))
            {
                _logger.LogWarning("Composition inputs not ready/readable. Returning logo path.");
                return logoPath;
            } 

            string cacheDir = _config.CachePath;
             if (!string.IsNullOrEmpty(subFolder))
            {
                cacheDir = Path.Combine(_config.CachePath, subFolder);
            }
             if (!Directory.Exists(cacheDir)) Directory.CreateDirectory(cacheDir);

            string fileName = Path.GetFileNameWithoutExtension(logoPath);
            string targetPath;
            
            if (isPreview)
            {
                // Preview Mode: Use Circular Buffer (Max 3 slots) to prevent cache flooding
                targetPath = GetPreviewSlot(cacheDir, fileName);
            }
            else
            {
                // EN: Permanent Mode - Use simple fixed filename with offset metadata validation
                // FR: Mode permanent - Utiliser un nom de fichier simple fixe avec validation des mÃ©tadonnÃ©es d'offsets
                string simpleName = $"{fileName}_composed.png";
                targetPath = Path.Combine(cacheDir, simpleName);
                
                if (File.Exists(targetPath))
                {
                    // EN: Validate cached file has same offsets as current request
                    // FR: Valider que le fichier en cache a les mÃªmes offsets que la requÃªte actuelle
                    bool offsetsMatch = ValidateOffsetMetadata(targetPath, offsetX, offsetY, fanartScale, logoOffsetX, logoOffsetY, logoScale);
                    
                    if (offsetsMatch)
                    {
                        _logger.LogInformation($"[CACHE REUSE] Found existing with matching offsets: {simpleName}");
                        return targetPath;
                    }
                    else
                    {
                        // EN: Offsets changed - Delete old cache and metadata, regenerate
                        // FR: Offsets changÃ©s - Supprimer ancien cache et mÃ©tadonnÃ©es, regÃ©nÃ©rer
                        _logger.LogInformation($"[CACHE INVALID] Offsets changed, deleting old cache: {simpleName}");
                        try
                        {
                            File.Delete(targetPath);
                            var metadataPath = Path.ChangeExtension(targetPath, ".json");
                            if (File.Exists(metadataPath)) File.Delete(metadataPath);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning($"[CACHE INVALID] Failed to delete old cache: {ex.Message}");
                        }
                    }
                }
                
                _logger.LogInformation($"[CACHE NEW] Creating: {simpleName}");
            }

            try
            {
                var w = _config.MarqueeWidth;
                var h = _config.MarqueeHeight;
                var bg = _config.MarqueeBackgroundColor; // Use configured background color
                var logoH = (int)(h * 0.9);
                
                // Canvas approach for full control of Fanart placement (Offset)
                // 1. Create Canvas with configured background color
                // 2. Composite Fanart (Resized) at Center + Offset
                // 3. (Optional) Composite Gradient
                // 4. Composite Logo (Resized) at Center + Logo Offset (or Auto Gravity)

                var layout = _config.MarqueeLayout.ToLowerInvariant();
                
                // Apply fanart with scale
                var fanartW = (int)(w * fanartScale);
                var args = new List<string>
                {
                    "-size", $"{w}x{h}", $"xc:{bg}", // Base Canvas
                    
                    "(", 
                    fanartPath, 
                    "-resize", $"{fanartW}x",              
                    ")",
                    "-gravity", "Center",
                    "-geometry", $"{ (offsetX >= 0 ? "+" : "") }{offsetX}{ (offsetY >= 0 ? "+" : "") }{offsetY}",
                    "-composite"
                };

                // Gradient Logic
                // Gradient Logic
                if (layout == "gradient-left")
                {
                    // Goal: Black on Left -> Transparent on Right
                    // We generate a Vertical Gradient (Top->Bottom) and rotate it -90 degrees.
                    // Vertical Size must be swapped: H x W so that after 90deg rotation it becomes W x H.
                    
                    // gradient:black-none means Top=Black, Bottom=None
                    // Rotate -90: Top becomes Left (Black), Bottom becomes Right (None).
                    
                    args.Add("(");
                    args.Add("-size"); args.Add($"{h}x{w}"); // Swapped dimensions for rotation
                    args.Add("gradient:black-none");
                    args.Add("-rotate"); args.Add("-90");
                    args.Add(")"); 
                    args.Add("-gravity"); args.Add("Center");
                    args.Add("-composite");
                }
                else if (layout == "gradient-right")
                {
                     // Goal: Transparent on Left -> Black on Right
                     // gradient:none-black means Top=None, Bottom=Black
                     // Rotate -90: Top becomes Left (None), Bottom becomes Right (Black).
                     
                    args.Add("(");
                    args.Add("-size"); args.Add($"{h}x{w}"); // Swapped dimensions for rotation
                    args.Add("gradient:none-black");
                    args.Add("-rotate"); args.Add("-90");
                    args.Add(")"); 
                    args.Add("-gravity"); args.Add("Center");
                    args.Add("-composite");
                }
                else if (layout == "gradient-standard")
                {
                     // Bilateral Gradient: Black Left -> Transparent Center -> Black Right
                     // We construct this by appending two gradients horizontally.
                     // Part 1 (Left Half): Black->None. (Rotated 90 Left)
                     // Part 2 (Right Half): None->Black. (Rotated 90 Left)
                     
                     var halfW = w / 2;
                     var remainderW = w - halfW;

                     args.Add("(");
                        // Left Half
                        args.Add("(");
                        args.Add("-size"); args.Add($"{h}x{halfW}"); // H x HalfW (Swapped)
                        args.Add("gradient:black-none");
                        args.Add("-rotate"); args.Add("-90");
                        args.Add(")");
                        
                        // Right Half
                        args.Add("(");
                        args.Add("-size"); args.Add($"{h}x{remainderW}"); // H x RemainderW (Swapped)
                        args.Add("gradient:none-black");
                        args.Add("-rotate"); args.Add("-90");
                        args.Add(")");

                        // Join them
                        args.Add("+append");
                     args.Add(")");
                     
                     args.Add("-gravity"); args.Add("Center");
                     args.Add("-composite");
                }

                // Logo Logic with scale
                var logoW = (int)(w * 0.9 * logoScale); // Max 90% of canvas width * scale
                var scaledLogoH = (int)(logoH * logoScale);
                args.Add("(");
                args.Add("-background"); args.Add("none");
                args.Add(logoPath);
                args.Add("-resize"); args.Add($"{logoW}x{scaledLogoH}"); // Fit within bounds (allow both shrink and enlarge)
                args.Add(")");

                // Position Logo based on Layout
                if (layout == "gradient-left")
                {
                    args.Add("-gravity"); args.Add("West");
                    args.Add("-geometry"); args.Add("+20+0"); // Slight padding from left edge
                }
                else if (layout == "gradient-right")
                {
                    args.Add("-gravity"); args.Add("East");
                    args.Add("-geometry"); args.Add("+20+0"); // Slight padding from right edge
                }
                else
                {
                    // EN: Use NorthWest for video offset preview (absolute coords like FFmpeg), Center for normal image editing
                    // FR: Utiliser NorthWest pour preview offset vidÃ©o (coords absolues comme FFmpeg), Center pour Ã©dition image normale  
                    // Video mode uses "video_preview" subfolder, image mode uses system subfolder (e.g., "gw")
                    string logoGravity = (subFolder == "video_preview") ? "NorthWest" : "Center";
                    args.Add("-gravity"); args.Add(logoGravity);
                    args.Add("-geometry"); args.Add($"{ (logoOffsetX >= 0 ? "+" : "") }{logoOffsetX}{ (logoOffsetY >= 0 ? "+" : "") }{logoOffsetY}");
                }
                
                args.Add("-composite");

                args.Add("-strip");
                args.Add("-depth"); args.Add("8");
                args.Add("-colorspace"); args.Add("sRGB");
                
                // Atomic Write: Output to a temporary file first, then move to target
                // This prevents MPV from reading a partial/corrupt file while ImageMagick is writing.
                string tempPath = targetPath + ".tmp_" + DateTime.Now.Ticks + ".png";

                args.Add(tempPath);

                _logger.LogInformation($"Generating composition (Fanart Off: {offsetX},{offsetY} Scale: {fanartScale:F2} | Logo Off: {logoOffsetX},{logoOffsetY} Scale: {logoScale:F2}) [Preview:{isPreview}]: {targetPath}");

                var startInfo = new ProcessStartInfo
                {
                    FileName = _config.IMPath,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(_config.IMPath)
                };
                
                 foreach(var arg in args) startInfo.ArgumentList.Add(arg);

                using var process = Process.Start(startInfo);
                if (process == null) return logoPath; 

                process.WaitForExit(15000);
                
                if (WaitForFile(tempPath))
                {
                    // Atomic move: Copy temp to target (overwrite), then delete temp
                    try
                    {
                        File.Copy(tempPath, targetPath, true);
                        File.Delete(tempPath);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"Atomic Move Failed: {ex.Message}");
                        return logoPath;
                    }

                    // Clean up old permanent files when using preview mode (hotkeys)
                    // Clean up old permanent files when using preview mode (hotkeys)
                    // EN: Remove old composed files to prevent cache pollution
                    // FR: Supprimer les anciens fichiers composÃ©s pour Ã©viter la pollution du cache
                    if (isPreview)
                    {
                        try
                        {
                            // Pattern: filename_composed_*.png
                            var pattern = $"{fileName}_composed_*.png";
                            var directory = Path.GetDirectoryName(targetPath);
                            if (!string.IsNullOrEmpty(directory))
                            {
                                var oldFiles = Directory.GetFiles(directory, pattern);
                                foreach (var oldFile in oldFiles)
                                {
                                    try
                                    {
                                        File.Delete(oldFile);
                                        _logger.LogDebug($"Deleted old permanent cache file: {Path.GetFileName(oldFile)}");
                                    }
                                    catch { } // Silent fail - non-critical
                                }
                            }
                        }
                        catch { } // Silent fail - non-critical
                    }
                    
                    // EN: Save offset metadata for permanent files (enables cache validation on next load)
                    // FR: Sauvegarder les mÃ©tadonnÃ©es d'offsets pour les fichiers permanents (permet validation du cache au prochain chargement)
                    if (!isPreview)
                    {
                        SaveOffsetMetadata(targetPath, offsetX, offsetY, fanartScale, logoOffsetX, logoOffsetY, logoScale);
                    }
                    
                    return targetPath;
                }
                
                if (process.ExitCode != 0)
                {
                     _logger.LogError($"ImageMagick Composition Error: {process.StandardError.ReadToEnd()}");
                     return logoPath;
                }

                return File.Exists(targetPath) ? targetPath : logoPath;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error generating composition: {ex.Message}");
                return logoPath;
            }
        }
        private string GetPreviewSlot(string cacheDir, string fileName)
        {
            // Circular buffer of 3 files
            for (int i = 0; i < 3; i++)
            {
                var path = Path.Combine(cacheDir, $"{fileName}_preview_{i}.png");
                if (!File.Exists(path)) return path;

                try 
                {
                    // Check if we can write/delete
                    File.Delete(path); 
                    return path;
                }
                catch 
                { 
                    // Locked, try next
                }
            }
            // Fallback if all locked (unlikely with just 1 MPV instance)
            return Path.Combine(cacheDir, $"{fileName}_preview_overflow_{DateTime.Now.Ticks % 100}.png");
        }

        /// <summary>
        /// Waits for a file to be accessible (not locked) and stable (size > 0).
        /// Retries for up to 2 seconds.
        /// </summary>
        private bool WaitForFile(string path)
        {
            if (!File.Exists(path)) return false;
            
            for (int i = 0; i < 10; i++)
            {
                try
                {
                    using (var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        if (stream.Length > 0) return true;
                    }
                }
                catch (IOException)
                {
                    // File is locked, wait and retry
                    System.Threading.Thread.Sleep(200);
                }
            }
            return false;
        }

        public async Task<byte[]> GetRawDmdBytes(string imagePath, int width, int height, bool grayscale = true)
        {
            if (!File.Exists(imagePath)) return Array.Empty<byte>();

            return await Task.Run(() => 
            {
                try
                {
                    if (!WaitForFile(imagePath)) return Array.Empty<byte>();

                    using var fs = new FileStream(imagePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var originalBitmap = new Bitmap(fs); 
                    return GetRawDmdBytes(originalBitmap, width, height, grayscale);
                }
                catch (Exception ex)
                {
                    _logger.LogError($"GetRawDmdBytes file error: {ex.Message}");
                    return Array.Empty<byte>();
                }
            });
        }

        public byte[] GetRawDmdBytes(Bitmap originalInfo, int width, int height, bool grayscale = true)
        {
            try
            {
                Bitmap resized;
                bool needsDispose = false;

                // Only resize if necessary to avoid any interpolation artifacts
                if (originalInfo.Width == width && originalInfo.Height == height)
                {
                    // Image is already the correct size, use it directly
                    // Note: originalInfo might be a frame from a multi-frame image, 
                    // so we should probably clone it if we want to be safe, but typically it's fine.
                    resized = originalInfo; 
                }
                else
                {
                    // Resize with aspect ratio preservation (Contain)
                    resized = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                    needsDispose = true;
                    using (var g = Graphics.FromImage(resized))
                    {
                        g.Clear(Color.Black); // Background for DMD
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic; // Better for downscaling
                        g.PixelOffsetMode = PixelOffsetMode.Half;
                        g.SmoothingMode = SmoothingMode.AntiAlias;

                        // Calculate Aspect Ratio
                        float scale = Math.Min((float)width / originalInfo.Width, (float)height / originalInfo.Height);
                        int newW = (int)(originalInfo.Width * scale);
                        int newH = (int)(originalInfo.Height * scale);
                        int posX = (width - newW) / 2;
                        int posY = (height - newH) / 2;

                        g.DrawImage(originalInfo, posX, posY, newW, newH);
                    }
                }

                try
                {
                    // Extract bytes
                    var result = new byte[width * height * (grayscale ? 1 : 3)];
                    
                    var data = resized.LockBits(
                        new Rectangle(0, 0, width, height), 
                        ImageLockMode.ReadOnly, 
                        PixelFormat.Format32bppArgb);

                    int pixelSize = 4; // ARGB
                    byte[] buffer = new byte[data.Stride * height];
                    Marshal.Copy(data.Scan0, buffer, 0, buffer.Length);
                    resized.UnlockBits(data);

                    int outIdx = 0;
                    for (int y = 0; y < height; y++)
                    {
                        for (int x = 0; x < width; x++)
                        {
                            int inIdx = (y * data.Stride) + (x * pixelSize);
                            
                            // B, G, R, A (ARGB format)
                            byte b = buffer[inIdx];
                            byte g = buffer[inIdx + 1];
                            byte r = buffer[inIdx + 2];
                            byte a = buffer[inIdx + 3];
                            
                            // CRITICAL: Handle transparency by blending against a black background
                            // This simulates semi-transparency on the DMD
                            float alpha = a / 255.0f;
                            
                            // Blend against black (0,0,0): Result = Source * Alpha + Background * (1 - Alpha)
                            // Since Background is 0, Result = Source * Alpha
                            r = (byte)(r * alpha);
                            g = (byte)(g * alpha);
                            b = (byte)(b * alpha);

                            if (grayscale)
                            {
                                // Rec. 709 Weights (Modern Standard)
                                // Attenduate slightly (0.85) to avoid "blinding whites" on physical DMDs
                                byte gray = (byte)((r * 0.2126 + g * 0.7152 + b * 0.0722) * 0.85);
                                result[outIdx++] = gray;
                            }
                            else
                            {
                                // RGB
                                result[outIdx++] = r;
                                result[outIdx++] = g;
                                result[outIdx++] = b;
                            }
                        }
                    }

                    return result;
                }
                finally
                {
                    if (needsDispose) resized.Dispose();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"GetRawDmdBytes conversion error: {ex.Message}");
                return Array.Empty<byte>();
            }
        }
    
    /// <summary>
    /// EN: Save offset metadata for a composed file (only if custom offsets)
    /// FR: Sauvegarder les mÃ©tadonnÃ©es d'offsets pour un fichier composÃ© (seulement si offsets personnalisÃ©s)
    /// </summary>
    private void SaveOffsetMetadata(string composedFilePath, int fanartOffsetX, int fanartOffsetY, double fanartScale, int logoOffsetX, int logoOffsetY, double logoScale)
    {
        try
        {
            // EN: Only save metadata if offsets are not default (0,0,1.0)
            // FR: Sauvegarder les mÃ©tadonnÃ©es seulement si offsets ne sont pas par dÃ©faut (0,0,1.0)
            bool isDefaultOffsets = 
                fanartOffsetX == 0 && fanartOffsetY == 0 && Math.Abs(fanartScale - 1.0) < 0.01 &&
                logoOffsetX == 0 && logoOffsetY == 0 && Math.Abs(logoScale - 1.0) < 0.01;
            
            var metadataPath = Path.ChangeExtension(composedFilePath, ".json");
            
            if (isDefaultOffsets)
            {
                // EN: Delete metadata file if it exists (offsets reset to default)
                // FR: Supprimer le fichier de mÃ©tadonnÃ©es s'il existe (offsets rÃ©initialisÃ©s par dÃ©faut)
                if (File.Exists(metadataPath))
                {
                    File.Delete(metadataPath);
                    _logger.LogInformation($"[OFFSET METADATA] Deleted (offsets reset to default): {Path.GetFileName(metadataPath)}");
                }
                return;
            }
            
            // EN: Save metadata for custom offsets
            // FR: Sauvegarder les mÃ©tadonnÃ©es pour offsets personnalisÃ©s
            var metadata = new
            {
                fanartOffsetX,
                fanartOffsetY,
                fanartScale,
                logoOffsetX,
                logoOffsetY,
                logoScale
            };
            
            var json = System.Text.Json.JsonSerializer.Serialize(metadata, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(metadataPath, json);
            _logger.LogInformation($"[OFFSET METADATA] Saved custom offsets: {Path.GetFileName(metadataPath)}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"[OFFSET METADATA] Failed to save: {ex.Message}");
        }
    }
    
    /// <summary>
    /// EN: Check if offset metadata matches current offsets
    /// FR: VÃ©rifier si les mÃ©tadonnÃ©es d'offsets correspondent aux offsets actuels
    /// </summary>
    private bool ValidateOffsetMetadata(string composedFilePath, int fanartOffsetX, int fanartOffsetY, double fanartScale, int logoOffsetX, int logoOffsetY, double logoScale)
    {
        try
        {
            var metadataPath = Path.ChangeExtension(composedFilePath, ".json");
            
            // EN: Check if requested offsets are default
            // FR: VÃ©rifier si les offsets demandÃ©s sont par dÃ©faut
            bool requestedIsDefault = 
                fanartOffsetX == 0 && fanartOffsetY == 0 && Math.Abs(fanartScale - 1.0) < 0.01 &&
                logoOffsetX == 0 && logoOffsetY == 0 && Math.Abs(logoScale - 1.0) < 0.01;
            
            if (!File.Exists(metadataPath))
            {
                // EN: No metadata = default offsets assumed. Valid if requested offsets are also default.
                // FR: Pas de mÃ©tadonnÃ©es = offsets par dÃ©faut supposÃ©s. Valide si offsets demandÃ©s sont aussi par dÃ©faut.
                if (requestedIsDefault)
                {
                    _logger.LogInformation($"[OFFSET METADATA] No metadata (default offsets), requested offsets are also default - cache valid");
                    return true;
                }
                else
                {
                    _logger.LogInformation($"[OFFSET METADATA] No metadata (default offsets), but custom offsets requested - cache invalid");
                    return false;
                }
            }
            
            var json = File.ReadAllText(metadataPath);
            var metadata = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, System.Text.Json.JsonElement>>(json);
            
            if (metadata == null) return false;
            
            // Compare all offset values
            bool matches = 
                metadata.TryGetValue("fanartOffsetX", out var fx) && fx.GetInt32() == fanartOffsetX &&
                metadata.TryGetValue("fanartOffsetY", out var fy) && fy.GetInt32() == fanartOffsetY &&
                metadata.TryGetValue("fanartScale", out var fs) && Math.Abs(fs.GetDouble() - fanartScale) < 0.01 &&
                metadata.TryGetValue("logoOffsetX", out var lx) && lx.GetInt32() == logoOffsetX &&
                metadata.TryGetValue("logoOffsetY", out var ly) && ly.GetInt32() == logoOffsetY &&
                metadata.TryGetValue("logoScale", out var ls) && Math.Abs(ls.GetDouble() - logoScale) < 0.01;
            
            if (!matches)
            {
                _logger.LogInformation($"[OFFSET METADATA] Offsets changed - Fanart: {metadata["fanartOffsetX"].GetInt32()},{metadata["fanartOffsetY"].GetInt32()},{metadata["fanartScale"].GetDouble():F2} -> {fanartOffsetX},{fanartOffsetY},{fanartScale:F2} | Logo: {metadata["logoOffsetX"].GetInt32()},{metadata["logoOffsetY"].GetInt32()},{metadata["logoScale"].GetDouble():F2} -> {logoOffsetX},{logoOffsetY},{logoScale:F2}");
            }
            
            return matches;
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"[OFFSET METADATA] Validation failed: {ex.Message}");
            return false;
        }
    }
        public string GetScrapingPlaceholder(string type, string? scraperName = "ScreenScraper")
        {
            string fileName = type == "dmd" ? "scraping-dmd.png" : "scraping.png";
            // Important: We don't cache generic file if we want dynamic text. 
            // BUT, for performance, we might want to cache per scraper?
            // Or just overwrite? If we overwrite, multiple scrapers running in parallel might cause contention.
            // For now, let's append scraperName to filename if not ScreenScraper to allow dynamic generation.
            
            if (!string.IsNullOrEmpty(scraperName) && scraperName != "ScreenScraper")
            {
                fileName = type == "dmd" ? $"scraping-dmd-{scraperName}.png" : $"scraping-{scraperName}.png";
            }

            string placeholderPath = Path.Combine(_config.MarqueeImagePath, fileName);

            if (File.Exists(placeholderPath)) return placeholderPath;

            // Generate if missing
            try
            {
                int w = type == "dmd" ? _config.DmdWidth : _config.MarqueeWidth;
                int h = type == "dmd" ? _config.DmdHeight : _config.MarqueeHeight;
                
                string displayLabel = string.IsNullOrEmpty(scraperName) ? "Scraping..." : $"{scraperName}: Scraping...";
                if (type == "dmd") displayLabel = "Scraping..."; // Keep DMD simple or use short name? 
                if (type == "dmd" && !string.IsNullOrEmpty(scraperName)) displayLabel = scraperName; // Just name on DMD?
                
                string label = displayLabel;
                
                var startInfo = new ProcessStartInfo
                {
                    FileName = _config.IMPath,
                    Arguments = $"-size {w}x{h} xc:black -gravity center -fill white -pointsize {(type == "dmd" ? 12 : 24)} -draw \"text 0,0 '{label}'\" \"{placeholderPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using (var process = Process.Start(startInfo))
                {
                    process?.WaitForExit(5000);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to generate scraping placeholder: {ex.Message}");
            }

            return File.Exists(placeholderPath) ? placeholderPath : _config.DefaultImagePath;
        }
        public string GenerateScoreOverlay(int currentPoints, int totalPoints, bool isDmd, bool isHardcore = false, bool forceRegenerate = false)
        {
            try
            {
                int canvasWidth, canvasHeight;
                if (isDmd)
                {
                    canvasWidth = _config.DmdWidth > 0 ? _config.DmdWidth : 128;
                    canvasHeight = _config.DmdHeight > 0 ? _config.DmdHeight : 32;
                }
                else
                {
                   canvasWidth = _config.MarqueeWidth;
                   canvasHeight = _config.MarqueeHeight;
                }

                string text = $"{currentPoints}/{totalPoints}";
                string overlayFolder = isHardcore ? "hc_overlays" : "overlays";
                string outputFolder = Path.Combine(_config.CachePath, overlayFolder);
                Directory.CreateDirectory(outputFolder);

                // EN: Clean up old score overlays to prevent accumulation
                // FR: Nettoyer les anciens overlays de score pour Ã©viter l'accumulation
                try
                {
                    var prefix = $"score_overlay_{(isDmd ? "dmd" : "mpv")}_";
                    var oldFiles = Directory.GetFiles(outputFolder, $"{prefix}*.png");
                    foreach (var file in oldFiles)
                    {
                        try { File.Delete(file); } catch { }
                    }
                }
                catch { }

                // EN: Use unique filename to avoid file locking issues
                // FR: Utiliser un nom de fichier unique pour Ã©viter les problÃ¨mes de verrouillage de fichier
                string timestamp = forceRegenerate ? $"_{DateTime.Now.Ticks}" : "";
                string outputPath = Path.Combine(outputFolder, $"score_overlay_{(isDmd ? "dmd" : "mpv")}_{DateTime.Now.Ticks}{timestamp}.png");

                using (var bitmap = new Bitmap(canvasWidth, canvasHeight, PixelFormat.Format32bppArgb))
                using (var g = Graphics.FromImage(bitmap))
                {
                    // EN: Pixel Text Rendering for Retro Look
                    // FR: Rendu de texte pixel pour look rÃ©tro
                    g.SmoothingMode = SmoothingMode.None;
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.SingleBitPerPixelGridFit;
                    g.Clear(Color.Transparent);

                    // Style configuration
                    // EN: Proportional scaling based on target box height
                    // FR: Scaling proportionnel basé sur la hauteur de la boîte cible
                    var overlay = GetOverlayItem("score", isDmd);
                    int refBoxHeight = isDmd ? (canvasHeight < 64 ? 32 : canvasHeight) : (overlay != null ? overlay.Height : canvasHeight / 6);
                    float fontSize = (overlay != null && overlay.FontSize > 0) 
                        ? overlay.FontSize 
                        : (isDmd ? (refBoxHeight < 64 ? 6 : 10) : Math.Max(10, (int)(refBoxHeight * 0.5)));

                    var fontFamilyStr = _config.RAFontFamily;
                    var fontStyle = FontStyle.Bold;
                    
                    Font? font = null;
                    PrivateFontCollection? pfc = null;

                    try
                    {
                        // 1. Try Loading Custom Font File from medias/retroachievements/fonts/*.ttf
                        string fontsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "medias", "retroachievements", "fonts");
                        if (Directory.Exists(fontsDir))
                        {
                            var fontFiles = Directory.GetFiles(fontsDir, "*.ttf");
                            if (fontFiles.Length > 0)
                            {
                                pfc = new PrivateFontCollection();
                                pfc.AddFontFile(fontFiles[0]); // Load first found font
                                if (pfc.Families.Length > 0)
                                {
                                    font = new Font(pfc.Families[0], fontSize, fontStyle);
                                }
                            }
                        }

                        // 2. Fallback to Configured Font Family
                        if (font == null)
                        {
                             font = new Font(fontFamilyStr, fontSize, fontStyle);
                        }
                    }
                    catch
                    {
                         // EN: Fallback to generic sans serif
                         font = new Font(FontFamily.GenericSansSerif, fontSize, fontStyle);
                    }
                    
                    using (font)
                    {
                          // EN: Measurement Loop for DMD Auto-fit
                          // FR: Boucle de mesure pour l'ajustement automatique sur DMD
                          SizeF textSize = SizeF.Empty;
                          SizeF hcSize = SizeF.Empty;
                          string hcText = "HC ";
                          float currentFontSize = fontSize;
                          bool textFits = false;
                          int paddingX = isDmd ? 1 : 8;
                          int paddingY = isDmd ? 0 : 10;
                          int boxWidth = 0;
                          int boxHeight = 0;
                          Font effectiveFont = font!;

                          // EN: Iteratively reduce font size if it doesn't fit on target box
                          // FR: Réduire itérativement la taille de la police si elle ne tient pas dans la zone cible
                          int targetLimitWidth = canvasWidth;
                          int targetLimitHeight = canvasHeight;

                          overlay = GetOverlayItem("score", isDmd); 
                          if (overlay != null && !overlay.IsEnabled) return string.Empty;

                          if (overlay != null)
                          {
                              targetLimitWidth = overlay.Width;
                              targetLimitHeight = overlay.Height;
                              // EN: For DMD, allow utilizing full canvas width even if overlay is defined narrower (auto-expand)
                              // FR: Pour DMD, permettre d'utiliser toute la largeur du canvas même si l'overlay est défini plus étroit
                              if (isDmd) targetLimitWidth = canvasWidth;
                          }

                          // EN: Provide a manual override: If FontSize is set by user, we trust it and skip auto-shrink
                          // FR: S'il y a une taille définie par l'utilisateur, on l'utilise telle quelle (pas de réduction auto)
                          bool isFixedSize = (overlay != null && overlay.FontSize > 0);

                          while (!isFixedSize && !textFits && currentFontSize >= 5)
                          {
                              // Create a temporary font for testing if size changed
                              using (var tempFont = (currentFontSize == fontSize) ? null : new Font(font?.FontFamily ?? FontFamily.GenericSansSerif, currentFontSize, fontStyle))
                              {
                                  var activeFont = tempFont ?? font;
                                  if (activeFont == null) break;

                                  textSize = g.MeasureString(text, activeFont);
                                  hcSize = isHardcore ? g.MeasureString(hcText, activeFont) : SizeF.Empty;
                                  boxWidth = (int)(textSize.Width + hcSize.Width) + (paddingX * 2);
                                  boxHeight = (int)Math.Max(textSize.Height, hcSize.Height) + (paddingY * 2);
                                  
                                  // EN: Check both width and height fit within target rectangle (box or canvas)
                                  // FR: Vérifier que la largeur et la hauteur tiennent dans le rectangle cible
                                  if (boxWidth <= targetLimitWidth * 0.98f && boxHeight <= targetLimitHeight)
                                  {
                                      textFits = true;
                                      if (tempFont != null)
                                      {
                                          effectiveFont = new Font(activeFont.FontFamily, currentFontSize, fontStyle);
                                      }
                                  }
                                  else
                                  {
                                      currentFontSize -= 0.5f;
                                  }
                              }
                          }
                          
                          // EN: Calculate dimensions for fixed size case if loop was skipped
                          if (isFixedSize)
                          {
                              textSize = g.MeasureString(text, effectiveFont);
                              hcSize = isHardcore ? g.MeasureString(hcText, effectiveFont) : SizeF.Empty;
                              boxWidth = (int)(textSize.Width + hcSize.Width) + (paddingX * 2);
                              boxHeight = (int)Math.Max(textSize.Height, hcSize.Height) + (paddingY * 2);
                          }

                          try
                          {
                              int x, y;
                              int finalBoxWidth = boxWidth;
                              int finalBoxHeight = boxHeight;

                              if (overlay != null)
                              {
                                  x = overlay.X;
                                  y = overlay.Y;
                                  finalBoxWidth = Math.Max(boxWidth, overlay.Width); // Auto-expand for long text / FR: Auto-agrandir
                                  
                                  // EN: If DMD and expanded beyond overlay width, re-center horizontally on canvas
                                  // FR: Si DMD et agrandi au-delà de la largeur de l'overlay, recentrer horizontalement sur le canvas
                                  if (isDmd && finalBoxWidth > overlay.Width)
                                  {
                                      x = (canvasWidth - finalBoxWidth) / 2;
                                  }
                                  finalBoxHeight = overlay.Height;
                              }
                              else if (isDmd)
                              {
                                  // Centered for DMD / FR: Centré pour DMD
                                  x = (canvasWidth - boxWidth) / 2;
                                  y = (canvasHeight - boxHeight) / 2;
                                  y += 3; // EN: Move down specific amount requested / FR: Descendre selon demande
                              }
                              else
                              {
                                  // Top-Right for MPV with margin
                                  int margin = 20;
                                  x = canvasWidth - boxWidth - margin;
                                  y = margin;
                              }

                              // Draw Background Box
                              Color bgColor = Color.FromArgb(180, 40, 40, 40); // Default semi-transparent grey
                              if (isDmd) bgColor = Color.FromArgb(255, 60, 60, 60); // EN: Solid Dark Gray for DMD Masking / FR: Gris foncé opaque pour le masquage DMD

                              if (bgColor.A > 0)
                              {
                                  using (var brush = new SolidBrush(bgColor))
                                  {
                                      g.FillRectangle(brush, x, y, finalBoxWidth, finalBoxHeight);
                                  }
                              }
                                 
                              // Draw Border
                              var resolvedTextColor = GetColorFromHex(overlay?.TextColor ?? "", Color.Gold); // Renamed to avoid capture if needed, though local scope is fine
                              
                              // EN: For MPV, we always draw a border (Gold/Silver)
                              // FR: Pour MPV, on dessine toujours une bordure (Or/Argent)
                              Color borderColor = isHardcore ? Color.Gold : Color.Silver;
                              // EN: REMOVED: Do not override border color with text color on DMD
                              // FR: RETIRÉ : Ne pas écraser la couleur de bordure avec la couleur du texte sur DMD
                              // if (isDmd) borderColor = Color.FromArgb(200, textColor.R, textColor.G, textColor.B);

                              using (var pen = new Pen(borderColor, isDmd ? 1 : 2))
                              {
                                  g.DrawRectangle(pen, x, y, finalBoxWidth, finalBoxHeight);
                              }

                             // Draw Text
                              using (var brush = new SolidBrush(resolvedTextColor))
                              {
                                  // Horizontal Centering Calculation
                                  float totalWidth = textSize.Width + hcSize.Width;
                                  float currentX = x + (finalBoxWidth - totalWidth) / 2;
                                  float textOffsetY = isDmd ? 1f : 0f; // EN: Shift text down inside frame / FR: Décaler texte vers le bas dans le cadre
                                  
                                  var centerFormat = new StringFormat 
                                  { 
                                      Alignment = StringAlignment.Near, 
                                      LineAlignment = StringAlignment.Center,
                                      FormatFlags = StringFormatFlags.NoWrap
                                  };
                                  
                                  if (isHardcore)
                                  {
                                      using (var hcBrush = new SolidBrush(Color.Red))
                                      {
                                          var hcRect = new RectangleF(currentX, y + textOffsetY, hcSize.Width, finalBoxHeight);
                                          // Note: DrawStringWithOutline might need adaptation for HC, but usually HC is small enough.
                                          // However, let's just use standard DrawString for HC to be safe or use outline if consistency is needed.
                                          // Score usually needs outline on DMD.
                                          DrawStringWithOutline(g, hcText, effectiveFont, hcBrush, hcRect, centerFormat, isDmd);
                                      }
                                      currentX += hcSize.Width;
                                  }
                                  
                                  // EN: Expand Score Rect slightly to prevent wrap / FR: Élargir légérement le rect Score pour éviter le retour à la ligne
                                  var scoreRect = new RectangleF(currentX, y + textOffsetY, textSize.Width + 5, finalBoxHeight);
                                  DrawStringWithOutline(g, text, effectiveFont, brush, scoreRect, centerFormat, isDmd);
                             }
                         }
                         finally
                         {
                             // EN: Dispose effectiveFont if it was a newly created one
                             // FR: LibÃ©rer effectiveFont s'il s'agissait d'une nouvelle instance
                             if (effectiveFont != null && effectiveFont != font)
                             {
                                 effectiveFont.Dispose();
                             }
                         }
                         
                         bitmap.Save(outputPath, ImageFormat.Png);
                         _logger.LogInformation($"[RA Score] Generated score overlay ({(isDmd ? "DMD" : "MPV")}): {outputPath}");
                    }
                    return outputPath;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error generating score overlay: {ex.Message}");
                return string.Empty;
            }
        }

        /// <summary>
        /// EN: Generate locked badge from normal badge (grayscale + darkened)
        /// FR: GÃ©nÃ©rer badge verrouillÃ© depuis badge normal (niveaux de gris + assombri)
        /// </summary>
        public string? GenerateBadgeLockFromNormal(string normalBadgePath, int gameId, int achievementId)
        {
            if (string.IsNullOrEmpty(normalBadgePath) || !File.Exists(normalBadgePath))
            {
                _logger.LogWarning($"[RA Badge Lock] Source badge not found: {normalBadgePath}");
                return null;
            }

            try
            {
                var lockCacheDir = Path.Combine(_config.CachePath, "badges_lock", gameId.ToString());
                Directory.CreateDirectory(lockCacheDir);

                var lockPath = Path.Combine(lockCacheDir, $"{achievementId}_lock_generated.png");

                // EN: Check cache / FR: VÃ©rifier cache
                if (File.Exists(lockPath))
                {
                    _logger.LogDebug($"[RA Badge Lock] Found cached: {lockPath}");
                    return lockPath;
                }

                // EN: Generate grayscale / FR: GÃ©nÃ©rer niveaux de gris
                using (var sourceImage = Image.FromFile(normalBadgePath))
                using (var grayBitmap = new Bitmap(sourceImage.Width, sourceImage.Height))
                {
                    using (var g = Graphics.FromImage(grayBitmap))
                    {
                        // EN: Grayscale color matrix / FR: Matrice de couleur niveaux de gris
                        var colorMatrix = new ColorMatrix(new float[][]
                        {
                            new float[] {0.299f, 0.299f, 0.299f, 0, 0},
                            new float[] {0.587f, 0.587f, 0.587f, 0, 0},
                            new float[] {0.114f, 0.114f, 0.114f, 0, 0},
                            new float[] {0, 0, 0, 1, 0},
                            new float[] {0, 0, 0, 0, 1}
                        });

                        var attributes = new ImageAttributes();
                        attributes.SetColorMatrix(colorMatrix);

                        g.DrawImage(sourceImage,
                            new Rectangle(0, 0, sourceImage.Width, sourceImage.Height),
                            0, 0, sourceImage.Width, sourceImage.Height,
                            GraphicsUnit.Pixel,
                            attributes);
                    }

                    // EN: Darken (50% opacity black overlay) / FR: Assombrir (overlay noir 50% opacitÃ©)
                    using (var g = Graphics.FromImage(grayBitmap))
                    using (var brush = new SolidBrush(Color.FromArgb(128, Color.Black)))
                    {
                        g.FillRectangle(brush, 0, 0, grayBitmap.Width, grayBitmap.Height);
                    }

                    grayBitmap.Save(lockPath, ImageFormat.Png);
                    _logger.LogInformation($"[RA Badge Lock] Generated grayscale: {lockPath}");
                    return lockPath;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[RA Badge Lock] Error generating locked badge: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// EN: Get the capacity and sizing of the badge ribbon based on template
        /// FR: Obtenir la capacitÃ© et le dimensionnement du bandeau de badges basÃ© sur le template
        /// </summary>
        public (int BadgeSize, int MaxBadgesPerFrame) GetBadgeRibbonCapacity(bool isDmd)
        {
            int screenWidth = isDmd ? (_config.DmdWidth > 0 ? _config.DmdWidth : 128) : _config.MarqueeWidth;
            int screenHeight = isDmd ? (_config.DmdHeight > 0 ? _config.DmdHeight : 32) : _config.MarqueeHeight;
            
            var overlay = GetOverlayItem("badges", isDmd);
            int ribbonWidth, ribbonHeight;

            if (overlay != null && overlay.IsEnabled)
            {
                ribbonWidth = overlay.Width;
                ribbonHeight = overlay.Height;
            }
            else
            {
                ribbonWidth = screenWidth;
                // Default logic fallback:
                ribbonHeight = isDmd ? 16 : (int)(screenHeight / 4.5);
            }

            int badgeSize = isDmd ? Math.Min(32, ribbonHeight) : ribbonHeight;
            int spacing = isDmd ? 0 : 1;
            int maxBadges = Math.Max(1, ribbonWidth / (badgeSize + spacing));

            return (badgeSize, maxBadges);
        }

        /// <summary>
        /// EN: Generate badge ribbon overlay (locked/unlocked badges in horizontal ribbon)
        /// FR: GÃ©nÃ©rer overlay de bandeau de badges (badges verrouillÃ©s/dÃ©verrouillÃ©s en bandeau horizontal)
        /// </summary>
        public async Task<string> GenerateBadgeRibbonOverlay(
            Dictionary<string, Achievement> achievements,
            int gameId,
            RetroAchievementsService raService,
            bool isDmd,
            bool isHardcore = false,
            bool forceRegenerate = false)
        {
            if (achievements == null || achievements.Count == 0)
            {
                _logger.LogWarning("[RA Ribbon] No achievements to display");
                return string.Empty;
            }

            try
            {
                int screenWidth = isDmd ? (_config.DmdWidth > 0 ? _config.DmdWidth : 128) : _config.MarqueeWidth;
                int screenHeight = isDmd ? (_config.DmdHeight > 0 ? _config.DmdHeight : 32) : _config.MarqueeHeight;
                
                var overlay = GetOverlayItem("badges", isDmd);
                int ribbonX = 0;
                int ribbonY, ribbonWidth, ribbonHeight;

                    if (overlay != null)
                    {
                        if (!overlay.IsEnabled) return string.Empty;
                        ribbonX = overlay.X;
                        ribbonY = overlay.Y;
                        ribbonWidth = overlay.Width;
                        ribbonHeight = overlay.Height;
                    }
                    else
                    {
                        ribbonWidth = screenWidth;
                        ribbonHeight = isDmd ? 16 : (int)(screenHeight / 4.5);
                        ribbonY = screenHeight - ribbonHeight; // EN: Align to bottom / FR: Aligner en bas
                    }

                // Dynamic badge size
                int badgeSize = isDmd ? Math.Min(32, ribbonHeight) : ribbonHeight;
                
                // EN: Locked badges show top 30% (peek effect)
                double peekPercentage = 0.30;
                int peekHeight = (int)(badgeSize * peekPercentage);
                
                bool isTopMode = (overlay != null && overlay.Y <= 2);

                // EN: Position badges so locked (30% top/bottom) stick to the edge of the ribbon/box
                // FR: Positionner les badges pour que le verrouillage (30% haut/bas) colle au bord du bandeau
                int baseY;
                if (isTopMode)
                {
                    baseY = ribbonY;
                }
                else
                {
                    baseY = (overlay != null && overlay.IsEnabled) ? ribbonY + (ribbonHeight - peekHeight) : screenHeight - peekHeight;
                }
                
                // EN: Create transparent canvas
                using (var bitmap = new Bitmap(screenWidth, screenHeight, PixelFormat.Format32bppArgb))
                using (var g = Graphics.FromImage(bitmap))
                {
                    g.Clear(Color.Transparent);
                    g.CompositingMode = CompositingMode.SourceOver;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    
                    int spacing = isDmd ? 0 : 1;
                    int x = ribbonX;
                    int xMax = ribbonX + ribbonWidth;
                    
                    // EN: Sort achievements by DisplayOrder / FR: Trier succÃ¨s par DisplayOrder
                    var sortedAchievements = achievements.Values
                        .OrderBy(a => a.DisplayOrder)
                        .ToList();
                    
                    foreach (var achievement in sortedAchievements)
                    {
                        // EN: Stop if we exceed screen width (allow 10px tolerance for DMD to ensure 4th badge)
                        // FR: Arrêter si on dépasse la largeur (tolérance 10px pour DMD pour assurer le 4ème badge)
                        if (x + badgeSize > xMax + (isDmd ? 10 : 0))
                        {
                            _logger.LogDebug($"[RA Ribbon] Reached screen width limit, displayed {sortedAchievements.IndexOf(achievement)} badges");
                            break;
                        }
                        
                        string? badgePath = null;
                        
                        bool isUnlocked = isHardcore ? achievement.DateEarnedHardcore.HasValue : achievement.Unlocked;

                        if (isUnlocked)
                        {
                            // EN: Get unlocked badge / FR: Obtenir badge dÃ©verrouillÃ©
                            badgePath = await raService.GetBadgePath(gameId, achievement.ID);
                        }
                        else
                        {
                            // EN: Get locked badge / FR: Obtenir badge verrouillÃ©
                            badgePath = await raService.GetBadgeLockPath(gameId, achievement.ID);
                        }
                        
                        if (string.IsNullOrEmpty(badgePath) || !File.Exists(badgePath))
                        {
                            _logger.LogWarning($"[RA Ribbon] Badge not found: Achievement {achievement.ID}");
                            continue;
                        }
                        
                        try
                        {
                                if (isUnlocked)
                                {
                                    // EN: Draw full badge filling the ribbon - rises up or drops down (tile effect)
                                    // FR: Dessiner badge complet remplissant le bandeau - monte ou descend (effet tuile)
                                    int unlockedY = ribbonY;
                                    
                                    bool badgeIsHc = achievement.DateEarnedHardcore.HasValue;
                                    DrawBadgeWithFrame(g, badgePath, x, unlockedY, badgeSize, badgeIsHc, isDmd);
                                }
                                else
                                {
                                    // EN: Draw partial badge - top or bottom 30% stuck to the edge
                                    // FR: Dessiner badge partiel - haut ou bas 30% collé au bord
                                    using (var badge = Image.FromFile(badgePath))
                                    {
                                        int srcY = isTopMode ? badge.Height - Math.Max(1, (int)(badge.Height * peekPercentage)) : 0;
                                        int srcHeight = (int)(badge.Height * peekPercentage);
                                        
                                        var srcRect = new Rectangle(0, srcY, badge.Width, srcHeight);
                                        var destRect = new Rectangle(x, baseY, badgeSize, peekHeight);
                                        
                                        g.DrawImage(badge, destRect, srcRect, GraphicsUnit.Pixel);
                                    }
                                }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"[RA Ribbon] Error drawing badge {achievement.ID}: {ex.Message}");
                        }
                        
                        x += badgeSize + spacing;
                    }
                    
                    // EN: Save overlay / FR: Sauvegarder overlay
                    string overlayFolder = isHardcore ? "hc_overlays" : "overlays";
                    string outputFolder = Path.Combine(_config.CachePath, overlayFolder);
                    Directory.CreateDirectory(outputFolder);
                    
                    // EN: Clean up old ribbon overlays / FR: Nettoyer anciens overlays de bandeau
                    try
                    {
                        var prefix = $"badge_ribbon_{(isDmd ? "dmd" : "mpv")}_";
                        var oldFiles = Directory.GetFiles(outputFolder, $"{prefix}*.png");
                        foreach (var file in oldFiles)
                        {
                            try { File.Delete(file); } catch { }
                        }
                    }
                    catch { }
                    
                    string outputPath = Path.Combine(outputFolder, $"badge_ribbon_{(isDmd ? "dmd" : "mpv")}_{DateTime.Now.Ticks}.png");
                    bitmap.Save(outputPath, ImageFormat.Png);
                    
                    _logger.LogInformation($"[RA Ribbon] Generated badge ribbon: {outputPath} ({sortedAchievements.Count} achievements)");
                    return outputPath;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[RA Ribbon] Error generating badge ribbon: {ex.Message}");
                return string.Empty;
            }
        }
        
        /// <summary>
        /// EN: Compose score and badges into single overlay image for MPV
        /// FR: Composer score et badges en une seule image overlay pour MPV
        /// </summary>
        public string ComposeScoreAndBadges(string? scorePath, string? badgesPath, string? countPath = null, int screenWidth = 1920, int screenHeight = 360, bool isHardcore = false, bool isDmd = false)
        {
            try
            {
                using (var canvas = new Bitmap(screenWidth, screenHeight, PixelFormat.Format32bppArgb))
                using (var g = Graphics.FromImage(canvas))
                {
                    g.Clear(Color.Transparent);
                    g.CompositingMode = CompositingMode.SourceOver;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;

                    // EN: Define drawing elements with their Z-Order
                    // FR: DÃ©finir les Ã©lÃ©ments Ã  dessiner avec leur Z-Order
                    var itemsToDraw = new List<dynamic>();

                    if (!string.IsNullOrEmpty(badgesPath) && File.Exists(badgesPath)) 
                        itemsToDraw.Add(new { Path = badgesPath, Type = "badges", Z = GetOverlayItem("badges", isDmd)?.ZOrder ?? 1 });

                    if (!string.IsNullOrEmpty(countPath) && File.Exists(countPath)) 
                        itemsToDraw.Add(new { Path = countPath, Type = "count", Z = GetOverlayItem("count", isDmd)?.ZOrder ?? 2 });

                    if (!string.IsNullOrEmpty(scorePath) && File.Exists(scorePath)) 
                        itemsToDraw.Add(new { Path = scorePath, Type = "score", Z = GetOverlayItem("score", isDmd)?.ZOrder ?? 3 });

                    // EN: Sort by Z-Order / FR: Trier par Z-Order
                    var sortedItems = itemsToDraw.OrderBy(i => (int)i.Z).ToList();

                    foreach (var item in sortedItems)
                    {
                        using (var img = Image.FromFile(item.Path))
                        {
                            if (item.Type == "badges")
                            {
                                bool isFullScreen = img.Height == screenHeight && img.Width == screenWidth;
                                int badgesY = isFullScreen ? 0 : screenHeight - img.Height;
                                g.DrawImage(img, 0, badgesY, img.Width, img.Height);
                            }
                            else
                            {
                                // EN: Draw at (0,0) as positions are handled internally in their respective generation methods
                                g.DrawImage(img, 0, 0, screenWidth, screenHeight);
                            }
                        }
                    }
                    
                    // EN: Save composed overlay / FR: Sauvegarder overlay composÃ©
                    string overlayFolder = isHardcore ? "hc_overlays" : "overlays";
                    string outputFolder = Path.Combine(_config.CachePath, overlayFolder);
                    Directory.CreateDirectory(outputFolder);
                    
                    string outputPath = Path.Combine(outputFolder, $"composed_mpv_{DateTime.Now.Ticks}.png");
                    canvas.Save(outputPath, ImageFormat.Png);
                    
                    _logger.LogInformation($"[RA Compose] Created MPV composed overlay: {outputPath}");
                    return outputPath;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[RA Compose] Error composing score and badges: {ex.Message}");
                return string.Empty;
            }
        }
        
        /// <summary>
        /// EN: Generate achievement count overlay (unlocked/total)
        /// FR: GÃ©nÃ©rer overlay compteur achievements (dÃ©bloquÃ©s/total)
        /// </summary>
        public string GenerateAchievementCountOverlay(int unlockedCount, int totalCount, bool isDmd, bool isHardcore = false, bool forceRegenerate = false)
        {
            try
            {
                int canvasWidth, canvasHeight;
                if (isDmd)
                {
                    canvasWidth = _config.DmdWidth > 0 ? _config.DmdWidth : 128;
                    canvasHeight = _config.DmdHeight > 0 ? _config.DmdHeight : 32;
                }
                else
                {
                    canvasWidth = _config.MarqueeWidth;
                    canvasHeight = _config.MarqueeHeight;
                }

                string text = $"{unlockedCount}/{totalCount}";
                // if (isHardcore) text = $"HC {text}"; // EN: Handled separately now for coloring / FR: Géré séparément maintenant pour la coloration
                string overlayFolder = isHardcore ? "hc_overlays" : "overlays";
                string outputFolder = Path.Combine(_config.CachePath, overlayFolder);
                Directory.CreateDirectory(outputFolder);

                // EN: Clean up old count overlays / FR: Nettoyer anciens overlays compteur
                try
                {
                    var prefix = $"achievement_count_{(isDmd ? "dmd" : "mpv")}_";
                    var oldFiles = Directory.GetFiles(outputFolder, $"{prefix}*.png");
                    foreach (var file in oldFiles)
                    {
                        try { File.Delete(file); } catch { }
                    }
                }
                catch { }

                string timestamp = forceRegenerate ? $"_{DateTime.Now.Ticks}" : "";
                string outputPath = Path.Combine(outputFolder, $"achievement_count_{(isDmd ? "dmd" : "mpv")}_{DateTime.Now.Ticks}{timestamp}.png");

                using (var bitmap = new Bitmap(canvasWidth, canvasHeight, PixelFormat.Format32bppArgb))
                using (var g = Graphics.FromImage(bitmap))
                {
                    // EN: Pixel Text Rendering for Retro Look / FR: Rendu texte pixel look rÃ©tro
                    g.SmoothingMode = SmoothingMode.None;
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.SingleBitPerPixelGridFit;
                    g.Clear(Color.Transparent);

                    // EN: Proportional scaling based on target box height
                    // FR: Scaling proportionnel basé sur la hauteur de la boîte cible
                    var overlayMeta = GetOverlayItem("count", isDmd);
                    int refBoxH = isDmd ? (canvasHeight < 64 ? 32 : canvasHeight) : (overlayMeta != null ? overlayMeta.Height : canvasHeight / 6);
                    float fontSize = (overlayMeta != null && overlayMeta.FontSize > 0)
                        ? overlayMeta.FontSize
                        : (isDmd ? (refBoxH < 64 ? 7 : 10) : Math.Max(10, (int)(refBoxH * 0.5)));
                    var fontStyle = FontStyle.Bold;
                    
                    Font? font = null;
                    PrivateFontCollection? pfc = null;

                    string raFontFamily = _config.RAFontFamily;
                    if (string.IsNullOrEmpty(raFontFamily)) raFontFamily = "Arial";

                    try
                    {
                        // EN: Try Loading Custom Font File / FR: Essayer charger fichier police personnalisÃ©
                        string fontsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "medias", "retroachievements", "fonts");
                        if (Directory.Exists(fontsDir))
                        {
                            var fontFiles = Directory.GetFiles(fontsDir, "*.ttf");
                            if (fontFiles.Length > 0)
                            {
                                pfc = new PrivateFontCollection();
                                pfc.AddFontFile(fontFiles[0]);
                                if (pfc.Families.Length > 0)
                                {
                                    font = new Font(pfc.Families[0], fontSize, fontStyle);
                                }
                            }
                        }
                    }
                    catch { }

                    if (font == null)
                    {
                        font = new Font(raFontFamily, fontSize, fontStyle);
                    }
                    
                    using (font)
                    {
                        // EN: Auto-fit Loop for Count
                        float currentFontSize = fontSize;
                        bool textFits = false;
                        int paddingX = isDmd ? 2 : 8;
                        int paddingY = isDmd ? 4 : 10;
                        int boxWidth = 0;
                        int boxHeight = 0;
                        Font effectiveFont = font;
                        
                        // Define limits
                        int targetLimitWidth = canvasWidth;
                        int targetLimitHeight = canvasHeight;
                        if (overlayMeta != null) 
                        {
                            targetLimitWidth = overlayMeta.Width;
                            targetLimitHeight = overlayMeta.Height;
                            if (isDmd) targetLimitWidth = canvasWidth; // Same DMD expansion rule
                        }

                        // EN: Manual override check
                        bool isFixedSize = (overlayMeta != null && overlayMeta.FontSize > 0);

                        while (!isFixedSize && !textFits && currentFontSize >= 5)
                        {
                            using (var tempFont = (currentFontSize == fontSize) ? null : new Font(font.FontFamily, currentFontSize, fontStyle))
                            {
                                var activeFont = tempFont ?? font;
                                var textSize = g.MeasureString(text, activeFont);
                                var hcSize = isHardcore ? g.MeasureString("HC ", activeFont) : SizeF.Empty;

                                boxWidth = (int)(textSize.Width + hcSize.Width) + (paddingX * 2);
                                boxHeight = (int)Math.Max(textSize.Height, hcSize.Height) + (paddingY * 2);
                                
                                // Check fit
                                if (boxWidth <= targetLimitWidth * 0.98f && boxHeight <= targetLimitHeight)
                                {
                                    textFits = true;
                                    if (tempFont != null) effectiveFont = new Font(activeFont.FontFamily, currentFontSize, fontStyle); 
                                }
                                else
                                {
                                    currentFontSize -= 0.5f;
                                }
                            }
                        }

                        if (isFixedSize)
                        {
                            var textSize = g.MeasureString(text, effectiveFont);
                            var hcSize = isHardcore ? g.MeasureString("HC ", effectiveFont) : SizeF.Empty;
                            boxWidth = (int)(textSize.Width + hcSize.Width) + (paddingX * 2);
                            boxHeight = (int)Math.Max(textSize.Height, hcSize.Height) + (paddingY * 2);
                        }
                        
                         // Update variables for drawing
                        var textSizeFinal = g.MeasureString(text, effectiveFont);
                        var hcSizeFinal = isHardcore ? g.MeasureString("HC ", effectiveFont) : SizeF.Empty;
                        // font = effectiveFont; // REMOVED: Avoid reassigning using variable (CS0728)
                            
                        boxWidth = (int)(textSizeFinal.Width + hcSizeFinal.Width) + (paddingX * 2);
                        boxHeight = (int)Math.Max(textSizeFinal.Height, hcSizeFinal.Height) + (paddingY * 2);

                        var overlay = GetOverlayItem("count", isDmd);
                        if (overlay != null && !overlay.IsEnabled) return string.Empty;

                        int x, y;
                        int finalBoxWidth = boxWidth;
                        int finalBoxHeight = boxHeight;

                        if (overlay != null)
                        {
                            x = overlay.X;
                            y = overlay.Y;
                            finalBoxWidth = Math.Max(boxWidth, overlay.Width); // Auto-expand for long text / FR: Auto-agrandir
                            finalBoxHeight = overlay.Height;
                            
                            // EN: Re-center if expanded on DMD
                            if (isDmd && finalBoxWidth > overlay.Width)
                            {
                                x = (canvasWidth - finalBoxWidth) / 2;
                            }
                        }
                        else if (isDmd)
                        {
                            // EN: Centered for DMD / FR: CentrÃ© pour DMD
                            // EN: Centered for DMD / FR: CentrÃ© pour DMD
                            x = (canvasWidth - finalBoxWidth) / 2;
                            y = (canvasHeight - finalBoxHeight) / 2;
                            // EN: Micro-adjustment for visual vertical centering on DMD (often needs -1 or -2)
                            // FR: Micro-ajustement pour le centrage vertical visuel sur DMD (nécessite souvent -1 ou -2)
                            y -= 1; 
                        }
                        else
                        {
                            // EN: Top-Left for MPV with margin / FR: Haut-gauche pour MPV avec marge
                            int margin = 20;
                            x = margin;
                            y = margin;
                        }

                        Color bgColor = Color.FromArgb(180, 40, 40, 40); // Default semi-transparent grey
                        if (isDmd) bgColor = Color.FromArgb(255, 60, 60, 60);

                        if (bgColor.A > 0)
                        {
                            using (var brush = new SolidBrush(bgColor))
                            {
                                g.FillRectangle(brush, x, y, finalBoxWidth, finalBoxHeight);
                            }
                        }
                            
                        // EN: Draw Border
                        Color textColor = GetColorFromHex(overlay?.TextColor ?? "", Color.Gold);
                        // EN: For MPV, we always draw a border (Gold/Silver)
                        // FR: Pour MPV, on dessine toujours une bordure (Or/Argent)
                        Color borderColor = (isHardcore ? Color.Gold : Color.Silver);
                        // EN: REMOVED: Do not override border color with text color on DMD
                        // FR: RETIRÉ : Ne pas écraser la couleur de bordure avec la couleur du texte sur DMD
                        // if (isDmd) borderColor = Color.FromArgb(200, textColor.R, textColor.G, textColor.B);

                        using (var pen = new Pen(borderColor, isDmd ? 1 : 2))
                        {
                            g.DrawRectangle(pen, x, y, finalBoxWidth, finalBoxHeight);
                        }

                        // EN: Draw Text
                        using (var brush = new SolidBrush(textColor))
                        {
                            // Use full rectangle for centering
                            var rectF = new RectangleF(x, y, finalBoxWidth, finalBoxHeight);
                            var format = new StringFormat 
                            { 
                                Alignment = StringAlignment.Center, 
                                LineAlignment = StringAlignment.Center,
                                FormatFlags = StringFormatFlags.NoWrap
                            };
                            
                            // EN: Draw Split Text (HC in Red, Count in Gold)
                            // FR: Dessiner texte séparé (HC en Rouge, Compte en Or)
                            float totalContentWidth = textSizeFinal.Width + hcSizeFinal.Width;
                            float startX = x + (finalBoxWidth - totalContentWidth) / 2;
                            
                            // Vertical centering base
                            float centerY = y + (finalBoxHeight - Math.Max(textSizeFinal.Height, hcSizeFinal.Height)) / 2;
                            if (isDmd) centerY += 1; // EN: Adjusted up by 1px from previous +2 (Total +1 from center) / FR: Remonté de 1px

                            if (isHardcore)
                            {
                                using (var hcBrush = new SolidBrush(Color.Red))
                                {
                                    // Manually draw HC
                                    // Use generic format without alignment since we calculate X/Y manually for split parts
                                    g.DrawString("HC ", effectiveFont, hcBrush, startX, centerY);
                                }
                                startX += hcSizeFinal.Width;
                            }
                            
                            g.DrawString(text, effectiveFont, brush, startX, centerY);
                                                        
                            // DrawStringWithOutline(g, text, font, brush, rectF, format, isDmd); // Replaced by manual split drawing
                        }

                        // EN: Dispose effectiveFont if it was a new instance (created by auto-fit loop)
                        // FR: Disposer effectiveFont si c'est une nouvelle instance (créée par la boucle auto-fit)
                        if (effectiveFont != null && effectiveFont != font)
                        {
                            effectiveFont.Dispose();
                        }
                    }

                    bitmap.Save(outputPath, ImageFormat.Png);
                    _logger.LogInformation($"[RA Count] Generated count overlay: {outputPath}");
                    return outputPath;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[RA Count] Error generating count overlay: {ex.Message}");
                return string.Empty;
            }
            finally
            {
                // Cleanup (nothing specific needed here)
            }
        }

        /// <summary>
        /// EN: Helper to draw a badge with a frame (Gold for Hardcore)
        /// FR: Assistant pour dessiner un badge avec un cadre (DorÃ© pour Hardcore)
        /// </summary>
        private void DrawBadgeWithFrame(Graphics g, string badgePath, int x, int y, int size, bool isHardcore, bool isDmd)
        {
            try
            {
                using (var badgeImg = LoadBitmapWithSvgSupport(badgePath))
                {
                    if (badgeImg == null) return;

                    // EN: Fix for DMD Transparency: Replace Black pixels with Dark Gray to prevent transparency holes
                    // FR: Correctif Transparence DMD : Remplacer les pixels noirs par du Gris Foncé pour éviter les trous
                    if (isDmd)
                    {
                        // Clone to avoid locking issues if source is locked, though LoadBitmap usually returns copy
                        // We need to iterate pixels. For performance on small badges, simple Get/SetPixel is okay.
                        // For larger ones, LockBits is better, but DMD badges are tiny (32x32 max usually).
                        if (badgeImg.Width <= 64 && badgeImg.Height <= 64)
                        {
                             for (int py = 0; py < badgeImg.Height; py++)
                             {
                                 for (int px = 0; px < badgeImg.Width; px++)
                                 {
                                     Color pixel = badgeImg.GetPixel(px, py);
                                     // Check for opaque pure black
                                     if (pixel.A > 240 && pixel.R == 0 && pixel.G == 0 && pixel.B == 0)
                                     {
                                         badgeImg.SetPixel(px, py, Color.FromArgb(255, 60, 60, 60));
                                     }
                                 }
                             }
                        }
                    }

                    // EN: Draw badge / FR: Dessiner le badge
                    g.DrawImage(badgeImg, x, y, size, size);

                    if (isHardcore)
                    {
                        // EN: Draw Golden Frame / FR: Dessiner cadre doré
                        // EN: Use 1px for DMD (small sizes), 2px for MPV / FR: Utiliser 1px pour DMD (petites tailles), 2px pour MPV
                        float penWidth = size <= 32 ? 1f : 2f;
                        using (var penGold = new Pen(Color.Gold, penWidth))
                        {
                            // EN: Adjust to stay inside bounds / FR: Ajuster pour rester dans les limites
                            int rectW = size - 1;
                            int rectH = size - 1;

                            // EN: Ensure we don't draw outside the visible area (bottom edge)
                            // FR: S'assurer de ne pas dessiner en dehors de la zone visible (bord inférieur)
                            int maxY = (int)g.VisibleClipBounds.Height - 1;
                            if (y + rectH > maxY) rectH = maxY - y;

                            g.DrawRectangle(penGold, x, y, rectW, rectH);
                            
                            // EN: Add inner glow/bevel effect / FR: Ajouter effet de relief/lueur interne
                            if (size > 32)
                            {
                                using (var penInner = new Pen(Color.FromArgb(180, Color.LightYellow), 1))
                                {
                                    // EN: Reduce rect size to stay inside the gold frame and visible area
                                    // FR: Réduire la taille du rect pour rester à l'intérieur du cadre doré et de la zone visible
                                    int innerW = size - 3;
                                    int innerH = size - 3;
                                    if (y + 1 + innerH > maxY) innerH = maxY - (y + 1);

                                    g.DrawRectangle(penInner, x + 1, y + 1, innerW, innerH);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[ImageConversion] Error DrawBadgeWithFrame: {ex.Message}");
            }
        }

        private string Sanitize(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;
            
            var result = input;
            
            // 1. Replace invalid filename chars
            var invalids = Path.GetInvalidFileNameChars();
            foreach (var c in invalids) result = result.Replace(c, '_');

            // 2. EN: Replace spaces and common delimiters that might cause issues with DMD drivers/CLI
            // FR: Remplacer les espaces et délimiteurs communs qui pourraient causer des soucis avec drivers DMD/CLI
            // EN: Also replace apostrophes which cause issues with MPV filter graph escaping
            // FR: Remplacer aussi les apostrophes qui causent des soucis avec l'échappement filter graph MPV
            char[] delimiters = { ' ', '(', ')', '[', ']', '-', '.', ',', '\'' };
            foreach (var c in delimiters) result = result.Replace(c, '_');

            // 3. EN: Collapse multiple underscores into one
            // FR: RÃ©duire les underscores multiples en un seul
            while (result.Contains("__")) result = result.Replace("__", "_");

            return result.Trim('_').Trim();
        }
        /// <summary>
        /// EN: Purge all files in the hc_overlays directory
        /// FR: Purger tous les fichiers du dossier hc_overlays
        /// </summary>
        public void PurgeHardcoreOverlays()
        {
            try
            {
                // EN: Purge from cache folder instead of hardcoded path
                // FR: Purger depuis le dossier cache au lieu du chemin codÃ© en dur
                string overlayFolder = Path.Combine(_config.CachePath, "hc_overlays");
                if (Directory.Exists(overlayFolder))
                {
                    var files = Directory.GetFiles(overlayFolder, "*.png");
                    foreach (var file in files)
                    {
                        try { File.Delete(file); } catch { }
                    }
                    _logger.LogInformation($"[ImageConversion] Purged {files.Length} files from hc_overlays");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[ImageConversion] Error purging hc_overlays: {ex.Message}");
            }
        }


        /// <summary>
        /// EN: Purge all files in the overlays directory (Softcore)
        /// FR: Purger tous les fichiers du dossier overlays (Softcore)
        /// </summary>
        public void PurgeSoftcoreOverlays()
        {
            try
            {
                // EN: Purge from cache folder (Softcore)
                // FR: Purger depuis le dossier cache (Softcore)
                string overlayFolder = Path.Combine(_config.CachePath, "overlays");
                if (Directory.Exists(overlayFolder))
                {
                    var files = Directory.GetFiles(overlayFolder, "*.png");
                    foreach (var file in files)
                    {
                        try { File.Delete(file); } catch { }
                    }
                    _logger.LogInformation($"[ImageConversion] Purged {files.Length} files from overlays (Softcore)");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[ImageConversion] Error purging overlays: {ex.Message}");
            }
        }
        /// <summary>
        /// EN: Generate an overlay image for Rich Presence text (Emoji aware if possible)
        /// FR: GÃ©nÃ©rer une image d'overlay pour le texte Rich Presence (conscient des Emojis si possible)
        /// </summary>
        public async Task<OverlayResult> GenerateRichPresenceOverlay(string text, bool isHardcore, int width = 1920, int height = 360, string? backgroundPath = null, string position = "center", bool forceRegenerate = false, CancellationToken token = default)
        {
            try
            {
                text = CleanRichPresenceString(text, false); // Keep emojis for MPV image generation
                
                var result = new OverlayResult { Width = width, Height = height };

                if (string.IsNullOrWhiteSpace(text)) return result; // Nothing to show

                // Cache dir
                string overlayFolder = isHardcore ? "hc_overlays" : "overlays";
                string cacheDir = Path.Combine(_config.CachePath, overlayFolder);
                if (!Directory.Exists(cacheDir)) Directory.CreateDirectory(cacheDir);

                // Unique filename logic
                string safeText = Sanitize(text);
                if (safeText.Length > 20) safeText = safeText.Substring(0, 20);
                
                // Include position and background hash in filename to differentiate
                int bgHash = backgroundPath?.GetHashCode() ?? 0;
                string filename = $"rp_{position}_{safeText}_{DateTime.Now.Ticks}_{bgHash}.png"; 
                string targetPath = Path.Combine(cacheDir, filename);

                using (var bitmap = new Bitmap(width, height))
                {
                    using (var g = Graphics.FromImage(bitmap))
                    {
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

                        // 1. Draw Background (for DMD composition) or Clear (for MPV overlay)
                        if (!string.IsNullOrEmpty(backgroundPath) && File.Exists(backgroundPath))
                        {
                            try
                            {
                                using (var bgImg = Image.FromFile(backgroundPath))
                                {
                                    g.DrawImage(bgImg, 0, 0, width, height); // Stretch to fit
                                }
                            }
                            catch
                            {
                                g.Clear(Color.Black); // Fallback
                            }
                        }
                        else
                        {
                            g.Clear(Color.Transparent);
                        }

                        // Font Setup
                        string fontFamily = "Segoe UI Emoji";
                        if (!IsFontInstalled(fontFamily)) fontFamily = "Arial"; // Fallback

                        // Colors
                        Color textColor = isHardcore ? Color.Gold : Color.White;

                        // 2. Calculate Positioning
                        var overlay = GetOverlayItem("rp_narration", width < 300);
                        float boxW, boxH, boxX, boxY;

                        if (overlay != null)
                        {
                            if (!overlay.IsEnabled) return result;
                            boxX = overlay.X;
                            boxY = overlay.Y;
                            boxW = overlay.Width;
                            boxH = overlay.Height;

                            // EN: Override text color if specified in overlay
                            if (!string.IsNullOrEmpty(overlay.TextColor))
                            {
                                textColor = GetColorFromHex(overlay.TextColor, textColor);
                            }
                        }
                        else
                        {
                            // Static Fallback
                            boxW = width * 0.5f;
                            boxH = height * 0.3f;
                            boxX = (width - boxW) / 2;
                            boxY = (height - boxH) / 2;
                        }

                        // EN: Determine Font Size (Config > Dynamic)
                        // FR: Déterminer la taille de police (Config > Dynamique)
                        float fontSize = 0f;
                        if (overlay != null && overlay.FontSize > 0)
                        {
                            fontSize = overlay.FontSize;
                        }
                        else
                        {
                             // Dynamic Fallback
                             fontSize = Math.Max(10, (int)(boxH * 0.35));
                             if (width < 300) fontSize = Math.Max(7, (int)(boxH * 0.5));
                        }

                        string raFontFamily = _config.RAFontFamily;
                        // Default fallback if empty
                        if (string.IsNullOrEmpty(raFontFamily)) raFontFamily = "Arial";

                        // Emoji detection override
                        bool containsEmoji = System.Text.RegularExpressions.Regex.IsMatch(text, @"\p{Cs}|[\u2000-\u3300]");
                        if (containsEmoji && IsFontInstalled("Segoe UI Emoji"))
                        {
                            raFontFamily = "Segoe UI Emoji";
                        }

                        if (!IsFontInstalled(raFontFamily)) raFontFamily = "Arial";
                        
                        // EN: Ensure fontSize is valid to avoid emSize exception
                        // FR: S'assurer que fontSize est valide pour éviter l'exception emSize
                        if (fontSize <= 0) fontSize = 10;

                        using (var font = new Font(raFontFamily, fontSize, FontStyle.Bold))
                        {
                            // Measure Text within the Box
                            var format = new StringFormat
                            {
                                Alignment = StringAlignment.Center,
                                LineAlignment = StringAlignment.Center,
                                Trimming = StringTrimming.None, // EN: No dots requested by user / FR: Pas de points demandés par l'utilisateur
                                FormatFlags = StringFormatFlags.NoWrap
                            };
                            
                            // Measure unbounded check for scrolling
                            var trueSize = g.MeasureString(text, font, new PointF(0, 0), StringFormat.GenericTypographic);
                            
                            // 3. Draw Background Box
                            Color bgColor = Color.Transparent; // EN: Transparent background as requested / FR: Fond transparent comme demandé

                            // EN: Check overflow and generate GIF if needed
                            // FR: Vérifier le dépassement et générer un GIF si nécessaire
                            _logger.LogInformation($"[RP Debug] Text '{text}' Width: {trueSize.Width} vs BoxW: {boxW - 20}");
                            
                            if (trueSize.Width > boxW - 20)
                            {
                                var gifResult = await GenerateFullscreenScrollingGif(text, font, width, height, new RectangleF(boxX, boxY, boxW, boxH), backgroundPath, bgColor, textColor, isHardcore, token);
                                _logger.LogInformation($"[RP Debug] GIF Path: {gifResult.Path} at {gifResult.X}:{gifResult.Y}");
                                return gifResult;
                            }

                            var textSize = g.MeasureString(text, font, new SizeF(boxW - 20, boxH - 10), format);
                            
                            if (bgColor.A > 0)
                            {
                                using (var boxBrush = new SolidBrush(bgColor))
                                {
                                    g.FillRectangle(boxBrush, boxX, boxY, boxW, boxH); 
                                }
                            }

                            // 4. Draw Text centered in Box
                            RectangleF centeredRect = new RectangleF(boxX, boxY, boxW, boxH);

                            // Shadow
                            using (var shadowBrush = new SolidBrush(Color.FromArgb(150, 0, 0, 0)))
                            {
                                RectangleF shadowRect = centeredRect;
                                shadowRect.Offset(2, 2);
                                g.DrawString(text, font, shadowBrush, shadowRect, format);
                            }

                            using (var textBrush = new SolidBrush(textColor))
                            {
                                g.DrawString(text, font, textBrush, centeredRect, format);
                            }
                        }
                    }
                    bitmap.Save(targetPath, ImageFormat.Png);
                }

                result.Path = targetPath;
                result.X = 0;
                result.Y = 0; // PNG are still fullscreen for now to keep it simple
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[ImageConversion] Failed to generate RP overlay: {ex.Message}");
                return new OverlayResult();
            }
        }


        /// <summary>
        /// EN: Generate a composite achievement overlay for DMD (Badge + Title + Points) similar to MPV layout
        /// FR: Générer un overlay de succès composite pour DMD (Badge + Titre + Points) similaire au layout MPV
        /// </summary>
        public string GenerateDmdAchievementOverlay(string badgePath, string title, string description, int points, bool isHardcore)
        {
            try
            {
                int width = _config.DmdWidth > 0 ? _config.DmdWidth : 128;
                int height = _config.DmdHeight > 0 ? _config.DmdHeight : 32;

                string folder = isHardcore ? "hc_overlays" : "overlays";
                string outputFolder = Path.Combine(_config.CachePath, folder);
                Directory.CreateDirectory(outputFolder);

                string safeTitle = Sanitize(title);
                string filename = $"dmd_ach_{safeTitle}_{DateTime.Now.Ticks}.png";
                string outputPath = Path.Combine(outputFolder, filename);

                var overlay = GetOverlayItem("unlock", true);
                if (overlay != null && !overlay.IsEnabled) return string.Empty;

                // EN: Check for overflow and generate GIF if needed
                // FR: Vérifier le débordement et générer un GIF si nécessaire
                try
                {
                    using (var bmpCheck = new Bitmap(1, 1))
                    using (var grCheck = Graphics.FromImage(bmpCheck))
                    {
                        PrivateFontCollection? pfcCheck = null;
                        FontFamily? famCheck = null;
                        try
                        {
                            string fontName = _config.RAFontFamily;
                            if (string.IsNullOrEmpty(fontName)) fontName = "Arial";
                            if (System.Text.RegularExpressions.Regex.IsMatch(title, @"\p{Cs}|[\u2000-\u3300]") && IsFontInstalled("Segoe UI Emoji"))
                                fontName = "Segoe UI Emoji";

                            famCheck = GetFontFamilySafe(fontName, out pfcCheck);
                            
                            int hCheck = height > 0 ? height : 32;
                            int titleSizeCheck = hCheck < 64 ? 7 : 10;
                            using (var fCheck = new Font(famCheck, titleSizeCheck, FontStyle.Bold))
                            {
                                int bSize = hCheck - 4;
                                int tX = 2 + bSize + 4;
                                int tW = width - tX - 2;
                                
                                if (grCheck.MeasureString(title, fCheck).Width > tW)
                                {
                                    string gif = GenerateDmdScrollingAchievementGif(badgePath, title, description, points, isHardcore, width, height);
                                    if (!string.IsNullOrEmpty(gif) && File.Exists(gif))
                                    {
                                        _logger.LogInformation($"[RA DMD] Title overflow detected. Returning scrolling GIF: {gif}");
                                        return gif;
                                    }
                                }
                            }
                        }
                        finally 
                        {
                            if(pfcCheck!=null) pfcCheck.Dispose();
                            else famCheck?.Dispose();
                        }
                    }
                }
                catch (Exception ex)
                {
                     _logger.LogWarning($"[RA DMD] Error checking text overflow: {ex.Message}");
                }

                using (var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb))
                using (var g = Graphics.FromImage(bitmap))
                {
                    // Setup Quality
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.SmoothingMode = SmoothingMode.AntiAlias; // Use AntiAlias for shapes/lines even on DMD for cleaner look downscaled
                    g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit; // Pixel perfect text for low res

                    // 1. Background (Dark Gray)
                    g.Clear(Color.FromArgb(255, 40, 40, 40));

                    // 2. Draw Border
                    Color borderColor = isHardcore ? Color.Gold : Color.Silver;
                    using (var pen = new Pen(borderColor, 1))
                    {
                        g.DrawRectangle(pen, 0, 0, width - 1, height - 1);
                    }

                    // 3. Draw Badge (Left Side)
                    // margin 2px
                    int badgeSize = height - 4; // 28px on 32h
                    int badgeX = 2;
                    int badgeY = 2;

                    DrawBadgeWithFrame(g, badgePath, badgeX, badgeY, badgeSize, isHardcore, true);

                    // 4. Draw Text (Right Side)
                    int textX = badgeX + badgeSize + 4;
                    int textW = width - textX - 2;

                    // Fonts
                    // Title: Bold, slightly larger
                    // Points: Regular/Bold, smaller, yellow/gold
                    
                    int titleSize = height < 64 ? 7 : 10;
                    int pointsSize = height < 64 ? 6 : 9;

                    var fontNameOrPath = _config.RAFontFamily;
                    // Emoji detection for DMD Title
                    bool containsEmoji = System.Text.RegularExpressions.Regex.IsMatch(title, @"\p{Cs}|[\u2000-\u3300]");
                    if (containsEmoji && IsFontInstalled("Segoe UI Emoji"))
                    {
                        fontNameOrPath = "Segoe UI Emoji";
                    }

                    if (string.IsNullOrEmpty(fontNameOrPath)) fontNameOrPath = "Arial";
                    
                    PrivateFontCollection? pfc = null;
                    FontFamily? family = null;

                    try
                    {
                        family = GetFontFamilySafe(fontNameOrPath, out pfc);
                        
                        using (var titleFont = new Font(family, titleSize, FontStyle.Bold))
                        using (var pointsFont = new Font(family, pointsSize, FontStyle.Regular))
                        using (var titleBrush = new SolidBrush(borderColor)) // Use border color (Silver/Gold) for Title
                        using (var pointsBrush = new SolidBrush(Color.Gold)) // Always Gold for points
                        {
                            var format = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Near, FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.EllipsisCharacter };
                            
                            // Title Rect (Top half)
                            RectangleF titleRect = new RectangleF(textX, 2, textW, height / 2);
                            
                            // Points Rect (Bottom half)
                            RectangleF pointsRect = new RectangleF(textX, height / 2 + 1, textW, height / 2);

                            g.DrawString(title, titleFont, titleBrush, titleRect, format);
                            g.DrawString($"{points} pts", pointsFont, pointsBrush, pointsRect, format);
                        }
                    }
                    finally
                    {
                        if (pfc != null) pfc.Dispose();
                        else family?.Dispose();
                    }
                    
                    bitmap.Save(outputPath, ImageFormat.Png);
                }

                if (!string.IsNullOrEmpty(outputPath) && File.Exists(outputPath))
                {
                    _logger.LogInformation($"[RA DMD] Generated achievement overlay: {outputPath}");
                    return outputPath;
                }
                return string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[RA DMD] Error generating achievement overlay: {ex.Message}");
                return string.Empty;
            }
        }

        /// <summary>
        /// EN: Generate a scrolling GIF for DMD achievement with long title
        /// FR: Générer un GIF défilant pour succès DMD avec titre long
        /// </summary>
        private string GenerateDmdScrollingAchievementGif(string badgePath, string title, string description, int points, bool isHardcore, int width, int height)
        {
            try
            {
                // Cache check
                string safeTitle = Sanitize(title);
                string folder = isHardcore ? "hc_overlays" : "overlays";
                string outputFolder = Path.Combine(_config.CachePath, folder);
                Directory.CreateDirectory(outputFolder);
                string gifFilename = $"dmd_ach_scroll_{safeTitle}_{DateTime.Now.Ticks}.gif";
                string gifPath = Path.Combine(outputFolder, gifFilename);

                // Setup Base Layout (Static)
                using (var baseBmp = new Bitmap(width, height, PixelFormat.Format32bppArgb))
                using (var g = Graphics.FromImage(baseBmp))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;

                    // 1. Background
                    g.Clear(Color.FromArgb(255, 40, 40, 40));

                    // 2. Border
                    Color borderColor = isHardcore ? Color.Gold : Color.Silver;
                    using (var pen = new Pen(borderColor, 1))
                    {
                        g.DrawRectangle(pen, 0, 0, width - 1, height - 1);
                    }

                    // 3. Badge
                    int badgeSize = height - 4;
                    int badgeX = 2;
                    int badgeY = 2;
                    DrawBadgeWithFrame(g, badgePath, badgeX, badgeY, badgeSize, isHardcore, true);

                    // 4. Points (Static)
                    int textX = badgeX + badgeSize + 4;
                    int textW = width - textX - 2;
                    int pointsSize = height < 64 ? 6 : 9;
                    
                    var fontNameOrPath = _config.RAFontFamily;
                    if (string.IsNullOrEmpty(fontNameOrPath)) fontNameOrPath = "Arial";
                    if (IsFontInstalled("Segoe UI Emoji")) fontNameOrPath = "Segoe UI Emoji"; // Safer

                    PrivateFontCollection? pfc = null;
                    FontFamily? family = GetFontFamilySafe(fontNameOrPath, out pfc);
                    
                    try
                    {
                        using (var pointsFont = new Font(family, pointsSize, FontStyle.Regular))
                        using (var pointsBrush = new SolidBrush(Color.Gold))
                        {
                            var format = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Near, FormatFlags = StringFormatFlags.NoWrap };
                            RectangleF pointsRect = new RectangleF(textX, height / 2 + 1, textW, height / 2);
                            g.DrawString($"{points} pts", pointsFont, pointsBrush, pointsRect, format);
                        }

                        // 5. Calculate Title Scrolling
                        int titleSize = height < 64 ? 7 : 10;
                         using (var titleFont = new Font(family, titleSize, FontStyle.Bold))
                         using (var titleBrush = new SolidBrush(borderColor))
                         {
                             // Measure Title
                             var textSize = g.MeasureString(title, titleFont);
                             float titleW = textSize.Width;
                             
                             // SCROLL GENERATION (FFmpeg)
                             string ffmpegPath = FindFfmpeg();
                             if (string.IsNullOrEmpty(ffmpegPath)) return string.Empty;

                             var startInfo = new ProcessStartInfo
                             {
                                FileName = ffmpegPath,
                                Arguments = $"-y -f image2pipe -framerate 30 -i - -filter_complex \"[0:v] split [a][b];[a] palettegen=reserve_transparent=0 [p];[b][p] paletteuse\" -loop 0 \"{gifPath}\"",
                                RedirectStandardInput = true,
                                UseShellExecute = false,
                                CreateNoWindow = true,
                                RedirectStandardOutput = true,
                                RedirectStandardError = true
                             };

                             var proc = Process.Start(startInfo);
                             if (proc == null) return string.Empty;
                             
                             RectangleF titleRect = new RectangleF(textX, 2, textW, height / 2);
                             int speed = 2; // Slower for DMD
                             int totalFrames = (int)((textW + titleW) / speed) + 20;
                             if (totalFrames > 300) { speed = 4; totalFrames = (int)((textW + titleW) / speed) + 20; }

                             using (var stdin = proc.StandardInput.BaseStream)
                             using (var frameBmp = new Bitmap(width, height, PixelFormat.Format32bppArgb))
                             using (var gf = Graphics.FromImage(frameBmp))
                             {
                                 gf.InterpolationMode = InterpolationMode.HighQualityBicubic;
                                 gf.SmoothingMode = SmoothingMode.AntiAlias;
                                 gf.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
                                 
                                 var format = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Near, FormatFlags = StringFormatFlags.NoWrap };

                                 for(int i=0; i<totalFrames; i++)
                                 {
                                     // Draw Base
                                     gf.DrawImage(baseBmp, 0, 0);
                                     
                                     // Clip to Title Area
                                     gf.SetClip(titleRect);
                                     
                                     float currentX = (titleRect.Right - 5) - (i * speed);
                                     
                                     // Draw Title
                                     // Shadow for legibility
                                     using(var shadowBrush = new SolidBrush(Color.Black))
                                     {
                                         gf.DrawString(title, titleFont, shadowBrush, currentX + 1, titleRect.Y + 1, format);
                                     }
                                     gf.DrawString(title, titleFont, titleBrush, currentX, titleRect.Y, format);
                                     
                                     gf.ResetClip();
                                     
                                     frameBmp.Save(stdin, ImageFormat.Png);
                                 }
                             }
                             
                             proc.WaitForExit(15000);
                             if (proc.ExitCode == 0 && File.Exists(gifPath)) return gifPath;
                         }
                    }
                    finally
                    {
                        if (pfc != null) pfc.Dispose();
                        else family?.Dispose();
                    }
                }
                return string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[RA DMD] Error generating scrolling achievement GIF: {ex.Message}");
                return string.Empty;
            }
        }


        public string CleanRichPresenceString(string text, bool stripEmojis)
        {
             if (string.IsNullOrWhiteSpace(text)) return string.Empty;
             
             // 1. Filter out zero-value segments (e.g. "Gems x0", "Lives: 0")
             // EN: Split by | or , / FR: DÃ©couper par | ou ,
             var parts = text.Split(new[] { '|', ',' }, StringSplitOptions.RemoveEmptyEntries);
             var filteredParts = parts.Where(p => 
             {
                 var trim = p.Trim();
                 // Return false if matches "0" value pattern / FR: Retourne faux si correspond au pattern de valeur "0"
                 return !System.Text.RegularExpressions.Regex.IsMatch(trim, @"(?:\s*[:x]\s*)0\s*$");
             });
             
             var result = string.Join(", ", filteredParts.Select(p => p.Trim()));

             // 2. Strip standard emoji chars if requested (for DMD)
             if (stripEmojis)
             {
                 // Remove anything above U+FFFF (Surrogates which include most emojis)
                 result = System.Text.RegularExpressions.Regex.Replace(result, @"[^\u0000-\uFFFF]", "");
             }

             return result;
        }

        /// <summary>
        /// EN: Generate a styled overlay for an individual Rich Presence item (Key: Value)
        /// FR: GÃ©nÃ©rer un overlay stylisÃ© pour un Ã©lÃ©ment individuel de Rich Presence (Clef: Valeur)
        /// </summary>
        public string GenerateRichPresenceItemOverlay(string key, string value, bool isHardcore, int canvasWidth, int canvasHeight, bool isDmd = false, bool isScore = false, string alignment = "top-left", int yOffset = 0, bool forceRegenerate = false, CancellationToken token = default)
        {
            PrivateFontCollection? pfc = null;
            FontFamily? family = null;

            try
            {
                string overlayFolder = isHardcore ? "hc_overlays" : "overlays";
                string outputFolder = Path.Combine(_config.CachePath, overlayFolder);
                Directory.CreateDirectory(outputFolder);

                string safeKey = Sanitize(key);
                string suffix = isDmd ? "dmd" : "mpv";
                string fileName = $"rp_item_{safeKey}_{suffix}_{DateTime.Now.Ticks}.png";
                string outputPath = Path.Combine(outputFolder, fileName);

                using (var bitmap = new Bitmap(canvasWidth, canvasHeight, PixelFormat.Format32bppArgb))
                using (var g = Graphics.FromImage(bitmap))
                {
                    g.Clear(Color.Transparent);
                    
                    // 1. Determine Layout Type Early
                    string layoutType = isScore ? "rp_score" : "stat";
                    if (!isScore)
                    {
                        string lowerKey = key.ToLowerInvariant();
                        if (lowerKey.Contains("live") || lowerKey.Contains("♥") || lowerKey.Contains("vies")) layoutType = "rp_lives";
                        else if (lowerKey.Contains("score") || lowerKey.Contains("💵")) layoutType = "rp_score";
                        else if (lowerKey.Contains("narrat")) layoutType = "rp_narration";
                        else if (lowerKey.Contains("weapon") || lowerKey.Contains("arme") || lowerKey.Contains("🔫")) layoutType = "rp_weapon";
                        else if (lowerKey.Contains("lap") || lowerKey.Contains("tour")) layoutType = "rp_lap";
                        else if (lowerKey.Contains("rank") || lowerKey.Contains("pos")) layoutType = "rp_rank";
                        else layoutType = "rp_stat";
                    }

                    // EN: Content formatting / FR: Formatage du contenu
                    // EN: Use value only for stats/weapons, add heart for lives
                    // FR: Utiliser la valeur seule pour stats/armes, ajouter un coeur pour les vies
                    string text = value;
                    if (layoutType == "rp_lives" && !value.Contains("♥")) text = $"{value}♥";
                    else if (layoutType == "rp_stat")
                    {
                        // EN: Check for Emoji in key
                        if (System.Text.RegularExpressions.Regex.IsMatch(key, @"\p{Cs}|[\u2000-\u3300]"))
                        {
                            text = $"{key} {value}"; 
                        }
                        else
                        {
                            text = value; // Text keys too long, show only value
                        }
                    }
                    else if (layoutType == "rp_score" || layoutType == "rp_narration" || layoutType == "rp_weapon" || layoutType == "rp_lap" || layoutType == "rp_rank") text = value;
                    else if (!isScore) text = $"{key}: {value}"; // Fallback for other items

                    var overlay = GetOverlayItem(layoutType, isDmd);
                    if (overlay != null && !overlay.IsEnabled) return string.Empty;
                    _logger.LogInformation($"[RP Item] Generating '{key}' (Type: {layoutType}) - Overlay found: {overlay != null}");

                    // 2. Select Font (Scaled if Overlay exists, Default if not)
                    Font? font = null;
                    int defaultFontSize = isDmd ? (canvasHeight < 64 ? 7 : 10) : Math.Max(10, canvasHeight / 24);
                    var fontStyle = FontStyle.Bold;
                    
                    // Resolve Font Family safely
                    string fontNameOrPath = _config.RAFontFamily;
                    bool containsEmoji = System.Text.RegularExpressions.Regex.IsMatch(text, @"\p{Cs}|[\u2000-\u3300]");
                    if (containsEmoji && IsFontInstalled("Segoe UI Emoji"))
                    {
                        fontNameOrPath = "Segoe UI Emoji";
                    }

                    family = GetFontFamilySafe(fontNameOrPath, out pfc);

                    if (overlay != null)
                    {
                         if (overlay.FontSize > 0)
                         {
                             font = new Font(family, overlay.FontSize, fontStyle);
                         }
                         else
                         {
                             // Auto-scale to fit overlay width
                             int padding = isDmd ? 4 : 16;
                             int maxF = isDmd ? (canvasHeight < 64 ? 8 : 12) : defaultFontSize + 6;
                             int minF = isDmd ? 6 : 8;
                             
                             font = GetAdjustedFont(g, text, family, maxF, minF, overlay.Width - padding, overlay.Height - padding, fontStyle);
                         }
                    }
                    else
                    {
                         font = new Font(family, defaultFontSize, fontStyle);
                    }

                    using (font)
                    {
                        g.TextRenderingHint = isDmd ? TextRenderingHint.SingleBitPerPixelGridFit : TextRenderingHint.AntiAliasGridFit;
                        g.SmoothingMode = SmoothingMode.AntiAlias;

                        var textSize = g.MeasureString(text, font);
                        
                        int paddingX = isDmd ? 2 : 8;
                        int paddingY = isDmd ? 2 : 10;
                        
                        int boxWidth = (int)textSize.Width + (paddingX * 2);
                        int boxHeight = (int)textSize.Height + (paddingY * 2);

                        // LayoutType already calculated above
                        // Overlay already retrieved above

                        int x = 20, y = 20;
                        int finalBoxWidth = boxWidth;
                        int finalBoxHeight = boxHeight;

                        if (overlay != null)
                        {
                            x = overlay.X;
                            y = overlay.Y;
                            // EN: Auto-expand frame if text is too large to avoid truncation (height and width)
                            // FR: Agrandir automatiquement le cadre si le texte est trop grand pour éviter la troncature
                            finalBoxWidth = Math.Max(overlay.Width, boxWidth);
                            finalBoxHeight = Math.Max(overlay.Height, boxHeight);
                        }
                        else
                        {
                            // EN: Orchestrate alignment / FR: Orchestrer l'alignement
                            int margin = isDmd ? 2 : 20;

                            switch (alignment.ToLowerInvariant())
                            {
                                case "top-right":
                                    x = canvasWidth - boxWidth - margin;
                                    y = margin + yOffset;
                                    break;
                                case "top-left":
                                    x = margin;
                                    y = margin + yOffset;
                                    break;
                                case "bottom-right":
                                    x = canvasWidth - boxWidth - margin;
                                    y = canvasHeight - boxHeight - margin - yOffset;
                                    // EN: If not DMD, move up to avoid badges / FR: Si pas DMD, monter pour Ã©viter badges
                                    if (!isDmd)
                                    {
                                        float badgeAreaHeight = canvasHeight / 7.2f;
                                        y -= (int)badgeAreaHeight;
                                    }
                                    break;
                                case "bottom-left":
                                    x = margin;
                                    y = canvasHeight - boxHeight - margin - yOffset;
                                    if (!isDmd)
                                    {
                                        float badgeAreaHeight = canvasHeight / 7.2f;
                                        y -= (int)badgeAreaHeight;
                                    }
                                    break;
                                case "center":
                                    x = (canvasWidth - boxWidth) / 2;
                                    y = (canvasHeight - boxHeight) / 2 + yOffset;
                                    break;
                            }
                        }

                        // EN: Draw Background
                        Color bgColor = Color.FromArgb(180, 40, 40, 40); // Default semi-transparent grey
                        if (layoutType.Contains("narration")) bgColor = Color.FromArgb(180, 40, 40, 40); // Force gray for narration
                        using (var brushBg = new SolidBrush(bgColor))
                        {
                            g.FillRectangle(brushBg, x, y, finalBoxWidth, finalBoxHeight);
                        }

                        Color textColor = GetColorFromHex(overlay?.TextColor ?? "", Color.Gold);
                        _logger.LogInformation($"[RP Item] '{key}' Color: {textColor} (Config: {overlay?.TextColor})");
                        // EN: For MPV, we always draw a border (Gold/Silver)
                        // FR: Pour MPV, on dessine toujours une bordure (Or/Argent)
                        Color borderColor = (isHardcore ? Color.Gold : Color.Silver);
                        // EN: REMOVED: Do not override border color with text color on DMD
                        // FR: RETIRÉ : Ne pas écraser la couleur de bordure avec la couleur du texte sur DMD
                        // if (isDmd) borderColor = Color.FromArgb(200, textColor.R, textColor.G, textColor.B);

                        using (var pen = new Pen(borderColor, isDmd ? 1 : 2))
                        {
                            g.DrawRectangle(pen, x, y, finalBoxWidth, finalBoxHeight);
                        }

                        // EN: Draw Text / FR: Dessiner le Texte
                        using (var brush = new SolidBrush(textColor))
                        {
                            // EN: Always Center text in box for RP items
                            // FR: Toujours centrer le texte dans la boite pour les items RP
                             var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                             
                             // EN: Check if text fits (Measure without constraint to get real width)
                             // FR: Vérifier si le texte tient (Mesurer sans contrainte pour avoir la largeur réelle)
                             var textSizeForGif = g.MeasureString(text, font, new PointF(0, 0), StringFormat.GenericTypographic);
                             bool textFits = textSizeForGif.Width <= finalBoxWidth - 10;
                             
                             // EN: If MPV (Not DMD) and Narration (relaxed check) and Text doesn't fit -> Generate Scrolling GIF
                             // FR: Si MPV (Pas DMD) et Narration (vérif souple) et Texte ne tient pas -> Générer GIF défilant
                             if (!isDmd && layoutType.Contains("narration") && !textFits)
                             {
                                 // Close existing bitmap context to release file lock potential (though we haven't saved yet)
                                 // Actually we need to return a different file path.
                                 // We can't easily abort this using block, but we can skip saving this bitmap and generate the GIF instead.
                                 string gifPath = GenerateScrollingTextGif(text, font, finalBoxWidth, finalBoxHeight, bgColor, textColor, isHardcore, token);
                                 if (!string.IsNullOrEmpty(gifPath))
                                 {
                                     return gifPath;
                                 }
                                 // EN: Verify log if failed / FR: Vérifier log si échoué
                                 _logger.LogWarning($"[ImageConversion] Scrolling GIF generation failed for '{text}', using static fallback.");
                             }
                             
                             Rectangle rect = new Rectangle(x, y, finalBoxWidth, finalBoxHeight);
                             DrawStringWithOutline(g, text, font, brush, rect, format, isDmd);
                        }

                        bitmap.Save(outputPath, ImageFormat.Png);
                    }
                    return outputPath;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[ImageConversion] Error generating RP individual item: {ex.Message}");
                return string.Empty;
            }
            finally
            {
                if (pfc != null) pfc.Dispose();
                else family?.Dispose();
            }
        }

        /// <summary>
        /// EN: Parse Rich Presence text into structured Narrative/Stats data
        /// FR: DÃ©coder le texte Rich Presence en donnÃ©es structurÃ©es Narration/Stats
        /// </summary>
        public RichPresenceState ParseRichPresence(string rpText)
        {
            var state = new RichPresenceState { RawText = rpText };
            if (string.IsNullOrWhiteSpace(rpText)) return state;

            var statKeysToTarget = new[] { "lives", "score", "weapon", "arme", "ammo", "gems", "keys", "difficulty", "mode", "lap", "tour", "rank", "pos" };

            // EN: Step 1 - Try to extract Key: Value pairs using a robust regex (Colon/x separated)
            // FR: Étape 1 - Essayer d'extraire les paires Clef: Valeur via une regex robuste (séparé par deux-points/x)
            // Refined to disallow "Dot Space" in keys (prevents "Overworld. Lives:0" -> Key="Overworld. Lives")
            // Also disallow '-' in keys to prevent "Level 1-1 - Lives:3" -> Key="Level 1-1 - Lives"
            var matches = System.Text.RegularExpressions.Regex.Matches(rpText, 
                @"(?<key>(?:[^|:,\n.-]|\.(?!\s))+?)\s*[:x]\s*(?<value>.*?)(?=\s*(?:[|]|\u00B7|,\s*[^|:,\n]+?[:x]|$))", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            var matchedIndices = new List<(int, int)>();

            // EN: Common robust emoji/surrogate regex patterns
            // FR: Patterns regex robustes pour émojis/surrogates
            // Matches any Surrogate Pair (High Surrogate D800-DBFF + Low Surrogate DC00-DFFF) or strictly high surrogates for detection
            string surrogatePattern = @"[\uD800-\uDFFF]";
            
            // Map Emojis: Map, Round Pushpin, Triangular Flag, Earths, Castle, Cityscape, Compass
            // 🗺️ \uD83D\uDDFA
            // 📍 \uD83D\uDCCD
            // 🚩 \uD83D\uDEA9
            // 🌍 \uD83C\uDF0D
            // 🌎 \uD83C\uDF0E
            // 🌏 \uD83C\uDF0F
            // 🏰 \uD83C\uDFF0
            // 🏙️ \uD83C\uDFD9
            // 🧭 \uD83E\uDDED
            string mapEmojiPattern = @"\uD83D\uDDFA|\uD83D\uDCCD|\uD83D\uDEA9|\uD83C\uDF0D|\uD83C\uDF0E|\uD83C\uDF0F|\uD83C\uDFF0|\uD83C\uDFD9|\uD83E\uDDED";

            foreach (System.Text.RegularExpressions.Match match in matches)
            {
                var key = match.Groups["key"].Value.Trim();
                var val = match.Groups["value"].Value.Trim();
                var lowKey = key.ToLowerInvariant();
                
                // EN: Detect Emoji Keys using Surrogate range (more reliable in .NET for string handling)
                bool isEmojiKey = System.Text.RegularExpressions.Regex.IsMatch(key, surrogatePattern);
                
                // EN: Exclude Map-like emojis from stats so they remain in narrative
                // FR: Exclure les émojis de type Carte des stats pour qu'ils restent dans la narration
                bool isMapEmoji = System.Text.RegularExpressions.Regex.IsMatch(key, mapEmojiPattern);

                // If it's an Emoji Key BUT NOT a Map Emoji, OR it's a known Stat Key -> Extract to Stats
                if ((isEmojiKey && !isMapEmoji) || statKeysToTarget.Any(k => lowKey.Contains(k)))
                {
                    state.Stats[key] = val;
                }
            }

            // EN: Step 2 - Extract segments that were NOT identified as generic stats as Narrative
            var narrativeDraft = rpText;
            foreach (System.Text.RegularExpressions.Match match in matches)
            {
                var key = match.Groups["key"].Value;
                var lowKey = key.ToLowerInvariant();
                
                bool isEmojiKey = System.Text.RegularExpressions.Regex.IsMatch(key, surrogatePattern);
                bool isMapEmoji = System.Text.RegularExpressions.Regex.IsMatch(key, mapEmojiPattern);

                if ((isEmojiKey && !isMapEmoji) || statKeysToTarget.Any(k => lowKey.Contains(k)))
                {
                    narrativeDraft = narrativeDraft.Replace(match.Value, "");
                }
            }

            // EN: Step 3 - Special Pass for Space-Separated Stats (Lap, Rank) which don't use colons
            // FR: Étape 3 - Passe spéciale pour les stats séparées par espace (Lap, Rank) sans deux-points
            // Regex: Word (Lap/Rank) + Space + Value (digits/fraction)
            var specialMatches = System.Text.RegularExpressions.Regex.Matches(narrativeDraft,
                @"\b(?<key>Lap|Tour|Rank|Pos|Rank\s+in|Pos\s+in)\s+(?<value>\d+(?:\s*/\s*\d+)?)\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            foreach (System.Text.RegularExpressions.Match match in specialMatches)
            {
                var key = match.Groups["key"].Value.Trim();
                var val = match.Groups["value"].Value.Trim();
                
                // EN: Simplify key name for internal mapping (Rank in -> Rank)
                // FR: Simplifier le nom de clé pour le mapping interne
                var simpleKey = key;
                if (key.StartsWith("Rank", StringComparison.OrdinalIgnoreCase)) simpleKey = "Rank";
                if (key.StartsWith("Pos", StringComparison.OrdinalIgnoreCase)) simpleKey = "Pos";

                state.Stats[simpleKey] = val; // Add to stats
                narrativeDraft = narrativeDraft.Replace(match.Value, ""); // Remove from narrative
            }

            // EN: Step 4 - Special Pass for Crystals and Gems (Natural Language: "with X crystals and Y gems")
            // FR: Étape 4 - Passe spéciale pour Cristaux et Gemmes (Langage naturel : "with X crystals and Y gems")
            var crystalGemMatches = System.Text.RegularExpressions.Regex.Matches(narrativeDraft,
                @"\b(?<value>\d+)\s+(?<type>crystals?|gems?)\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            foreach (System.Text.RegularExpressions.Match match in crystalGemMatches)
            {
                var val = match.Groups["value"].Value;
                var type = match.Groups["type"].Value.ToLowerInvariant();
                
                // EN: Use Emojis for keys (Crystals=🔮, Gems=💎) so they fall into rp_stat logic (Key + Value)
                // FR: Utiliser des Emojis pour les clés (Crystals=🔮, Gems=💎) pour qu'ils tombent dans la logique rp_stat (Clé + Valeur)
                var key = type.StartsWith("crystal") ? "🔮" : "💎";

                state.Stats[key] = val;
                
                // EN: Remove the match from narrative (e.g. "0 gems")
                // FR: Supprimer la correspondance de la narration
                narrativeDraft = narrativeDraft.Replace(match.Value, "");
            }
            
            // EN: Cleanup "with" and "and" leftovers from narrative if they become isolated
            // FR: Nettoyer les "with" et "and" restants dans la narration s'ils deviennent isolés
            narrativeDraft = System.Text.RegularExpressions.Regex.Replace(narrativeDraft, @"\bwith\s*(?=\s|$)", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            narrativeDraft = System.Text.RegularExpressions.Regex.Replace(narrativeDraft, @"\band\s*(?=\s|$)", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            narrativeDraft = System.Text.RegularExpressions.Regex.Replace(narrativeDraft, @"\bwith\s+and\b", "with", System.Text.RegularExpressions.RegexOptions.IgnoreCase); // Fix "with and"


            // EN: Clean up separators left behind (include '·' \u00B7)
            // FR: Nettoyer les séparateurs restants (incluant '·')
            // EN: Use Regex split to handle separators more intelligently
            // Split on |, ,, · OR dash (-) ONLY if it's not between two word characters (protects 1-1, X-Men)
            var parts = System.Text.RegularExpressions.Regex.Split(narrativeDraft, @"\s*[|,·]\s*|(?<!\w)\s*-\s*|\s*-\s*(?!\w)");

            var narrativeParts = parts
                .Select(p => p.Trim())
                .Where(p => !string.IsNullOrEmpty(p) && p.Length > 1) // Filter tiny artifacts
                .ToList();

            state.Narrative = string.Join(" · ", narrativeParts); // Re-join cleanly
            return state;
        }

        /// <summary>
        /// EN: Public entry point to cleanup old cache files from both overlay folders
        /// FR: Point d'entrée public pour nettoyer les anciens fichiers cache des dossiers d'overlay
        /// </summary>
        public void CleanupCache(bool forceFull = false)
        {
            _logger.LogInformation($"[ImageConversion] Explicit cache cleanup triggered (ForceFull: {forceFull}).");
            CleanupOldOverlays(Path.Combine(_config.CachePath, "overlays"), forceFull);
            CleanupOldOverlays(Path.Combine(_config.CachePath, "hc_overlays"), forceFull);
        }

        private void CleanupOldOverlays(string cacheDir, bool forceFull = false)
        {
            try 
            {
                if (!Directory.Exists(cacheDir)) return;
                var dirInfo = new DirectoryInfo(cacheDir);
                
                // EN: Search for both PNG and GIF files
                // FR: Rechercher les fichiers PNG et GIF
                var files = dirInfo.GetFiles("*.png").Concat(dirInfo.GetFiles("*.gif")).ToArray();
                var threshold = DateTime.Now.AddMinutes(-5); // Keep last 5 minutes by default

                foreach (var f in files)
                {
                    if (forceFull || f.CreationTime < threshold)
                    {
                        try { f.Delete(); } catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[ImageConversion] Cleanup error in {cacheDir}: {ex.Message}");
            }
        }
        
        private bool IsFontInstalled(string fontName)
        {
            using (var fonts = new InstalledFontCollection())
            {
                return fonts.Families.Any(f => f.Name.Equals(fontName, StringComparison.OrdinalIgnoreCase));
            }
        }
        private OverlayItem? GetOverlayItem(string overlayType, bool isDmd)
        {
            return _templateService.GetItem(isDmd ? "dmd" : "mpv", overlayType);
        }

        /// <summary>
        /// EN: Generates a single DMD frame containing ALL active overlay elements for preview
        /// FR: GÃ©nÃ¨re une frame DMD unique contenant TOUS les Ã©lÃ©ments d'overlay actifs pour la prÃ©visualisation
        /// </summary>
        private FontFamily GetFontFamilySafe(string nameOrPath, out PrivateFontCollection? pfc)
        {
            pfc = null;
            if (!string.IsNullOrEmpty(nameOrPath) && File.Exists(nameOrPath))
            {
                 try {
                     pfc = new PrivateFontCollection();
                     pfc.AddFontFile(nameOrPath);
                     return pfc.Families[0];
                 } catch {
                     pfc?.Dispose();
                     pfc = null;
                 }
            }
            // Fallback safe
            try { return new FontFamily(nameOrPath ?? "Arial"); }
            catch { return FontFamily.GenericSansSerif; }
        }

        /// <summary>
        /// EN: Generates a single DMD frame containing ALL active overlay elements for preview
        /// FR: GÃ©nÃ¨re une frame DMD unique contenant TOUS les Ã©lÃ©ments d'overlay actifs pour la prÃ©visualisation
        /// </summary>
        private Font GetAdjustedFont(Graphics g, string text, FontFamily fontFamily, int maxSize, int minSize, int width, int height, FontStyle style)
        {
            // EN: Iteratively reduce font size until text fits both width and height
            // FR: Réduire itérativement la taille de la police jusqu'à ce que le texte tienne en largeur et en hauteur
            for (int size = maxSize; size >= minSize; size--)
            {
                var font = new Font(fontFamily, size, style);
                var textSize = g.MeasureString(text, font);
                if (textSize.Width <= width && textSize.Height <= height) return font;
                font.Dispose();
            }
            return new Font(fontFamily, minSize, style);
        }

        public byte[] GenerateDmdFullPreviewFrame(int width, int height, bool useGrayscale)
        {
            try
            {
                using (var bitmap = new Bitmap(width, height))
                using (var g = Graphics.FromImage(bitmap))
                {
                    g.Clear(Color.Black);
                    g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;

                    // 1. Get current layout
                    var layout = _templateService.GetLayout();
                    
                    // Sort items by ZOrder to respect layering in preview
                    var items = layout.DmdItems.OrderBy(x => x.Value.ZOrder).ToList();

                    // Pre-generate sample data
                    // We reuse the existing generation methods to ensure WYSIWYG preview (fonts, sizes, borders)
                    
                    bool challengeShown = false;
                    foreach (var kvp in items)
                    {
                        var type = kvp.Key;
                        var item = kvp.Value;
                        if (!item.IsEnabled) continue;

                        string? imagePath = null;
                        
                        try
                        {
                            switch (type.ToLowerInvariant())
                            {
                                case "count": 
                                    imagePath = GenerateAchievementCountOverlay(15, 35, true, false, true); 
                                    break;
                                case "score": 
                                    imagePath = GenerateScoreOverlay(123456, 999999, true, false, true); 
                                    break;
                                case "badges":
                                    // Complex to mock badges without RaService, skip or draw placeholder
                                    // For now draw placeholder rect below
                                    break;
                                case "unlock":
                                    // Unlock is usually transient, maybe skip or show placeholder
                                    break;
                                case "challenge":
                                    if (challengeShown) continue;
                                    imagePath = GenerateDmdChallengeImage(new ChallengeState { IsActive = true, Type = ChallengeType.Progress, Title = "Challenge", CurrentValue = 5, TargetValue = 10, BadgePath = "dummy.png" }, width, height, useGrayscale);
                                    challengeShown = true;
                                    break;
                                case "rp_score":
                                    imagePath = GenerateRichPresenceItemOverlay("Score", "$: 99,999", false, width, height, true, true, "top-right", 0, true);
                                    break;
                                case "rp_lives":
                                    imagePath = GenerateRichPresenceItemOverlay("Score", "3", false, width, height, true, false, "top-left", 0, true); // Key Score used for Lives type usually maps to Heart if logic dictates, but let's pass generic
                                     // Actually checking logic: if lowerKey contains "vies" or heart. 
                                    imagePath = GenerateRichPresenceItemOverlay("Vies", "3", false, width, height, true, false, "top-left", 0, true);
                                    break;
                                case "rp_narration":
                                    // Start of string determines type in RP Item overlay check
                                    imagePath = GenerateRichPresenceItemOverlay("Narrat", "Narration Test...", false, width, height, true, false, "bottom", 0, true);
                                    break;
                                case "rp_stat":
                                    imagePath = GenerateRichPresenceItemOverlay("Stat", "Stats: X5", false, width, height, true, false, "bottom-left", 0, true);
                                    break;
                                case "rp_weapon":
                                     imagePath = GenerateRichPresenceItemOverlay("Weapon", "Plasma Gun", false, width, height, true, false, "bottom-left", 0, true);
                                     break;
                                case "rp_lap":
                                     imagePath = GenerateRichPresenceItemOverlay("Lap", "Lap 2/3", false, width, height, true, false, "top-left", 0, true);
                                     break;
                                case "rp_rank":
                                     imagePath = GenerateRichPresenceItemOverlay("Rank", "Pos 1/12", false, width, height, true, false, "top-right", 0, true);
                                     break;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning($"[DMD Preview] Failed to generate preview for {type}: {ex.Message}");
                        }

                        if (!string.IsNullOrEmpty(imagePath) && File.Exists(imagePath))
                        {
                            using (var img = Image.FromFile(imagePath))
                            {
                                // Draw at item coordinates. 
                                // Note: The generated overlays usually include the box at specific coords if they are canvas-sized, OR they are box-sized.
                                // GenerateRichPresenceItemOverlay returns a BOX-sized image if simply generating an item? 
                                // Wait, GenerateRichPresenceItemOverlay returns a BOX sized image at lines 3676 (using canvasWidth/Height).
                                // Actually line 3676: new Bitmap(canvasWidth, canvasHeight)
                                // So it returns a transparency filled FULL canvas image with the item at the calculated position.
                                // BUT, GenerateScoreOverlay returns a BOX sized image or Canvas sized?
                                // GenerateScoreOverlay line 2333: new Bitmap(canvasWidth, canvasHeight) -> Canvas Sized.
                                
                                // So we just draw them at 0,0?
                                // Wait, if the user moves the item in the editor, the `GetOverlayItem` inside `Generate...` picks up the position.
                                // So `Generate...` creates an image with the item ALREADY at X,Y.
                                // So we draw at 0,0.
                                
                                g.DrawImage(img, 0, 0, width, height); 
                            }
                        }
                        else
                        {
                             // Fallback to simple rect for unknown/failed items (badges etc)
                             using (var pen = new Pen(Color.Gray, 1))
                             {
                                 g.DrawRectangle(pen, item.X, item.Y, item.Width, item.Height);
                                 // g.DrawString(type, SystemFonts.DefaultFont, Brushes.Gray, item.X, item.Y);
                             }
                        }
                    }

                    return GetRawDmdBytes(bitmap, width, height, useGrayscale);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[ImageConversion] GenerateDmdFullPreviewFrame Error: {ex.Message}");
                return new byte[0];
            }
        }
        public byte[] GenerateDmdRichPresenceComposition(Dictionary<string, string> stats, int width, int height, bool useGrayscale, int? activeRpStatIndex = null)
        {
            PrivateFontCollection? pfc = null;
            FontFamily? family = null;

            try
            {
                string raFontFamily = _config.RAFontFamily;

                // EN: Check if any stat contains emojis (using robust regex) to force Emoji font if needed
                if (stats.Keys.Any(k => System.Text.RegularExpressions.Regex.IsMatch(k, @"\p{Cs}|[\u2000-\u3300]")) || 
                    stats.Values.Any(v => System.Text.RegularExpressions.Regex.IsMatch(v, @"\p{Cs}|[\u2000-\u3300]")))
                {
                    if (IsFontInstalled("Segoe UI Emoji")) raFontFamily = "Segoe UI Emoji";
                }

                // Resolve font family early
                family = GetFontFamilySafe(raFontFamily, out pfc);

                using (var surface = new Bitmap(width, height, PixelFormat.Format32bppArgb))
                using (var g = Graphics.FromImage(surface))
                {
                    g.Clear(Color.Transparent);
                    g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;

                    if (useGrayscale)
                    {
                         _logger.LogInformation("[DMD Composition] Generating in Grayscale (ForceMono detected). Colors will be lost.");
                    }

                    // Group stats to avoid overlap / FR: Grouper les stats pour éviter le chevauchement
                    var typeMap = new Dictionary<string, List<string>>();
                    foreach (var kvp in stats)
                    {
                        string lk = kvp.Key.ToLowerInvariant();
                        string type = "rp_stat";
                        if (lk.Contains("score") || lk.Contains("💵")) type = "rp_score";
                        else if (lk.Contains("live") || lk.Contains("♥") || lk.Contains("vies")) type = "rp_lives";
                        else if (lk.Contains("weapon") || lk.Contains("arme") || lk.Contains("🔫")) type = "rp_weapon";
                        else if (lk.Contains("lap") || lk.Contains("tour")) type = "rp_lap";
                        else if (lk.Contains("rank") || lk.Contains("pos")) type = "rp_rank";
                        
                        if (!typeMap.ContainsKey(type)) typeMap[type] = new List<string>();
                        
                        string val = kvp.Value;
                        if (type == "rp_lives") 
                        {
                            val = $"{kvp.Value}♥";
                        }
                        else if (type == "rp_stat")
                        {
                            // EN: Include Key/Icon for generic stats so user knows what it is
                            // FR: Inclure Clé/Icône pour les stats génériques pour que l'utilisateur sache ce que c'est
                            // Check for Emojis (Surrogates + Symbols)
                            bool isEmoji = System.Text.RegularExpressions.Regex.IsMatch(kvp.Key, @"\p{Cs}|[\u2000-\u3300]");
                            if (isEmoji)
                            {
                                val = $"{kvp.Key} {kvp.Value}"; // "💎 5"
                            }
                            else
                            {
                                val = kvp.Value; // EN: Text keys too long, show only value
                            }
                        }
                        
                        typeMap[type].Add(val);
                    }

                    foreach (var entry in typeMap)
                    {
                        string type = entry.Key;
                        string text = "";
                        
                        if (type == "rp_stat")
                        {
                            if (activeRpStatIndex.HasValue && activeRpStatIndex.Value >= 0 && activeRpStatIndex.Value < entry.Value.Count)
                            {
                                // Rotation enabled: show only current index
                                text = entry.Value[activeRpStatIndex.Value];
                            }
                            else
                            {
                                // Join with divider if not rotating or index invalid
                                text = string.Join(" | ", entry.Value);
                            }
                        }
                        else
                        {
                            text = entry.Value.FirstOrDefault() ?? "";
                        }

                        var overlayItem = GetOverlayItem(type, true);
                        if (overlayItem != null && overlayItem.IsEnabled)
                        {
                            int rx = overlayItem.X;
                            int ry = overlayItem.Y;
                            int rw = overlayItem.Width;
                            int rh = overlayItem.Height;
                            // Start of grouped render
                        
                        
                        

                        
                            
                            

                            

                            int maxFontSize = (height < 64 ? 8 : 12); // Slightly larger max
                            int minFontSize = 6;
                            
                            // Get adjusted font that fits
                            Font? font = null;
                            if (overlayItem.FontSize > 0)
                            {
                                font = new Font(family ?? FontFamily.GenericSansSerif, overlayItem.FontSize, FontStyle.Bold);
                            }
                            else
                            {
                                try 
                                { 
                                    font = GetAdjustedFont(g, text, family ?? FontFamily.GenericSansSerif, maxFontSize, minFontSize, overlayItem.Width - 4, overlayItem.Height - 2, FontStyle.Bold);
                                } 
                                catch 
                                { 
                                    font = GetAdjustedFont(g, text, FontFamily.GenericSansSerif, maxFontSize, minFontSize, overlayItem.Width - 4, overlayItem.Height - 2, FontStyle.Bold);
                                }
                            }

                            using (font)
                            {
                                Color textColor = GetColorFromHex(overlayItem.TextColor, Color.Gold);
                                using (var brush = new SolidBrush(textColor))
                                {
                                    var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                                    if (type == "rp_score") format.Alignment = StringAlignment.Far;

                                    RectangleF rect = new RectangleF(rx, ry, rw, rh);
                                    DrawStringWithOutline(g, text, font, brush, rect, format, true);
                                }
                            }
                        }
                    }

                    return GetRawDmdBytes(surface, width, height, useGrayscale);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to generate DMD RP composition: {ex.Message}");
                return new byte[0];
            }
            finally
            {
                if (pfc != null) pfc.Dispose();
                else family?.Dispose();
            }
        }
        /// <summary>
        /// EN: Generate a scrolling text GIF for MPV using FFmpeg
        /// FR: Générer un GIF de texte défilant pour MPV via FFmpeg
        /// </summary>
        private string GenerateScrollingTextGif(string text, Font font, int width, int height, Color bgColor, Color textColor, bool isHardcore, CancellationToken token = default)
        {
            try
            {
                string tempDir = Path.Combine(Path.GetTempPath(), $"retrobat_marquee_gif_frames_{DateTime.Now.Ticks}");
                Directory.CreateDirectory(tempDir);

                using (var dummyBmp = new Bitmap(1, 1))
                using (var g = Graphics.FromImage(dummyBmp))
                {
                    var textSize = g.MeasureString(text, font);
                    int textWidth = (int)textSize.Width;
                    
                    // Logic: Scroll from Right edge to Left edge completely
                    // Frame count depends on speed. let's say 4 pixels per frame (MPV usually runs 30-60fps)
                    // But GIF might be slower. 4px @ 30fps is decent.
                    int speed = 4;
                    int totalFrames = (width + textWidth) / speed;
                    
                    // Cap frames to avoid huge files
                    if (totalFrames > 300) { speed *= 2; totalFrames = (width + textWidth) / speed; }

                    using (var brushBg = new SolidBrush(bgColor))
                    using (var brushText = new SolidBrush(textColor))
                    {
                        var format = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center };
                        
                        for (int i = 0; i < totalFrames; i++)
                        {
                            if (token.IsCancellationRequested)
                            {
                                try { Directory.Delete(tempDir, true); } catch { }
                                return string.Empty;
                            }
                            using (var frame = new Bitmap(width, height, PixelFormat.Format32bppArgb))
                            using (var gf = Graphics.FromImage(frame))
                            {
                                gf.Clear(Color.Transparent);
                                gf.FillRectangle(brushBg, 0, 0, width, height);
                                
                                // Draw Border frame (MPV style: Silver 2px)
                                using (var pen = new Pen(Color.Silver, 2))
                                {
                                    gf.DrawRectangle(pen, 0, 0, width, height);
                                }

                                int x = width - (i * speed);
                                RectangleF rect = new RectangleF(x, 0, textWidth + 10, height);
                                
                                gf.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                                gf.DrawString(text, font, brushText, rect, format);
                                
                                frame.Save(Path.Combine(tempDir, $"frame_{i:000}.png"), ImageFormat.Png);
                            }
                        }
                    }
                }

                // Call FFmpeg to make GIF
                string ffmpegPath = FindFfmpeg();
                if (string.IsNullOrEmpty(ffmpegPath)) return string.Empty;

                string subFolder = isHardcore ? "hc_overlays" : "overlays";
                string gifPath = Path.Combine(_config.CachePath, subFolder, $"scrolling_rp_{DateTime.Now.Ticks}.gif");
                if (!Directory.Exists(Path.GetDirectoryName(gifPath)!)) Directory.CreateDirectory(Path.GetDirectoryName(gifPath)!);
                
                // ffmpeg -framerate 30 -i frame_%03d.png -vf "split[s0][s1];[s0]palettegen[p];[s1][p]paletteuse" output.gif
                // EN: Use -loop 1 to play only once (narration)
                // FR: Utiliser -loop 1 pour ne jouer qu'une fois (narration)
                string args = $"-y -framerate 30 -i \"{Path.Combine(tempDir, "frame_%03d.png")}\" -vf \"split[s0][s1];[s0]palettegen[p];[s1][p]paletteuse\" -loop 1 \"{gifPath}\"";
                
                var startInfo = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                var proc = Process.Start(startInfo);
                if (proc != null)
                {
                    using (proc)
                    {
                        if (!proc.WaitForExit(30000)) // 30s max
                        {
                            _logger.LogWarning($"[GIF Debug] FFmpeg timed out after 30s for: {text}");
                            try { proc.Kill(); } catch { }
                        }

                        if (proc.ExitCode != 0)
                        {
                            string err = proc.StandardError.ReadToEnd();
                            _logger.LogWarning($"[GIF Debug] FFmpeg failed with code {proc.ExitCode}: {err}");
                        }
                    }
                }

                try { Directory.Delete(tempDir, true); } catch { }

                return File.Exists(gifPath) ? gifPath : string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to generate scrolling GIF: {ex.Message}");
                return string.Empty;
            }
        }

        private void DrawStringWithOutline(Graphics g, string text, Font font, Brush brush, RectangleF rect, StringFormat format, bool isDmd)
        {
            if (isDmd)
            {
                // EN: Draw Dark Gray outline for better legibility on DMD (Matches background to avoid transparency holes)
                // FR: Dessiner un contour Gris Foncé pour une meilleure lisibilité sur DMD (Correspond au fond pour éviter les trous de transparence)
                using (var outlineBrush = new SolidBrush(Color.FromArgb(255, 60, 60, 60)))
                {
                    var offsetRect = rect;
                    
                    // Simple 4-way shadow/outline
                    offsetRect.Offset(-1, 0); g.DrawString(text, font, outlineBrush, offsetRect, format);
                    offsetRect = rect; offsetRect.Offset(1, 0); g.DrawString(text, font, outlineBrush, offsetRect, format);
                    offsetRect = rect; offsetRect.Offset(0, -1); g.DrawString(text, font, outlineBrush, offsetRect, format);
                    offsetRect = rect; offsetRect.Offset(0, 1); g.DrawString(text, font, outlineBrush, offsetRect, format);
                }
            }
            
            g.DrawString(text, font, brush, rect, format);
        }

        private Color GetColorFromHex(string hex, Color defaultColor)
        {
            try
            {
                if (string.IsNullOrEmpty(hex)) return defaultColor;
                return ColorTranslator.FromHtml(hex);
            }
            catch { return defaultColor; }
        }
        private async Task<OverlayResult> GenerateFullscreenScrollingGif(string text, Font font, int width, int height, RectangleF boxRect, string? backgroundPath, Color boxColor, Color textColor, bool isHardcore, CancellationToken token = default)
        {
            Image? bgImage = null;
                if (!string.IsNullOrEmpty(backgroundPath) && File.Exists(backgroundPath))
                {
                    try { bgImage = Image.FromFile(backgroundPath); } catch { }
                }

                // EN: If no background, we can generate a small GIF for the specific box area to save MPV CPU
                // FR: Si pas de fond, on génère un petit GIF spécifique à la zone pour économiser le CPU MPV
                bool isPartial = (bgImage == null);
                int gifW = isPartial ? (int)boxRect.Width : width;
                int gifH = isPartial ? (int)boxRect.Height : height;
                int offsetX = isPartial ? (int)boxRect.X : 0;
                int offsetY = isPartial ? (int)boxRect.Y : 0;

                // Adjust boxRect for drawing relative to the GIF canvas
                RectangleF drawBoxRect = isPartial ? new RectangleF(0, 0, boxRect.Width, boxRect.Height) : boxRect;

                string safeText = Sanitize(text);
                if (safeText.Length > 25) safeText = safeText.Substring(0, 25);
                string subFolder = isHardcore ? "hc_overlays" : "overlays";
                
                int configHash = $"{text}_{gifW}_{gifH}_{boxRect.Width}_{boxColor.ToArgb()}_{textColor.ToArgb()}_{backgroundPath}".GetHashCode();
                string cachedGifPath = Path.Combine(_config.CachePath, subFolder, $"rp_narration_{safeText}_{configHash}.gif");

                var result = new OverlayResult { Path = cachedGifPath, X = offsetX, Y = offsetY, Width = gifW, Height = gifH };

                if (File.Exists(cachedGifPath))
                {
                    _logger.LogInformation($"[GIF Cache] HIT: {Path.GetFileName(cachedGifPath)} at {offsetX}:{offsetY}");
                    
                    // EN: Recalculate duration even on cache hit (we don't store metadata separately yet)
                    // FR: Recalculer la durée même en cas de cache hit
                     using (var dummyBmp = new Bitmap(1, 1))
                     using (var g = Graphics.FromImage(dummyBmp))
                     {
                         var fontToMeasure = font ?? new Font("Arial", 16); // Fallback if font null (shouldn't happen here but safe)
                         var textSize = g.MeasureString(text, fontToMeasure);
                         int speed = 18;
                         int frames = (int)((boxRect.Width + textSize.Width) / speed) + 30;
                         if (frames > 300) { speed = 25; frames = (int)((boxRect.Width + textSize.Width) / speed) + 30; }
                         result.DurationMs = (frames * 50) + 3000; // EN: Increase buffer to 3s / FR: Augmenter le buffer à 3s
                     }
                    
                    bgImage?.Dispose();
                    return result;
                }

                // EN: Lock to ensure only one FFmpeg process runs at a time for GIF generation
                // FR: Verrou pour s'assurer qu'un seul processus FFmpeg tourne à la fois pour la génération de GIF
                await _gifGenerationLock.WaitAsync(token);
                try
                {
                    // Re-check after lock
                    if (File.Exists(cachedGifPath))
                    {
                        bgImage?.Dispose();
                        return result;
                    }

                    int speed = 18; // Increased from 12 to maintain visual speed (360px/s) at 20fps
                float textWidth = 0;
                using (var dummyBmp = new Bitmap(1, 1))
                using (var g = Graphics.FromImage(dummyBmp))
                {
                     var textSize = g.MeasureString(text, font);
                     textWidth = textSize.Width;
                }

                // Initial calculation of frames (at 20fps)
                // Logic: Scroll from Right edge of BOX to Left edge of BOX
                int totalFrames = (int)((boxRect.Width + textWidth) / speed) + 60; 
                
                // EN: Set approximate duration (50ms per frame at 20fps + 3s buffer)
                // FR: Définir durée approximative (50ms par frame à 20fps + 3s buffer)
                result.DurationMs = (totalFrames * 50) + 3000; 
                    
                // Cap frames to avoid huge files if text is extremely long, but allow enough for scrolling
                // FR: Limiter les frames
                // Cap frames to avoid huge files if text is extremely long, but allow enough for scrolling
                // FR: Limiter les frames
                if (totalFrames > 300) { 
                    speed = 25; 
                    totalFrames = (int)((boxRect.Width + textWidth) / speed) + 60; 
                    result.DurationMs = (totalFrames * 50) + 3000; // Recalculate duration
                }

                _logger.LogInformation($"[GIF Debug] Starting GIF Generation (Speed: {speed}, Frames: {totalFrames}, Filter: PaletteGen)");

                // Call FFmpeg
                string ffmpegPath = FindFfmpeg();
                if (string.IsNullOrEmpty(ffmpegPath)) 
                {
                     bgImage?.Dispose();
                     return result;
                }

                string subFolder_inner = isHardcore ? "hc_overlays" : "overlays";
                // Use the deterministic cachedGifPath instead of random timestamp
                string gifPath = cachedGifPath;
                
                if (!Directory.Exists(Path.GetDirectoryName(gifPath)!)) Directory.CreateDirectory(Path.GetDirectoryName(gifPath)!);

                // EN: Set GIF path in result (MPV will use GIF with :loop=-1 parameter)
                // FR: Définir le chemin GIF dans le résultat (MPV utilisera GIF avec paramètre :loop=-1)
                result.Path = gifPath;


                // Initialize ffmpeg args for image2pipe
                // Use palettegen/paletteuse for transparency support and high quality
                // -loop 0 sets the GIF to loop infinitely
                var startInfo = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    // EN: Use -loop 1 to play only once (narration)
                    // FR: Utiliser -loop 1 pour ne jouer qu'une fois (narration)
                    Arguments = $"-y -f image2pipe -framerate 20 -i - -filter_complex \"[0:v] split [a][b];[a] palettegen=reserve_transparent=1 [p];[b][p] paletteuse\" -loop 1 \"{gifPath}\"",
                    RedirectStandardInput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                var process = Process.Start(startInfo);
                if (process == null)
                {
                    _logger.LogError("Failed to start FFmpeg process.");
                    bgImage?.Dispose();
                    return result;
                }

                // Write frames to stdin
                using (var stdin = process.StandardInput.BaseStream)
                using (Bitmap frame = new Bitmap(gifW, gifH, PixelFormat.Format32bppArgb))
                using (Graphics gf = Graphics.FromImage(frame))
                {
                    gf.SmoothingMode = SmoothingMode.AntiAlias;
                    gf.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

                    using (var brushBox = new SolidBrush(boxColor))
                    using (var brushText = new SolidBrush(textColor))
                    using (var shadowBrush = new SolidBrush(Color.FromArgb(150, 0, 0, 0)))
                    {
                        var format = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap };

                        for (int i = 0; i < totalFrames; i++)
                        {
                            if (token.IsCancellationRequested)
                            {
                                try { process.Kill(); } catch { }
                                return result;
                            }
                            gf.Clear(Color.Transparent); // Clear frame with transparency

                            // 1. Draw Static Background (if any)
                            if (bgImage != null)
                            {
                                gf.DrawImage(bgImage, 0, 0, width, height);
                            }
                            
                            // 2. Draw Box Background
                            if (boxColor.A > 0)
                            {
                                gf.FillRectangle(brushBox, drawBoxRect);
                            }

                            // 3. Draw Scrolling Text with Clipping
                            gf.SetClip(drawBoxRect);

                            // Calculate X position
                            // Start slightly inside (Right - 20) to appear faster
                            float currentX = (drawBoxRect.Right - 20) - (i * speed);

                            // Use a generous width to prevent any word wrap clipping
                            RectangleF drawRect = new RectangleF(currentX, drawBoxRect.Y, textWidth + 100, drawBoxRect.Height);

                            // Shadow
                            var shadowRect = drawRect;
                            shadowRect.Offset(2, 2);
                            gf.DrawString(text, font, shadowBrush, shadowRect, format);

                            // Text
                            gf.DrawString(text, font, brushText, drawRect, format);

                            gf.ResetClip();

                            // Write to FFmpeg stdin
                            frame.Save(stdin, ImageFormat.Png);
                        }
                    }
                }

                // Wait for FFmpeg to finish
                // Read stderr to avoid deadlocks and capture errors
                string stderr = process.StandardError.ReadToEnd();
                process.WaitForExit(30000); // 30s max
                
                if (process.ExitCode != 0)
                {
                    _logger.LogWarning($"[GIF Debug] FFMPEG Exited with Code {process.ExitCode}. Error: {stderr}");
                }

                if (bgImage != null) bgImage.Dispose();


                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to generate scrolled GIF: {ex.Message}");
                return new OverlayResult();
            }
            finally
            {
                _gifGenerationLock.Release();
            }
        }

        /// <summary>
        /// EN: Generates a specialized Preview Mode status overlay (Bottom-Left, Semi-transparent)
        /// FR: Génère un overlay de statut Mode Aperçu spécialisé (Bas-Gauche, Semi-transparent)
        /// </summary>
        public string GeneratePreviewStatusOverlay(string text, int width, int height)
        {
            try
            {
                string outputFolder = Path.Combine(_config.CachePath, "overlays");
                Directory.CreateDirectory(outputFolder);
                string fileName = $"preview_status_{DateTime.Now.Ticks}.png";
                string outputPath = Path.Combine(outputFolder, fileName);

                using (var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb))
                using (var g = Graphics.FromImage(bitmap))
                {
                    g.Clear(Color.Transparent);
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

                    // 1. Prepare Font
                    using (var font = new Font("Segoe UI", 12, FontStyle.Bold)) // Fixed small size
                    using (var brushText = new SolidBrush(Color.FromArgb(153, 255, 255, 255))) // White at 60% Opacity (255 * 0.6 = 153)
                    using (var brushBg = new SolidBrush(Color.FromArgb(100, 0, 0, 0))) // Black transparent bg
                    {
                        var textSize = g.MeasureString(text, font);
                        int padding = 5;
                        int boxW = (int)textSize.Width + (padding * 2);
                        int boxH = (int)textSize.Height + (padding * 2);

                        // Position: Bottom Left with margin
                        int margin = 20;
                        int x = margin;
                        int y = height - boxH - margin;

                        // Draw Box
                        Rectangle rect = new Rectangle(x, y, boxW, boxH);
                        g.FillRectangle(brushBg, rect);

                        // Draw Text
                        g.DrawString(text, font, brushText, x + padding, y + padding);
                    }

                    bitmap.Save(outputPath, ImageFormat.Png);
                }
                return outputPath;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[ImageConversion] GeneratePreviewStatusOverlay Error: {ex.Message}");
                return string.Empty;
            }
        }

    }
}
