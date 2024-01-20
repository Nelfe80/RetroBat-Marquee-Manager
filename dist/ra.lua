-- écran des amis et ce à quoi ils sont en train de jouer 
-- écran de connexion de l'utilisateur https://retroachievements.org/API/API_GetUserProfile.php?u=Nelfe&y=WPg4ngbUsJdtF5Pk97OQ91Mle0FnGQqx +  a lancé le jeu https://retroachievements.org/API/API_GetGame.php?i=1&z=Nelfe&y=WPg4ngbUsJdtF5Pk97OQ91Mle0FnGQqx
-- écran comme quoi il n'y a pas de succès pour le jeu (total 0 achievement)
-- écran d'un nouveau succés https://retroachievements.org/API/API_GetGameInfoAndUserProgress.php?g=11751&u=Nelfe&y=WPg4ngbUsJdtF5Pk97OQ91Mle0FnGQqx
-- écran du prochain succés à débloquer https://retroachievements.org/API/API_GetGameInfoAndUserProgress.php?g=11751&u=Nelfe&y=WPg4ngbUsJdtF5Pk97OQ91Mle0FnGQqx
-- écran progression dans le jeu
-- background possibles : étoiles jaunes, coupes gagnantes, 
-- Nous allons maintenant travailler sur le script LUA qui va réceptionner tous ces datas envoyés :
-- "user_info|Nelfe|C:\\RetroBat\\UserPic\\RA\\UserPic\\Nelfe.png"
-- "game_info|Blazing Star|C:\\RetroBat\\marquees\\RA\\Images\\016053.png|5|10.00%|50"
-- "achievement|59818|C:\\RetroBat\\marquees\\RA\\Badge\\62927.png|Blazing Guns|Upgrade the ship to the max (P1)|5|10.00%"

-- This is a simple Lua script for mpv media player

-- This function will be called when the file is loaded
mp.register_event("file-loaded", function()
    -- Display "Bonjour" on the OSD (On Screen Display)
    -- mp.osd_message("RetroAchievements Activate!", 5) -- The number '3' defines how many seconds the message will be shown
end)

mp.register_event("push-ra", function()
    -- Display "Bonjour" on the OSD (On Screen Display)
    mp.osd_message("PUSH", 5) -- The number '3' defines how many seconds the message will be shown
end)

-- mpv_ipc_listener.lua
-- This script listens for script messages and displays them on the OSD

-- function process_data(data)
    -- Display the incoming data on OSD for 60 seconds
-- mp.osd_message(data, 60)
-- end

function process_data(data)
    -- Découper la chaîne de données reçue sur les barres verticales
    local data_split = {}
    for str in string.gmatch(data, "([^|]+)") do
        table.insert(data_split, str)
    end

    -- Vérification et traitement des données selon le type
    if data_split[1] == "user_info" then
        process_user_info(data_split)
    elseif data_split[1] == "game_info" then
        process_game_info(data_split)
    elseif data_split[1] == "achievement" then
        process_achievement(data_split)
    else
        mp.osd_message("Type de données inconnu: " .. data, 5)
	end
end

-- function display_image(image_path, x, y)
	-- mp.osd_message("Display image" .. image_path, 5)
	-- OK : mp.command('vf add lavfi=[movie=\'Nelfe.png\'[img],[vid1][img]overlay=W-w-10:H-h-10]')
	-- OK : mp.commandv('vf', 'add', 'lavfi=[movie=\'Nelfe.png\'[img],[vid1][img]overlay=W-w-10:H-h-10]')
	-- OK : local imagepath = 'RA/UserPic/Nelfe.png'
	-- OK : mp.commandv('vf', 'remove', '@user')
	-- local transformed_path = image_path:gsub(".*\\(RA\\)", "%1"):gsub("\\", "/")
	-- mp.commandv('vf', 'add', 'lavfi=[movie=\'' .. transformed_path .. '\'[img],[vid1][img]overlay=W-w-10:H-h-10]')
-- end

function move_rectangle_step(x, y, w, h, opacity, end_x, end_y, end_opacity, step_x, step_y, step_opacity, current_step, total_steps, callback)
    if current_step > total_steps then
        if callback then
            callback() -- Exécuter la fonction de rappel à la fin du mouvement
        end
        return
    end

    local next_x = x + step_x
    local next_y = y + step_y
    local next_opacity = opacity + step_opacity

    gfx_draw_rectangle(next_x, next_y, w, h, next_opacity)
    mp.add_timeout(0.02, function()
        move_rectangle_step(next_x, next_y, w, h, next_opacity, end_x, end_y, end_opacity, step_x, step_y, step_opacity, current_step + 1, total_steps, callback)
    end)
end

function move_rectangle(x, y, w, h, opacity, end_x, end_y, end_opacity, steps, callback)
    local step_x = (end_x - x) / steps
    local step_y = (end_y - y) / steps
    local step_opacity = (end_opacity - opacity) / steps
    move_rectangle_step(x, y, w, h, opacity, end_x, end_y, end_opacity, step_x, step_y, step_opacity, 1, steps, callback)
end

function draw_rectangle(x, y, w, h, opacity_decimal)
    local opacity_hex = math.floor(opacity_decimal * 255) -- Convertir de décimal (0.0 - 1.0) à hexadécimal (0 - 255)
    opacity_hex = string.format("%X", 255 - opacity_hex) -- Convertir en hexadécimal et inverser pour le format ASS (0x00 est opaque, 0xFF est transparent)
	local x=x-9
	local y=y-9
    local ass_draw = string.format("{\\an7\\bord0\\shad0\\1c&H000000&\\1a&H%s&\\p1}m %d %d l %d %d %d %d %d %d{\\p0}",
                                   opacity_hex, x, y, x + w, y, x + w, y + h, x, y + h)

    local screen_width = mp.get_property_number("osd-width", 1920)
    local screen_height = mp.get_property_number("osd-height", 1080)

    mp.set_osd_ass(screen_width, screen_height, ass_draw)
end


function gfx_draw_rectangle(x, y, w, h, opacity_decimal)
    local opacity_hex = math.floor(opacity_decimal * 255)
    opacity_hex = string.format("%02X", 255 - opacity_hex) -- Inversion pour le format ASS (0xFF est transparent, 0x00 est opaque)
    -- Dessin du rectangle en utilisant la syntaxe ASS
    local ass_draw = string.format("{\\an7\\bord0\\shad0\\1c&H000000&\\1a&H%s&\\p1}m %d %d l %d %d %d %d %d %d{\\p0}",opacity_hex, x, y, x + w, y, x + w, y + h, x, y + h)
    -- Définition de la largeur et de la hauteur de l'écran pour l'OSD
    local screen_width = mp.get_property_number("osd-width", 1920)
    local screen_height = mp.get_property_number("osd-height", 1080)
    -- Affichage du rectangle sur l'OSD
    mp.set_osd_ass(screen_width, screen_height, ass_draw)
end

-- function display_image(image_path, x, y)
    -- local transformed_path = image_path:gsub(".*\\(RA\\)", "%1"):gsub("\\", "/")
    -- local filter_str = string.format("@userImage:lavfi=[movie='%s'[img],[vid1][img]overlay=x=%d:y=%d]", transformed_path, x, y)
    -- mp.commandv('vf', 'add', filter_str)
-- end

function display_image(name, image_path, x, y, w, h, callback)
    mp.commandv('vf', 'del', '@' .. name)
	local transformed_path = image_path:gsub(".*\\(RA\\)", "%1"):gsub("\\", "/")
    -- local filter_str = string.format("@userImage:lavfi=[movie='%s'[img],[vid1][img]overlay=x=%d:y=%d]", transformed_path, x, y)
	-- local filter_str = string.format("@userImage:lavfi=[movie='%s'[img],[vid1][img]overlay=x=%d:y=%d]", transformed_path, x, y)
	-- local filter_str = string.format("@userImage:lavfi=[movie='%s'[img],[vid1][img]overlay=%d:%d]", transformed_path, x, y)
	local filter_str = string.format("@%s:lavfi=[movie='%s'[img];[img]scale=%d:%d[scaled];[vid1][scaled]overlay=%d:%d]", name, transformed_path, w, h, x, y)
	mp.commandv('vf', 'add', filter_str)
    callback()
end

function move_image_step(name, image_path, x, y, end_x, end_y, step_x, step_y, current_step, total_steps, callback)
    if current_step > total_steps then
        if callback then
            callback() -- Exécuter la fonction de rappel à la fin du mouvement
        end
        return
    end
    local next_x = x + step_x
    local next_y = y + step_y
	display_image(name, image_path, next_x, next_y, 128, 128)
    mp.add_timeout(0.02, function()		
        move_image_step(image_path, next_x, next_y, end_x, end_y, step_x, step_y, current_step + 1, total_steps, callback)
    end)
end

function move_image(name, image_path, start_x, start_y, end_x, end_y, steps, callback)
    local step_x = (end_x - start_x) / steps
    local step_y = (end_y - start_y) / steps
    move_image_step(name, image_path, start_x, start_y, end_x, end_y, step_x, step_y, 1, steps, callback)
end

function display_text(name, texte, police, taille, couleur, x, y, callback)
    local ass_style = string.format("{\\fn%s\\fs%d\\1c&H%s&\\pos(%d,%d)}", police, taille, couleur, x, y)
    mp.osd_message(ass_style .. texte)
    if callback then callback() end
end


function process_user_info(data_split)
	-- Traitement des informations utilisateur
	local username = data_split[2]
	local userPicPath = data_split[3]	
	-- mp.osd_message("Utilisateur: " .. username .. "\nImage: " .. userPicPath, 5)

	-- mp.osd_message("{\\pos(300,50)}Texte à la position 300x50", 5)
	-- Créer un overlay OSD
	-- Créer un overlay OSD
	local overlay = mp.create_osd_overlay("ass-events")
	overlay.data = "{\\pos(300,50)\\fs40\\c&H00FF00&}" .. username
	overlay:update()
	
	move_rectangle(-128, 30, 128, 128, 0, 30, 30, 1, 10, function()
		mp.add_timeout(0.5, function()			
			display_image('user', 'RA/Anim/pixels.gif', 30, 30, 128, 128, function()
				mp.add_timeout(0.2, function()
					move_rectangle(30, 30, 128, 128, 1, 30, 30, 0.1, 10, function()
						mp.add_timeout(0.2, function()		
							display_image('user', userPicPath, 30, 30, 128, 128, function()
								mp.add_timeout(2, function()
									display_image('user', 'RA/Anim/pixels.gif', 30, 30, 128, 128, function()
										mp.add_timeout(1, function()
											mp.commandv('vf', 'remove', '@user')
											move_rectangle(30, 30, 128, 128, 0.1, 30, 30, 1, 10, function()													
												mp.add_timeout(0.2, function()													
													move_rectangle(30, 30, 128, 128, 1, -128, 30, 0, 10)
												end)
											end)
										end)
									end)
								end)
							end)
						end)
					end)
				end)
			end)
		end)
	end)

	
	--move_image("user", userPicPath, -150, 30, 30, 30, 5, function()
		--mp.add_timeout(5, function() -- Attendre 5 secondes avant de démarrer la deuxième animation
			--move_image("user", userPicPath, 30, 30, -150, 30, 5)
		--end)
	--end)
	
	
end

function process_game_info(data_split)
	-- Traitement des informations du jeu
	local gameTitle = data_split[2]
	local gameIconPath = data_split[3]
	local numAchievementsUnlocked = data_split[4]
	local userCompletion = data_split[5]
	local totalAchievements = data_split[6]
	-- mp.osd_message("Jeu: " .. gameTitle .. "\nIcône: " .. gameIconPath .. "\nSuccès débloqués: " .. numAchievementsUnlocked .. "/" .. totalAchievements .. "\nPourcentage de complétion: " .. userCompletion, 5)
end

function process_achievement(data_split)
	-- Traitement des informations de succès
	local achievementId = data_split[2]
	local badgePath = data_split[3]
	local title = data_split[4]
	local description = data_split[5]
	local numAwardedToUser = data_split[6]
	local userCompletion = data_split[7]
	mp.osd_message("Succès: " .. title .. "\nID: " .. achievementId .. "\nBadge: " .. badgePath .. "\nDescription: " .. description .. "\nUtilisateurs l'ayant débloqué: " .. numAwardedToUser .. "\nPourcentage de complétion: " .. userCompletion, 5)
end

-- Register the 'push-ra' message for processing
mp.register_script_message("push-ra", process_data)
