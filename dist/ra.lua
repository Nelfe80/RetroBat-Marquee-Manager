-- ra.lua

-- écran des amis et ce à quoi ils sont en train de jouer 
-- écran de connexion de l'utilisateur
-- écran comme quoi il n'y a pas de succès pour le jeu (total 0 achievement)
-- écran d'un nouveau succés
-- écran du prochain succés à débloquer
-- écran progression dans le jeu
-- background possibles : étoiles jaunes, coupes gagnantes,
-- "user_info|Nelfe|C:\\RetroBat\\UserPic\\RA\\UserPic\\Nelfe.png"
-- "game_info|Blazing Star|C:\\RetroBat\\marquees\\RA\\Images\\016053.png|5|10.00%|50"
-- "achievement|59818|C:\\RetroBat\\marquees\\RA\\Badge\\62927.png|Blazing Guns|Upgrade the ship to the max (P1)|5|10.00%"

-- function display_image(image_path, x, y)
	-- OK : mp.command('vf add lavfi=[movie=\'Nelfe.png\'[img],[vid1][img]overlay=W-w-10:H-h-10]')
	-- OK : mp.commandv('vf', 'add', 'lavfi=[movie=\'Nelfe.png\'[img],[vid1][img]overlay=W-w-10:H-h-10]')
	-- OK : local imagepath = 'RA/UserPic/Nelfe.png'
	-- OK : mp.commandv('vf', 'remove', '@user')
	-- OK : mp.commandv('vf', 'clr')
	-- OK local overlay = mp.create_osd_overlay("ass-events")
	-- OK overlay.data = "{\\pos(300,50)\\fs40\\c&H00FF00&}" .. username
	-- OK overlay:update()
-- end

-- This is a simple Lua script for mpv media player

-- Variables globales
local gfx_objects = {}
local refresh_interval = 0.02  -- Par exemple, 0.1 seconde
local achievements_data = {}
-- dimensions de l'écran
local screen_width = 0
local screen_height = 0
-- dimensions de l'image
local image_width = 0
local image_height = 0
local sorted_objects = {}
local is_sorted_objects_dirty = true
local is_update_display_running = false
local initialisation = true
local initscreen = "RA/Cache/_initscreen.png"

mp.register_event("file-loaded", function()
	-- mp.osd_message("register_event", 1)
	if initialisation then
		-- achievements_data = {}
		-- mp.osd_message("Initialisation", 5)
		init()
		initialisation = false
	end
end)

local isDMD = false
local config_file = io.open("config.ini", "r")
if config_file then
  for line in config_file:lines() do
    if line:match("ActiveDMD%s*=%s*true") then
      isDMD = true
	  -- mp.osd_message("isDMD true", 20)
      break
    end
  end
  config_file:close()
end

-- MAME OUTPUT SUPPORT
-- Download artworks and lays files here : https://dragonking.arcadecontrols.com/static.php?page=mhDisplays
-- MAME OUTPUT SUPPORT

-- Variables globales
current_game_dir = nil
layout_cache = nil  -- Contenu du layout (default.lay) chargé une fois
actual_width = 0
actual_height = 0
scale_x = 1
scale_y = 1
ref_width = nil
ref_height = nil
current_overlay_states = {}  -- Pour stocker l'état de chaque overlay (nom composite)

-- Fonction get_view_mapping améliorée pour extraire tous les paramètres, y compris les sous-éléments.
-- Retourne un mapping où chaque clé est le nom de l'élément et la valeur est un tableau contenant tous les éléments avec ce nom.
function get_view_mapping(layout_content, view_name)
    local mapping = {}
    local view_pattern = '<view%s+name%s*=%s*"' .. view_name .. '"([%s%S]-)</view>'
    local view_content = layout_content:match(view_pattern)
    if view_content then
        for element_block in view_content:gmatch("<element[%s%S]-</element>") do
            local name = element_block:match('name%s*=%s*"(.-)"')
            local ref = element_block:match('ref%s*=%s*"(.-)"')
            if name and ref then
                local element_data = { ref = ref }
                -- Extraction dynamique du(s) sous-élément(s), par exemple <bounds .../>
                local children = {}
                for tag, attrs in element_block:gmatch("<(%w+)%s+([^>/]+)%s*/>") do
                    local child_attrs = {}
                    for k, v in attrs:gmatch('(%w+)%s*=%s*"(.-)"') do
                        child_attrs[k] = tonumber(v) or v
                    end
                    children[tag] = child_attrs
                end
                element_data.children = children
                if mapping[name] then
                    table.insert(mapping[name], element_data)
                else
                    mapping[name] = { element_data }
                end
            end
        end
    end
    return mapping
end

-- Fonction pour récupérer le fichier image depuis la section de base du layout pour un élément donné
function get_base_image_file(layout_content, element_name)
    local pattern = '<element%s+name%s*=%s*"' .. element_name .. '"(.-)</element>'
    local element_block = layout_content:match(pattern)
    if element_block then
        local file_attr = element_block:match('<image%s+file%s*=%s*"([^"]+)"')
        return file_attr
    end
    return nil
end

-- Fonction pour récupérer les propriétés de l'image depuis la section de base du layout pour un élément donné
function get_base_image_properties(layout_content, element_name)
    local pattern = '<element%s+name%s*=%s*"' .. element_name .. '"(.-)</element>'
    local element_block = layout_content:match(pattern)
    if element_block then
        local file_attr = element_block:match('<image%s+file%s*=%s*"([^"]+)"')
        local state_attr = element_block:match('<image%s+file%s*=%s*"[^"]+"%s+state%s*=%s*"(%d+)"')
        return { file = file_attr, state = state_attr }
    end
    return nil
end

-- Fonction pour récupérer les dimensions du marquee depuis l'élément dont ref="marquee"
function get_marquee_bounds(layout_content)
    local marquee_block = layout_content:match('<element%s+ref%s*=%s*"marquee".-</element>')
    if marquee_block then
        local w = marquee_block:match('width%s*=%s*"(%d+)"')
        local h = marquee_block:match('height%s*=%s*"(%d+)"')
        if w and h then
            return tonumber(w), tonumber(h)
        end
    end
    return nil, nil
end

-- Fonction utilitaire pour retirer les espaces en début et fin de chaîne
local function trim(s)
    return (s:gsub("^%s*(.-)%s*$", "%1"))
end

-- Fonction process_control_message pour le traitement générique des messages de contrôle
function process_control_message(data)

    -- mp.osd_message("process_control_message: " .. data, 3)
	-- mp.osd_message("dimension : " .. screen_width .. " " .. screen_height, 20)
	refresh_interval = 0.25

	-- Détermination du mode d'affichage : DMD ou écran LCD
	local view_marquee = "Marquee_Only"
	if isDMD then
		view_marquee = "DMD_Only"
	end

    local id, state = data:match("^(%S+)%s*=%s*(%d+)")
    if id and state then
        if not current_game_dir or not layout_cache then return end
        local layout_content = layout_cache
        local view_mapping = get_view_mapping(layout_content, view_marquee)
        if not view_mapping then return end

        local lookup_id = trim(id):lower()
        local found_key = nil
        for key, _ in pairs(view_mapping) do
            if trim(key):lower() == lookup_id then
                found_key = key
                break
            end
        end
        if not found_key then return end

        -- Ici, view_mapping[found_key] est un tableau d'éléments.
        local elements = view_mapping[found_key]
        for i, element_data in ipairs(elements) do
            local composite_key = found_key .. "_" .. tostring(i)
            local ref = element_data.ref
            local children = element_data.children
            if children and children.bounds then
                local bounds = children.bounds
                local base_props = get_base_image_properties(layout_content, ref)
                if base_props and base_props.file then
                    local full_image_path = current_game_dir .. "/" .. base_props.file
                    if current_overlay_states[composite_key] == state then
                        -- Ignorer si l'état n'a pas changé pour cet overlay
                        goto continue
                    end
                    current_overlay_states[composite_key] = state
                    if state == "1" then
                        -- mp.osd_message("Turning ON " .. id .. " (" .. ref .. ") for " .. composite_key, 3)
                        set_object_properties(composite_key, { show = true })
                    elseif state == "0" then
                        -- mp.osd_message("Turning OFF " .. id .. " (" .. ref .. ") for " .. composite_key, 3)
                        set_object_properties(composite_key, { show = false })
                    end
                else
                    -- mp.osd_message("Image properties for ref " .. tostring(ref) .. " not found", 3)
                end
            else
                -- mp.osd_message("No bounds found for element " .. found_key, 3)
            end
            ::continue::
        end
    end
end


-- Variable globale pour stocker la dernière donnée reçue
last_mame_data = nil

-- Fonction mame_action appelée par MPV lors de la réception d'une commande via IPC
function mame_action(data)
    -- mp.osd_message("mame_action (raw data): " .. data, 3)

	-- Détermination du mode d'affichage : DMD ou écran LCD
	local view_marquee = "Marquee_Only"
	local bg_marquee = "marquee"
	if isDMD then
		view_marquee = "DMD_Only"
		bg_marquee = "marquee_dmd"
	end
	-- mp.osd_message("isDMD " .. tostring(isDMD) .. "view" .. view_marquee, 20)

    data = data or ""
    -- Si la donnée reçue est identique à la précédente, l'ignorer
    if data == last_mame_data then
        -- mp.osd_message("Duplicate data received, ignoring", 3)
        return
    end
    last_mame_data = data

    -- Découper la chaîne reçue en segments séparés par "|"
    local parts = {}
    for part in string.gmatch(data, "([^|]+)") do
        table.insert(parts, part)
    end

    for i, segment in ipairs(parts) do
        local lower_segment = segment:lower()
        if lower_segment:find("mame_start") then
            local game = segment:match("mame_start%s*=%s*(%S+)")
            if game then
                current_game_dir = "dof/mame/" .. game
                -- mp.osd_message("Game directory set to: " .. current_game_dir, 3)
                -- Supprimer tous les objets existants
                if gfx_objects then
                    for name, _ in pairs(gfx_objects) do
                        remove_object(name)
                    end
                    clear_osd(nil)
                end
                local layout_path = current_game_dir .. "/default.lay"
                local f = io.open(layout_path, "r")
                if f then
                    layout_cache = f:read("*a")
                    f:close()
                    local bg_image = layout_cache:match('<element%s+name%s*=%s*"' .. bg_marquee .. '".-<image%s+file%s*=%s*"([^"]+)"')
                    if bg_image then
                        local full_bg_path = current_game_dir .. "/" .. bg_image
                        -- mp.osd_message("Loading background: " .. full_bg_path, 3)
                        mp.commandv("loadfile", full_bg_path, "replace")
                    else
                        -- mp.osd_message("Background image not found in layout.", 3)
                    end
                    ref_width, ref_height = get_marquee_bounds(layout_cache)
                    if ref_width and ref_height then
                        -- mp.osd_message("Reference marquee dimensions: " .. ref_width .. "x" .. ref_height, 3)
                    else
                        -- mp.osd_message("Reference marquee dimensions not found", 3)
                    end
                    -- Création des overlays par défaut pour tous les éléments de la vue "Marquee_Only"
                    local view_mapping = get_view_mapping(layout_cache, view_marquee)
                    if view_mapping then
                        for key, elements in pairs(view_mapping) do
                            for i, element_data in ipairs(elements) do
                                local composite_key = key .. "_" .. tostring(i)
                                local ref = element_data.ref
                                local children = element_data.children
                                if children and children.bounds then
                                    local bounds = children.bounds
                                    local base_props = get_base_image_properties(layout_cache, ref)
                                    if base_props and base_props.file then
                                        local full_image_path = current_game_dir .. "/" .. base_props.file
                                        local show_initial = (base_props.state == "1")
                                        -- mp.osd_message("Loading default image for " .. key .. " (" .. ref .. ") as " .. composite_key .. " show:" .. tostring(show_initial) .. " " .. full_image_path, 3)
                                        local z_index = 30
                                        create(composite_key, "image", {
                                            image_path = full_image_path,
                                            x = bounds.x,
                                            y = bounds.y,
                                            w = bounds.width,
                                            h = bounds.height,
                                            show = show_initial,
                                            opacity_decimal = 1
                                        }, z_index)
                                        current_overlay_states[composite_key] = base_props.state
                                    else
                                        -- mp.osd_message("Image properties for ref " .. tostring(ref) .. " not found", 3)
                                    end
                                else
                                    -- mp.osd_message("No bounds found for element " .. key, 3)
                                end
                            end
                        end
                    end
                else
                    -- mp.osd_message("Layout file not found: " .. layout_path, 3)
                end
            end

        elseif segment:find("^width=") then
            local w = segment:match("width=(%d+)")
            if w then
                actual_width = tonumber(w)
                -- mp.osd_message("Actual marquee width: " .. actual_width, 3)
            end
            if actual_width and actual_height and ref_width and ref_height then
                scale_x = actual_width / ref_width
                scale_y = actual_height / ref_height
                -- mp.osd_message("Scale factors: " .. scale_x .. " x " .. scale_y, 3)
            end

        elseif segment:find("^height=") then
            local h = segment:match("height=(%d+)")
            if h then
                actual_height = tonumber(h)
                -- mp.osd_message("Actual marquee height: " .. actual_height, 3)
            end
            if actual_width and actual_height and ref_width and ref_height then
                scale_x = actual_width / ref_width
                scale_y = actual_height / ref_height
                -- mp.osd_message("Scale factors: " .. scale_x .. " x " .. scale_y, 3)
            end

        elseif lower_segment:find("mame_stop") then
            if gfx_objects then
				clear_visible_objects(function()
					clear_osd(function()
						for name, _ in pairs(gfx_objects) do
							remove_object(name)
						end
					end)
				end)

                -- mp.osd_message("All overlays removed (mame_stop)", 3)
            else
                -- mp.osd_message("No overlays to remove (gfx_objects is nil)", 3)
            end

        elseif segment:match("^%S+%s*=%s*%d+") then
            process_control_message(segment)
        end
    end
end

mp.register_script_message("mame-action", mame_action)

-- MARQUEE COMPOSE
function change_img(data)
	clear_visible_objects(function()
		local backgroundShape = "BGShape"
		local overlay_name = "CenterOverlay"
		local background_name = "BackgroundOverlay"

		mp.commandv('vf', 'remove', '@' .. backgroundShape)
		mp.commandv('vf', 'remove', '@' .. overlay_name)
		mp.commandv('vf', 'remove', '@' .. background_name)

		update_screen_dimensions(nil)

		if not(get_object_property(backgroundShape, "type")) then
			create(backgroundShape, "shape", {x = -2, y = 0, w = image_width+2, h = image_height, color_hex = "000000", opacity_decimal = 1}, 50)
		end

		fade_opacity(backgroundShape,  1, 0.01, function()

			-- mp.commandv("vf", "remove", "@" .. backgroundShape)

			-- Découper la chaîne reçue par "|" pour obtenir plusieurs segments.
			local parts = {}
			for part in string.gmatch(data, "([^|]+)") do
				table.insert(parts, part)
			end

			-- Supposons que le premier segment soit une étiquette (ex: "game-selected"),
			-- le deuxième soit le chemin du marquee et le troisième le chemin du fanart.
			local cmd = parts[1] or ""
			local marquee_path = parts[2] or ""
			local fanart_path = parts[3] or ""

			-- Traitement du marquee
			marquee_path = marquee_path:gsub("^['\"]", ""):gsub("['\"]$", "")
			marquee_path = marquee_path:gsub("\\", "/")
			--mp.osd_message("change_img (marquee): " .. marquee_path, 3)
			if marquee_path:find(":%/") then
				marquee_path = marquee_path:gsub("^%a:/[^/]+/", "../../")
			end
			if marquee_path:lower():match("%.gif$") then
				marquee_path = marquee_path .. "?frame=0"
			end

			-- Traitement du fanart, si fourni
			if fanart_path and fanart_path ~= "" then
				fanart_path = fanart_path:gsub("^['\"]", ""):gsub("['\"]$", "")
				fanart_path = fanart_path:gsub("\\", "/")
				-- mp.osd_message("change_img (fanart): " .. fanart_path, 3)
				if fanart_path:find(":%/") then
					fanart_path = fanart_path:gsub("^%a:/[^/]+/", "../../")
				end
				-- mp.msg.info("Fanart chemin relatif: " .. fanart_path)
			else
				fanart_path = nil
			end
			--mp.osd_message("change_img (fanart): " .. fanart_path, 3)
			-- Mise à jour des dimensions globales (image_width et image_height)


			-- Création de l'arrière-plan (utilise le fanart s'il est fourni, sinon le background par défaut)

			local bg_path = fanart_path

			-- Vérifier si la chaîne se termine par un point suivi de 3 ou 4 caractères (qui ne sont pas des points)
			local lower_path = fanart_path:lower()
			if not (string.match(lower_path, "%.[^%.][^%.][^%.]$") or string.match(lower_path, "%.[^%.][^%.][^%.][^%.]$")) then
				bg_path = 'RA/System/background.png'
			end



			-- Création de l'overlay marquee
			local overlay_height = image_height  -- L'overlay occupe toute la hauteur
			local overlay_width = -1  -- -1 indique que ffmpeg calcule la largeur pour préserver le ratio
			local overlay_x = 0      -- On positionne à 0 (le centrage devra être géré dans le filtre, si besoin)
			local overlay_y = 0

			if not(get_object_property(overlay_name, "type")) then
				mp.commandv('vf', 'remove', '@' .. overlay_name)
				create(overlay_name, "image", {
								image_path = marquee_path,
								x = overlay_x,
								y = overlay_y,
								w = overlay_width,  -- ffmpeg calcule la largeur automatiquement
								h = overlay_height,
								logo_align = "center",
								show = false,
								opacity_decimal = 1
							}, 30)
			end

			if not(get_object_property(background_name, "type")) then
				mp.add_timeout(0.03, function()
					mp.commandv('vf', 'remove', '@' .. background_name)
					create(background_name, "image", {
								image_path = bg_path,
								x = 0,
								y = 0,
								w = image_width,
								h = -1,
								show = false,
								opacity_decimal = 1
							}, 0)
				end)
			end

			set_object_properties(background_name, {image_path = bg_path})
			set_object_properties(overlay_name, {image_path = marquee_path})
			set_object_properties(background_name, {show = false})
			set_object_properties(overlay_name, {show = false})

			fade_opacity(backgroundShape,  1, 0.05, function()
				set_object_properties(background_name, {show = true})
				fade_opacity(backgroundShape,  1, 0.05, function()
					set_object_properties(overlay_name, {show = true})
					fade_opacity(backgroundShape,  1, 0.05, function()
						fade_opacity(backgroundShape,  0, 0.5, function()
						end)
					end)
				end)
			end)

		end)
	end)
end
mp.register_script_message("change-img", change_img)

-- Fonction pour afficher le nom de la touche pressée
function display_key_binding(name, event)
    local key_name = event["key_name"]
    if key_name then
        -- mp.osd_message("Touche pressée : " .. key_name)
    end
end
-- Écouter tous les événements de clavier et de souris
mp.register_event("key-binding", display_key_binding)

function init()
	clear_osd(function()
		update_screen_dimensions(nil)
		update_display_periodically()
	end)
end

-- #####################################
-- ############# CLEAR FUNCTIONS
-- #####################################

function clear_osd(callback)
    if next(gfx_objects) == nil then
        print("gfx_objects empty")
    else
        local names = {}
        for name, obj in pairs(gfx_objects) do
            table.insert(names, name)
        end

        for _, name in ipairs(names) do
            remove_object(name, function(wasRemoved)
                if wasRemoved then
                    -- print("Objet '" .. name .. "' a été supprimé avec succès.")
                end
            end)
        end
    end
    gfx_objects = {}
    mp.set_osd_ass(0, 0, "")
    if callback and type(callback) == "function" then
        callback()
    end
end

function clear_visible_objects(callback)
    local namesToRemove = {}
	mp.commandv('vf', 'clr')
    for name, obj in pairs(gfx_objects) do
        if obj.type == "image" then
            mp.commandv('vf', 'remove', '@' .. name)
        end
        table.insert(namesToRemove, name)
    end

    -- Supprimer les objets
    for _, name in ipairs(namesToRemove) do
        remove_object(name)
    end

    -- Vérifier si tous les objets ont été supprimés
    local checkIfAllRemoved = function()
        for _, name in ipairs(namesToRemove) do
            if gfx_objects[name] then
                return false
            end
        end
        return true
    end

    -- Exécuter le callback une fois que tous les objets sont supprimés
    local checkInterval = 0.1  -- Intervalle de vérification en secondes
    local function waitForRemoval()
        if checkIfAllRemoved() then
            if callback and type(callback) == "function" then
                callback()
            end
        else
            mp.add_timeout(checkInterval, waitForRemoval)
        end
    end

    waitForRemoval()
end


-- #####################################
-- ############# LISTEN DATAS
-- #####################################

local chrono_mode = false -- Etat mode chrono global
function process_data(data)
    local data_split = {}
    for str in string.gmatch(data, "([^|]+)") do
        table.insert(data_split, str)
    end
    -- Vérification et traitement des données selon le type
    if data_split[1] == "user_info" then
		refresh_interval = 0.02
		achievements_data = {}
		chrono_mode = false
		clear_osd(function()
			process_user_info(data_split)
		end)
    elseif data_split[1] == "game_info" then
        process_game_info(data_split)
	elseif data_split[1] == "game_stop" then
		process_game_stop(data_split)
    elseif data_split[1] == "achievement" then
		process_achievement(data_split)
	elseif data_split[1] == "achievement_info" then
        process_achievement_info(data_split)
	elseif data_split[1] == "leaderboardtimes" then
        process_leaderboardtimes(data_split)
	elseif data_split[1] == "leaderboard_event_started" then
        process_leaderboard_started(data_split)
	elseif data_split[1] == "leaderboard_event_canceled" then
        process_leaderboard_canceled(data_split)
	elseif data_split[1] == "leaderboard_event_submitting" then
        process_leaderboard_submitting(data_split)
	elseif data_split[1] == "leaderboard_event_submitted" then
        --process_leaderboard_submitted(data_split)
    else
        mp.osd_message("Type de données inconnu: " .. data, 2)
	end
end

-- #####################################
-- ############# REFRESH SCREEN
-- #####################################

local pending_capture = false
local pending_push = false
-- Fonction de mise à jour périodique
function update_display_periodically()
	-- Flag global pour indiquer qu'une capture est en attente
	if pending_capture or pending_push then
		-- mp.osd_message("pending_capture", 30)
		-- cache_screen("_marqueerefresh", false, false)

		local path = "dmd/cache/_cache_dmd.png"
		-- Attendre un court délai pour le rafraîchissement
		mp.add_timeout(0.2, function()
			mp.commandv("screenshot-to-file", path)
		end)

		if pending_capture then
			pending_capture = false  -- Réinitialisation du flag pending_capture
		end
		if pending_push then
			pending_push = false  -- Réinitialisation du flag pending_push
		end
	end
	-- mp.osd_message("update_display_periodically - pending_capture :" .. tostring(pending_capture), 10)
    gfx_refresh()
    if not is_update_display_running then
        is_update_display_running = true
        mp.add_timeout(refresh_interval, function()
            is_update_display_running = false
			update_display_periodically()
        end)
    end
end

-- MARQUEE PUSH TO DMD
function pushtodmd_img(data)
	-- mp.osd_message("pushtodmd_img : " .. data, 30)
	local parts = {}
	for part in string.gmatch(data, "([^|]+)") do
		table.insert(parts, part)
	end
	local marquee_path = parts[2] or ""
	mp.commandv("loadfile", marquee_path)
	pending_push = true
end
mp.register_script_message("pushtodmd-img", pushtodmd_img)


function update_screen_dimensions(callback)
    local function check_dimensions()
        screen_width = mp.get_property_number("osd-width")
        screen_height = mp.get_property_number("osd-height")
		image_width = mp.get_property_number("dwidth")
        image_height = mp.get_property_number("dheight")

        -- Si les dimensions sont mises à jour, exécutez la callback
        if screen_width ~= 0 and screen_height ~= 0 then
            if callback then
				-- mp.osd_message("screen_width" .. screen_width .. " screen_height" .. screen_height .. "image_width" .. image_width .. " image_height" .. image_height, 10)
                callback()
            end
        else
            -- Sinon, planifiez une nouvelle vérification après un court délai
            mp.add_timeout(0.01, check_dimensions)
        end
    end
    -- Démarrez la première vérification
    check_dimensions()
end

local last_refresh_time = 0
local framerate_threshold = 10  -- Seuil de framerate


function gfx_refresh()
    local current_time = mp.get_time()
    local delta_time = current_time - last_refresh_time
    last_refresh_time = current_time

    local framerate = 0
    if delta_time > 0 then
        framerate = 1 / delta_time
    end

    if framerate < framerate_threshold then
        -- mp.osd_message(string.format("Low Framerate : %.2f FPS", framerate), 1)
    end

    for obj_name, obj in pairs(gfx_objects) do
        if not obj.overlay then
            obj.overlay = mp.create_osd_overlay("ass-events")
        end

        local ass_data = ""
        if obj.type == "shape" and obj.updated == true then
            ass_data = generate_ass_shape(obj.properties)
        elseif obj.type == "text" and obj.updated == true then
            ass_data = generate_ass_text(obj.properties)
        elseif obj.type == "image" and obj.updated == true then
            gfx_draw_image(obj_name, obj.properties, function()
				pending_capture = true
                obj.updated = false
            end)
            -- Pas besoin de mise à jour d'overlay pour les images
            goto continue
        end

        -- Mise à jour des données de l'overlay
        if obj.updated then
            obj.overlay.res_x = image_width
            obj.overlay.res_y = image_height
            obj.overlay.data = ass_data
            obj.overlay.z = obj.z
            -- mp.osd_message("obj_name" .. obj_name .. "z" .. obj.overlay.z, 10)
            obj.overlay:update()
			pending_capture = true
            obj.updated = false
        end

        ::continue::
    end
end


local cache_screen_referer
function cache_screen(name, hide, referer, callback)
    local temp_hidden_objects = {}
	local path = "RA/Cache/" .. name .. ".png"
    -- Désactiver temporairement les objets de type "shape" et "text"
    for obj_name, obj in pairs(gfx_objects) do
        if obj.type == "shape" or obj.type == "text" then
            if obj.properties.show then
                temp_hidden_objects[obj_name] = true
                set_object_properties(obj_name, { show = false })
            end
        end
    end
    -- Attendre un court délai pour le rafraîchissement
    mp.add_timeout(0.2, function()
        -- Prendre un screenshot
        mp.commandv("screenshot-to-file", path)
        -- Réactiver les objets "shape" et "text" désactivés
        for obj_name, _ in pairs(temp_hidden_objects) do
            set_object_properties(obj_name, { show = true })
        end
		-- Mémorisation de cette image cache pour restoration ultèrieure
		if referer then
            cache_screen_referer = path
        end
		mp.add_timeout(0.2, function()
			if hide then
				for obj_name, obj in pairs(gfx_objects) do
					if obj.type == "image" then
						set_object_properties(obj_name, { show = false })
					end
				end
			end
		end)
        -- Affichage du screenshot en fond
		mp.add_timeout(0.2, function()
			mp.commandv("loadfile", path)
			-- Si hide est true, désactiver tous les objets de type "image"
		end)
		if callback then callback() end
    end)
end

function restore_cache_screen(cacheimg)
    if cacheimg then
        -- Afficher l'image en cache spécifiée
		mp.commandv("loadfile", cacheimg)
    elseif cache_screen_referer then
        -- Afficher l'image référencée par cache_screen_referer
        mp.commandv("loadfile", cache_screen_referer)
    else
        print("Aucune image en cache spécifiée ou aucun cache_screen_referer défini.")
    end
end

-- #####################################
-- ############# OBJECTS FUNCTIONS
-- #####################################

function create(name, type, properties, z)
    if gfx_objects[name] then
        -- Mettre à jour les propriétés de l'objet existant
        gfx_objects[name].type = type
        for key, value in pairs(properties) do
            gfx_objects[name].properties[key] = value
        end
        gfx_objects[name].overlay.z = z or gfx_objects[name].z
        gfx_objects[name].updated = true
    else
        -- Créer un nouvel objet si aucun objet avec ce nom n'existe
        gfx_objects[name] = {
            type = type,
            properties = properties,
            z = z or 0,
            updated = true
        }
    end
end

function set_object_properties(name, properties)
    local obj = gfx_objects[name]
    if not obj then
        print("Avertissement: L'objet nommé '" .. name .. "' n'existe pas.")
        return
    end
    -- Parcourir chaque propriété fournie et la mettre à jour
    for key, value in pairs(properties) do
        -- Gérer les cas spéciaux pour 'type' et 'z'
        if key == "type" then
            obj.type = value
        elseif key == "z" then
			obj.z = value
        elseif obj.properties and obj.properties[key] ~= nil then
            -- Mise à jour des autres propriétés standard
            obj.properties[key] = value
			obj.updated = true
        else
            print("Avertissement: La propriété '" .. key .. "' n'existe pas pour l'objet '" .. name .. "'.")
        end
    end
    -- Mettre à jour l'affichage pour refléter les changements
    -- gfx_refresh()
end

function get_object_property(name, property_key)
    local obj = gfx_objects[name]
    if not obj then
        print("Avertissement: L'objet nommé '" .. name .. "' n'existe pas.")
        return nil
    end
    -- Gérer les cas spéciaux pour 'type' et 'z'
    if property_key == "type" then
        return obj.type
    elseif property_key == "z" then
        return obj.z
    end
    -- Vérifier si la propriété existe dans les propriétés standard
    if obj.properties and obj.properties[property_key] ~= nil then
        return obj.properties[property_key]
    else
        print("Avertissement: La propriété '" .. property_key .. "' n'existe pas pour l'objet '" .. name .. "'.")
        return nil
    end
end


function remove_object(name, on_complete)
    local obj = gfx_objects[name]
    local wasRemoved = false

    if obj then
        if obj.type == "image" then
            mp.commandv('vf', 'remove', '@' .. name)
        end

        -- Supprimer l'overlay si existant
        if obj.overlay then
            obj.overlay:remove()
            obj.overlay = nil
        end

        -- Supprimer l'objet de la table
        gfx_objects[name] = nil
        -- gfx_refresh()
        wasRemoved = true
    else
        print("Avertissement: Impossible de supprimer, l'objet nommé '" .. name .. "' n'existe pas.")
    end

    if on_complete and type(on_complete) == "function" then
        on_complete(wasRemoved)
    end
end

function move(name, target_x, target_y, target_opacity, duration, on_complete)
    local obj = gfx_objects[name]
    if not obj then
        print("Avertissement: L'objet nommé '" .. name .. "' n'existe pas. Mouvement annulé.")
        return
    end

    local start_time = mp.get_time()
    local start_x = obj.properties.x
    local start_y = obj.properties.y
    local start_opacity = obj.properties.opacity_decimal
    local delta_x = target_x - start_x
    local delta_y = target_y - start_y
    local delta_opacity = target_opacity - start_opacity

    -- Fonction pour mettre à jour la position et l'opacité de l'objet au fil du temps
    local function update_position_and_opacity()
        local current_time = mp.get_time()
        local progress = (current_time - start_time) / duration
        if progress >= 1 then
            -- Mouvement et changement d'opacité terminés
            obj.properties.x = target_x
            obj.properties.y = target_y
            obj.properties.opacity_decimal = target_opacity
			obj.updated = true
            if on_complete then
                on_complete()  -- Appeler le callback une fois terminé
            end
        else
            -- Mettre à jour la position et l'opacité de l'objet
            obj.properties.x = start_x + delta_x * progress
            obj.properties.y = start_y + delta_y * progress
            obj.properties.opacity_decimal = start_opacity + delta_opacity * progress
			obj.updated = true
            -- Planifier la prochaine mise à jour
			if progress < 1 then
				mp.add_timeout(0.01, update_position_and_opacity)
			end

        end
    end

    -- Démarrer la mise à jour de la position et de l'opacité
    update_position_and_opacity()
end

function fade_opacity(name, target_opacity, duration, on_complete)
    local obj = gfx_objects[name]
    if not obj then
        print("Avertissement: L'objet nommé '" .. name .. "' n'existe pas. Fondu annulé.")
        return
    end

    local start_time = mp.get_time()
    local start_opacity = obj.properties.opacity_decimal
    local delta_opacity = target_opacity - start_opacity

    -- Fonction pour mettre à jour progressivement l'opacité de l'objet
    local function update_opacity()
        local current_time = mp.get_time()
        local progress = (current_time - start_time) / duration
        if progress >= 1 then
            -- Changement d'opacité terminé
            obj.properties.opacity_decimal = target_opacity
            obj.updated = true
            if on_complete then
                on_complete()  -- Appeler le callback une fois terminé
            end
        else
            -- Mettre à jour l'opacité de l'objet
            obj.properties.opacity_decimal = start_opacity + delta_opacity * progress
            obj.updated = true
            -- Planifier la prochaine mise à jour
            if progress < 1 then
                mp.add_timeout(0.05, update_opacity)
            end
        end
    end

    -- Démarrer la mise à jour de l'opacité
    update_opacity()
end

function animate_properties(name, targets, duration, on_complete)
    local obj = gfx_objects[name]
    if not obj then
        print("Avertissement: L'objet nommé '" .. name .. "' n'existe pas. Animation annulée.")
        return
    end

    local start_values = {}
    local delta_values = {}

    -- Initialisation des valeurs de départ et des deltas
    for property, target_value in pairs(targets) do
        if obj.properties[property] == nil then
            print("Avertissement: La propriété '" .. property .. "' n'existe pas pour l'objet '" .. name .. "'.")
            return
        end
        start_values[property] = obj.properties[property]
        delta_values[property] = target_value - start_values[property]
    end

    local start_time = mp.get_time()

    -- Fonction pour mettre à jour progressivement les propriétés
    local function update_properties()
        local current_time = mp.get_time()
        local progress = (current_time - start_time) / duration
        if progress >= 1 then
            -- Appliquer les valeurs finales
            for property, _ in pairs(targets) do
                obj.properties[property] = targets[property]
            end
            obj.updated = true
            if on_complete then
                on_complete()  -- Appeler le callback une fois terminé
            end
        else
            -- Mettre à jour les propriétés de l'objet
            for property, delta_value in pairs(delta_values) do
                obj.properties[property] = start_values[property] + delta_value * progress
            end
            obj.updated = true
            -- Planifier la prochaine mise à jour
            if progress < 1 then
                mp.add_timeout(0.01, update_properties)
            end
        end
    end

    -- Démarrer la mise à jour des propriétés
    update_properties()
end

-- #####################################
-- ############# ASS & GFX FUNCTIONS
-- #####################################

function generate_ass_shape(properties)
	local decx = -10  -- Décalage en X
	local decy = -10  -- Décalage en Y

	-- local decx = 0  -- Décalage en X
	-- local decy = 0  -- Décalage en Y

    local opacity_hex = math.floor(properties.opacity_decimal * 255)
    opacity_hex = string.format("%X", 255 - opacity_hex)

    local draw_command = properties.show and "m" or "n" -- Utilise "m" si show est vrai, sinon utilise "n"

    -- Appliquer le décalage global
    local x = properties.x + decx
    local y = properties.y + decy

    return string.format("{\\p1}{\\an7\\bord0\\shad0\\1c&H%s&\\1a&H%s&}%s %d %d l %d %d %d %d %d %d %d %d{\\p0}",
                         properties.color_hex, opacity_hex, draw_command,
                         x, y,
                         x + properties.w, y,
                         x + properties.w, y + properties.h,
                         x, y + properties.h,
                         x, y)
end

function generate_ass_text(properties)
    -- Vérifier si l'attribut text est présent
    if not properties.text or (properties.show == false) then
        return ""
    end

    -- Valeurs par défaut
    local default_size = 20
    local default_color = "FFFFFF"  -- Blanc
    local default_align = 7
    local default_border_size = 2
    local default_opacity = 1
    local default_border_color = "000000"  -- Noir
    local default_shadow_distance = 0
    local default_font = "Arial"

    -- Construire la chaîne de style
    local style = ""

    -- Alignement
    style = style .. string.format("\\an%d", properties.align or default_align)
    -- Taille de la bordure
    style = style .. string.format("\\bord%d", properties.border_size or default_border_size)
    -- Couleur de la bordure
    style = style .. string.format("\\3c&H%s&", properties.border_color or default_border_color)
    -- Taille de la police
    style = style .. string.format("\\fs%d", properties.size or default_size)
    -- Couleur de la police
    style = style .. string.format("\\1c&H%s&", properties.color or default_color)
    -- Opacité
    local opacity_hex = math.floor((properties.opacity_decimal or default_opacity) * 255)
    opacity_hex = string.format("%X", 255 - opacity_hex)
    style = style .. string.format("\\alpha&H%s&", opacity_hex)
    -- Police
    style = style .. string.format("\\fn%s", properties.font or default_font)
    -- Distance de l'ombre
	if properties.shad then
		style = style .. string.format("\\shad%d", properties.shadow_distance or default_shadow_distance)
	 end
    -- Flou des bords (si spécifié)
    if properties.blur_edges then
        style = style .. string.format("\\be%d", properties.blur_edges)
    end
    -- Échelle de police (si spécifiée)
    if properties.font_scale_x then
        style = style .. string.format("\\fscx%d", properties.font_scale_x)
    end
    if properties.font_scale_y then
        style = style .. string.format("\\fscy%d", properties.font_scale_y)
    end
    -- Espacement des lettres (si spécifié)
    if properties.letter_spacing then
        style = style .. string.format("\\fsp%.2f", properties.letter_spacing)
    end
    -- Rotation (si spécifiée)
    if properties.rotation_x then
        style = style .. string.format("\\frx%.2f", properties.rotation_x)
    end
    if properties.rotation_y then
        style = style .. string.format("\\fry%.2f", properties.rotation_y)
    end
    if properties.rotation_z then
        style = style .. string.format("\\frz%.2f", properties.rotation_z)
    end
    -- Position X et Y
	if properties.x or properties.y then
		local posX = properties.x or 0
		local posY = properties.y or 0
		style = style .. string.format("\\pos(%d,%d)", posX, posY)
	end
    -- Construire la chaîne de texte finale
    local text_string = string.format("{\\r%s}%s", style, properties.text)
    return text_string
end

function gfx_draw_image(name, properties, callback)
    if properties.show then
        local transformed_path = properties.image_path:gsub(".*\\(RA\\)", "%1"):gsub("\\", "/")
        -- Si properties.w ou properties.h ne sont pas définis (ou sont -1),
        -- on laisse ffmpeg calculer la dimension manquante en préservant le ratio.
        local scale_w = properties.w or -1
        local scale_h = properties.h or -1

        local overlay_x
        if properties.logo_align then
            if properties.logo_align == "left" then
                overlay_x = "W/10"
            elseif properties.logo_align == "right" then
                overlay_x = "W - w - W/10"
            elseif properties.logo_align == "center" then
                overlay_x = "(W-w)/2"
            else
                overlay_x = properties.x or 0
            end
        else
            overlay_x = properties.x or 0
        end
        local overlay_y = properties.y or 0

        local filter_str = string.format(
            "@%s:lavfi=[movie='%s'[img];[img]scale=%d:%d[scaled];[vid1][scaled]overlay=%s:%d]",
            name, transformed_path, scale_w, scale_h, overlay_x, overlay_y
        )
        mp.commandv('vf', 'add', filter_str)
    else
        mp.commandv('vf', 'remove', '@' .. name)
    end

    if callback then
        callback()
    end
end

local chrono_active = false -- Chrono en marche
local chrono_start_time
local chrono_paused = false
local paused_time = 0  -- Temps écoulé à la mise en pause
local record_time_global

function update_chrono_display()
    if not chrono_active then
        return
    end

    local elapsed
    if chrono_paused then
        elapsed = paused_time
    else
        elapsed = mp.get_time() - chrono_start_time
    end

    local minutes = math.floor(elapsed / 60)
    local seconds = math.floor(elapsed % 60)
    local centiseconds = math.floor((elapsed - math.floor(elapsed)) * 100)
    local display_time = string.format("%d:%02d.%02d", minutes, seconds, centiseconds)

    set_object_properties("Chrono", {text = display_time})

	local progression
    if record_time_global then
        progression = math.min(elapsed / record_time_global, 1)
    else
        progression = 0  -- Aucun record, donc progression est 0
    end
    local color_hex = calculate_progression_color(progression)
    set_object_properties("ProgressionBar", {
        w = (screen_width+10) * (progression*2),
        color_hex = color_hex
    })

    if not chrono_paused then
        mp.add_timeout(0.01, update_chrono_display)
    end
end

function toggle_chrono_pause()
    if chrono_active then
        chrono_paused = not chrono_paused
        if chrono_paused then
            paused_time = mp.get_time() - chrono_start_time
        else
            chrono_start_time = mp.get_time() - paused_time
            update_chrono_display()
        end
    end
end

-- Captation de la touche start pour gérer les pause en jeu (désolé, mais des fois ça marche pas bien...)
mp.add_key_binding("GAMEPAD_START", "toggle_chrono", toggle_chrono_pause)

-- Fonction pour calculer la couleur de progression
function calculate_progression_color(progression)
    local b, g, r

    if progression < 0.5 then
        -- Interpoler entre vert (0x00FF00) et orange (0x00A5FF)
        b = 0
		g = 0xFF
        r = 0xA5 * progression / 0.5
    elseif progression < 0.8 then
        -- Interpoler entre orange (0x00A5FF) et rouge (0xFF0000)
        local local_progress = (progression - 0.5) / 0.3
        b = 0
		g = 0xFF - 0xFF * local_progress
        r = 0xA5 + (0xFF - 0xA5) * local_progress
    else
        -- Reste sur rouge
        b, g, r = 0, 0, 0xFF
    end

    return string.format("%02X%02X%02X", b, g, r)
end

-- Fonction pour convertir le temps en secondes
function convert_time_to_seconds(time_str)
    local mins, secs, cents = time_str:match("(%d+):(%d+).(%d+)")
    return tonumber(mins) * 60 + tonumber(secs) + tonumber(cents) / 100
end

-- #####################################
-- ############# PROCESS FUNCTIONS
-- #####################################&

function process_leaderboard_started(data_split)
	clear_osd(function()
		restore_cache_screen(initscreen)
	end)

	show_score()
    chrono_mode = true
	set_object_properties("BottomBar", {show = false})
	create("BackgroundBar", "shape", {
        x = 0, y = 0,
        w = screen_width*2, h = screen_height*2,
        color_hex = "000000",
        opacity_decimal = 0.6
    }, 1)


    local text_properties = {
        align = 6,
        text = "0:00.00",
        color = "FFFFFF",
        size = 400,
        font = "Bebas Neue",
        border_size = 5
    }

    if gfx_objects["Chrono"] then
        set_object_properties("Chrono", text_properties)
    else
        create("Chrono", "text", text_properties, 100)
    end

    -- Créer un shape pour la progression
    create("ProgressionBar", "shape", {
        x = 0, y = 0,
        w = 0, h = screen_height*2,
        color_hex = "00FF00",
        opacity_decimal = 0.7
    }, 2)

	local record_text_properties = {
        align = 3,
        color = "FFFFFF",
        size = 100,
        font = "Bebas Neue",
        border_size = 3
    }

    -- Démarrer le chronomètre
	if data_split[4] ~= "No Record" then
        record_time_global = convert_time_to_seconds(data_split[4])
		record_text_properties.text = "Record: " .. data_split[4]
    else
        record_time_global = nil -- Aucun record disponible
		record_text_properties.text = "Record: No Record"
    end

	create("RecordTime", "text", record_text_properties, 200)

    chrono_active = true
    chrono_paused = false
    chrono_start_time = mp.get_time() - 0.43

    update_chrono_display()
end

function process_leaderboard_canceled(data_split)
    -- Arrêter le chronomètre
	clear_osd(function()
		restore_cache_screen(initscreen)
	end)
    chrono_active = false
end

function process_leaderboard_submitting(data_split)
	show_score()
	animate_properties("BottomBar", {y = screen_height-13, opacity_decimal = 0.7}, 3, nil)
    -- Arrêter le chronomètre
    local final_time_str = data_split[3]
    set_object_properties("Chrono", {text = final_time_str})
    chrono_active = false

	local submitted_time = convert_time_to_seconds(data_split[3])
    local time_diff = record_time_global and (submitted_time - record_time_global) or nil

    if time_diff then
        local diff_minutes = math.abs(math.floor(time_diff / 60))
        local diff_seconds = math.abs(math.floor(time_diff % 60))
        local diff_display = string.format("%d:%02d", diff_minutes, diff_seconds)

        if time_diff < 0 then
            -- Nouveau record
            set_object_properties("RecordTime", {
                text = "New record! -" .. diff_display,
                color = "00FF00" -- Vert
            })
        else
            -- En retard par rapport au record
            set_object_properties("RecordTime", {
                text = "No record! +" .. diff_display,
                color = "0000FF" -- Rouge
            })
        end
    else
        -- Aucun record précédent n'était disponible ou le temps soumis n'était pas un record
        set_object_properties("RecordTime", {
            text = "Record",
            color = "FFFFFF" -- Blanc
        })
    end
end

function process_leaderboardtimes(data_split)
	-- leaderboardtimes|2|0:44.66
end

local hardcoreMode = "False"
function process_user_info(data_split)
    update_screen_dimensions(nil)
    -- Traitement des informations utilisateur
    local username = data_split[2]
    local userPicPath = data_split[3]
    local userLanguage = data_split[4]
    hardcoreMode = data_split[5]
    local textColor = hardcoreMode == "True" and "FFFFFF" or "808080"

    -- Définition des paramètres selon le mode
    local rect_x, rect_y, rect_w, rect_h
    local text_x, text_y, text_size
    local userImage_x, userImage_y, userImage_w, userImage_h

    if isDMD then
        -- Valeurs adaptées pour un DMD (environ la moitié de celles en mode LCD)
        rect_x = -image_height
        rect_y = 5
        rect_w = image_height
        rect_h = image_height

        text_x = 0
        text_y = 45
        text_size = 20

        userImage_x = 5
        userImage_y = 0
        userImage_w = image_height
        userImage_h = image_height
    else
        -- Valeurs par défaut pour écran LCD
        rect_x = -128
        rect_y = 10
        rect_w = 128
        rect_h = 128

        text_x = 0
        text_y = 90
        text_size = 40

        userImage_x = 10
        userImage_y = 10
        userImage_w = 128
        userImage_h = 128
    end

    -- Création des overlays
    create("BlackRectangle", "shape", {
        x = rect_x, y = rect_y,
        w = rect_w, h = rect_h,
        color_hex = "000000",
        show = true,
        opacity_decimal = 0
    }, 1)

    create("UserText", "text", {
        text = username .. " connected",
        color = textColor,
        font = "VT323",
        x = text_x,
        y = text_y,
        border = 3,
        size = text_size,
        show = true,
        opacity_decimal = 0
    }, 2)

    create("UserImage", "image", {
        image_path = userPicPath,
        x = userImage_x,
        y = userImage_y,
        w = userImage_w,
        h = userImage_h,
        show = false,
        opacity_decimal = 1
    }, 3)

    -- Ajuster le temps de déplacement en fonction du mode
    local moveTime = isDMD and 0.3 or 0.5

    -- Animation de l'apparition
    move("BlackRectangle", isDMD and 5 or 10, isDMD and 5 or 10, 0.2, moveTime, function ()
        move("UserText", (isDMD and 45 or 90) + 30, isDMD and 45 or 90, 1, moveTime, nil)
        set_object_properties('UserImage', {show = true})
        mp.add_timeout(3, function()
            set_object_properties('UserImage', {show = false})
            move("UserText", isDMD and -30 or -50, isDMD and 45 or 90, 0, 0, nil)
            move("BlackRectangle", isDMD and -image_height or -128, rect_y, 0, moveTime, function ()
                remove_object("BlackRectangle", function ()
                    remove_object("UserImage", nil)
                    remove_object("UserText", nil)
                end)
            end)
        end)
    end)
end

function process_game_info(data_split)
	update_screen_dimensions(nil)

    -- Traitement des informations du jeu
    local gameTitle = data_split[2]
    local gameIconPath = data_split[3]
    local numAchievementsUnlocked = data_split[4]
    local userCompletion = data_split[5]
    local totalAchievements = data_split[6]
	--create("Pixels", "image", {image_path = 'RA/Anim/pixels.gif', x = 30, y = 30, w = 128, h = 128, show = true, opacity_decimal = 1}, 5)

    mp.add_timeout(3, function()
		cache_screen("_initscreen", false, false)
		show_achievements(function()
			print("Animation des achievements terminée")
			mp.add_timeout(2, function()
				cache_screen("_cacheAchv", true, true, function()
					print("Image cache des achievements")
					show_score()
				end)
			end)
		end)
	end)

end

function process_marquee_compose(data_split)
	update_screen_dimensions(nil)
	local name = "logo"
	mp.commandv('vf', 'remove', '@' .. name)
    -- Traitement des informations du marquee
	local fanart_file_path = data_split[4]:gsub("\\", "/")
    local fanart_top_y = data_split[5]
    local logo_file_path = data_split[2]:gsub("\\", "/")
    local logo_align = data_split[3]
	local logo_new_width = image_width / 2

	local x_position
    if logo_align == "left" then
        x_position = image_width / 10  -- 1/10th from left
    elseif logo_align == "center" then
        x_position = (image_width - logo_new_width) / 2  -- Center
    elseif logo_align == "right" then
        x_position = image_width - logo_new_width - (image_width / 10)  -- 1/10th from right
    end

	mp.commandv("loadfile", fanart_file_path)

	-- Échapper les apostrophes et les deux-points
	local escapedImagePath = logo_file_path:gsub("'", "\\'"):gsub(":", "\\:")
	local filter_str = string.format("@%s:lavfi=[movie='%s'[img];[img]scale=%d:%d[scaled];[vid1][scaled]overlay=%d:%d]", name, escapedImagePath, logo_new_width, -1, x_position, 10)
	mp.commandv('vf', 'add', filter_str)
	mp.add_timeout(0.5, function()
		mp.commandv("screenshot-to-file", "_cacheMarquee.png")
		mp.add_timeout(0.2, function()
			mp.add_timeout(0.2, function()
				mp.commandv("loadfile", "_cacheMarquee.png")
				mp.add_timeout(0.2, function()
					mp.commandv('vf', 'remove', '@' .. name)
				end)
			end)
		end)
	end)
end

function process_game_stop(data_split)
	clear_osd(function()
		restore_cache_screen(initscreen)
	end)
end


function process_achievement_info(data_split)
	-- print("Processing achievement info")
    -- Extraction des informations de l'achievement
    local achievementID = data_split[2]
    local achievementInfo = {
        NumAwarded = data_split[3],
        NumAwardedHardcore = data_split[4],
        Title = data_split[5],
        Description = data_split[6],
        Points = data_split[7],
        TrueRatio = data_split[8],
        BadgeURL = data_split[9],
        DisplayOrder = data_split[10],
        Type = data_split[11],
        Unlock = data_split[12]
    }
    -- Stocker les informations dans la variable globale
	-- print(achievementInfo)
    achievements_data[achievementID] = achievementInfo
end



function process_achievement(data_split)
	update_screen_dimensions(nil)
    -- Traitement des informations de succès
	-- "achievement|2|C:\\RetroBat\\marquees\\RA\\Badge\\250352.png|Amateur Collector|Collect 20 rings|2|8.33%"
    local achievementId = data_split[2]
    local badgePath = data_split[3]
    local title = data_split[4]
    local description = data_split[5]
    local numAwardedToUser = data_split[6]
    local userCompletion = data_split[7]

    -- Ajustement de la taille du badge
    local badgeSize = 64  -- Valeur par défaut pour écran LCD
    local offsetY = 41     -- Décalage vertical par défaut
    if isDMD then
        badgeSize = 28    -- Pour un DMD, chaque badge prend 32 de haut
        offsetY = 0       -- Centrer verticalement pour un écran étroit
    end

    -- Récupérer des informations supplémentaires depuis achievements_data
    local achievementInfo = achievements_data[achievementId]
    local points = achievementInfo and tonumber(achievementInfo.Points) or 0
    local numAwarded = achievementInfo and achievementInfo.NumAwarded or "Inconnu"
    local numAwardedHardcore = achievementInfo and achievementInfo.NumAwardedHardcore or "Inconnu"
    local trueRatio = achievementInfo and achievementInfo.TrueRatio or "Inconnu"

    if achievements_data[achievementId] then
        achievements_data[achievementId].Unlock = "True"  -- Marquer comme débloqué
        achievements_data[achievementId].NumAwarded = tostring(tonumber(achievements_data[achievementId].NumAwarded or "0") + 1)
    end

    -- Calculer le score total
    local totalPoints = 0
    for id, ach in pairs(achievements_data) do
		local achievementName = "AchievementImage" .. id
		set_object_properties(achievementName, {y = image_height + 74})
        if ach.Unlock == "True" then
            totalPoints = totalPoints + (tonumber(ach.Points) or 0)
        end
    end

    -- Construction du message (non utilisé ici directement)
    local message = string.format(
        "Succès débloqué: %s\nID: %s\nBadge: %s\nDescription: %s\nPoints: %s\nDébloqué par: %s utilisateurs\nDébloqué en mode hardcore par: %s utilisateurs\nRatio: %s\nPourcentage de complétion: %s\nTotal des points: %d",
        title, achievementId, badgePath, description, points, numAwarded, numAwardedHardcore, trueRatio, userCompletion, totalPoints
    )

    if chrono_mode == false then
		-- Création des éléments graphiques pour l'affichage de l'achievement
		local backgroundShape = "AchievementBackgroundShape"
		local backgroundName = "AchievementBackground"
		local cupName = "AchievementCup"
		local badgeName = "AchievementBadge"
		local textAchievement = "AchievementTxt"

		create(backgroundShape, "shape", {
            x = 0, y = 0,
            w = image_width,
            h = image_height,
            color_hex = "000000",
            opacity_decimal = 0
        }, 1)

		create(badgeName, "image", {
			image_path = badgePath,
			x = (image_width - badgeSize) / 2,
			y = (image_height - badgeSize) / 2 + offsetY,
			w = badgeSize,
			h = badgeSize,
			show = false,
			opacity_decimal = 1
		}, 30)

        -- Utiliser dmd.gif pour un DMD, sinon biggoldencup.png
        local cupImagePath = 'RA/System/biggoldencup.png'
		local cupImageWidth = 238
		local cupImageHeight = 235
        if isDMD then
            cupImagePath = 'RA/System/dmd.gif'
			cupImageWidth = image_width
			cupImageHeight = image_height
        end

		create(cupName, "image", {
			image_path = cupImagePath,
			x = (image_width - cupImageWidth) / 2,
			y = (image_height - cupImageHeight) / 2,
			w = cupImageWidth,
			h = cupImageHeight,
			show = false,
			opacity_decimal = 1
		}, 20)

		-- Adapter la taille du texte pour un DMD
		local textSize = 70
		if isDMD then
			textSize = 20
		end

		mp.add_timeout(1, function()
			create(backgroundName, "image", {
                image_path = 'RA/System/background.png',
                x = 0,
                y = 0,
                w = image_width,
                h = image_height,
                show = false,
                opacity_decimal = 1
            }, 2)
			fade_opacity(backgroundShape,  0.9, 0.4, function()
				mp.add_timeout(0.6, function()
					set_object_properties(backgroundName, {show = true})
					mp.add_timeout(0.2, function()
						fade_opacity(backgroundShape,  0, 0, function()
							create(textAchievement, "text", {
								text = title .. "!",
								color = "FFFFFF",
								size = textSize,
								font = "VT323",
								align = 2,
								show = true,
								border_size = 5,
								opacity_decimal = 0
							}, 25)
							set_object_properties(cupName, {show = true})
							set_object_properties(badgeName, {show = true})
							mp.add_timeout(1, function()
								cache_screen("_cacheNewAchv", true, false, function()
									fade_opacity(textAchievement, 1, 0.6, function()
										mp.add_timeout(1, function()
											fade_opacity(textAchievement, 0, 0.6, function()
												-- Adapter la taille du texte dans la suite si besoin
												local newSize = textSize
												if isDMD then
													newSize = textSize - 4
												end
												set_object_properties(textAchievement, {size = newSize})
												set_object_properties(textAchievement, {text = "(" .. description .. ")"})
												mp.add_timeout(1, function()
													fade_opacity(textAchievement, 1, 0.6, function()
														mp.add_timeout(1, function()
															fade_opacity(textAchievement, 0, 0.6, function()
																newSize = newSize
																if isDMD then
																	newSize = newSize - 4
																end
																set_object_properties(textAchievement, {size = newSize})
																set_object_properties(textAchievement, {text = "+" .. points .. "pts"})
																mp.add_timeout(1, function()
																	fade_opacity(textAchievement, 1, 0.6, function()
																		mp.add_timeout(2, function()
																			fade_opacity(textAchievement, 0, 0.6, function()
																				remove_object(textAchievement)
																				fade_opacity(backgroundShape,  1, 0, function()
																					restore_cache_screen(initscreen)
																					fade_opacity(backgroundShape,  0, 1, function()
																						set_object_properties(backgroundShape, {show = false})
																						set_object_properties(badgeName, {show = false})
																						set_object_properties(cupName, {show = false})
																						set_object_properties(backgroundName, {show = false})
																						show_achievements(function()
																							print("Animation des achievements terminée")
																							mp.add_timeout(3, function()
																								cache_screen("_cacheAchv", true, true, function()
																									print("Image cache des achievements")
																									show_score()
																									remove_object(backgroundShape)
																									remove_object(badgeName)
																									remove_object(cupName)
																									remove_object(backgroundName)
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
    end
end



-- #####################################
-- ############# SHOW FUNCTIONS
-- #####################################

function show_achievements(callback)
    update_screen_dimensions(function()
        -- Les dimensions sont mises à jour ici
    end)

    if next(achievements_data) == nil then
        print("Aucun achievement à afficher.")
        if callback then callback() end
        return
    end

    -- Filtrer et trier les achievements débloqués
    local unlocked = {}
    for id, ach in pairs(achievements_data) do
        if ach.Unlock == "True" then
            table.insert(unlocked, {id = id, data = ach})
        end
    end
    table.sort(unlocked, function(a, b)
        return tonumber(a.data.DisplayOrder) < tonumber(b.data.DisplayOrder)
    end)
    local numUnlocked = #unlocked

    -- Définition des paramètres d'affichage selon le mode
    local xPos, yPos, badgeSize, spacing, yPosAdjustment
    if isDMD then
        -- Pour un DMD, chaque badge doit mesurer 32 de haut (et on suppose 32 de large pour un rendu carré)
        xPos = 2
        yPos = 2  -- marge verticale de départ
        badgeSize = 28
        spacing = 2  -- espacement entre badges
        yPosAdjustment = 0  -- éventuellement à ajuster pour centrer verticalement
    else
        -- Paramètres par défaut pour un écran LCD
        xPos = 4
        yPos = 14
        badgeSize = 64
        spacing = 4
        yPosAdjustment = 54
    end

    -- Calcul du nombre maximum de badges pouvant tenir horizontalement
    local maxBadges = math.floor((image_width - xPos) / (badgeSize + spacing))

    if isDMD then
        -- Pour le DMD, n'afficher que les derniers achievements débloqués
        local startIndex = math.max(1, numUnlocked - maxBadges + 1)
        local order = 10
        -- Position verticale : placer les badges en bas de l'écran (adaptation possible selon vos besoins)
        local badgeY = image_height - badgeSize - yPos
        for i = startIndex, numUnlocked do
            local achievement = unlocked[i]
            local achievementName = "AchievementImage" .. achievement.id
            create(achievementName, "image", {
                image_path = achievement.data.BadgeURL,
                x = xPos,
                y = badgeY,
                w = badgeSize,
                h = badgeSize,
                show = true,
                opacity_decimal = 1
            }, order)
            xPos = xPos + badgeSize + spacing
            order = order + 1
        end
    else
        -- Pour un écran LCD, on conserve l'affichage habituel (ici, on affiche jusqu'à 5 achievements)
        local sorted_achievements = {}
        for id, ach in pairs(achievements_data) do
            table.insert(sorted_achievements, {id = id, data = ach})
        end
        table.sort(sorted_achievements, function(a, b)
            local unlockA = a.data.Unlock == "True"
            local unlockB = b.data.Unlock == "True"
            if unlockA == unlockB then
                return tonumber(a.data.DisplayOrder) < tonumber(b.data.DisplayOrder)
            else
                return unlockA and not unlockB
            end
        end)
        local startIndexLCD = math.max(1, numUnlocked - 5)
        local endIndexLCD = math.min(startIndexLCD + maxBadges - 1, #sorted_achievements)
        local order = 10
        for i = startIndexLCD, endIndexLCD do
            local achievement = sorted_achievements[i]
            local achievementName = "AchievementImage" .. achievement.id
            create(achievementName, "image", {
                image_path = achievement.data.BadgeURL,
                x = xPos,
                y = image_height - yPos,
                w = badgeSize,
                h = badgeSize,
                show = true,
                opacity_decimal = 1
            }, order)
            xPos = xPos + badgeSize + spacing
            order = order + 1
        end
    end

    -- (Optionnel) Animation ou mise à jour supplémentaire des badges peut être ajoutée ici

    if callback then callback() end
end


local score = 0
function show_score()
    local scoreTextName = "GameScoreText"
    local currentPoints = 0
    local potentialPoints = 0

    -- Calculer le score total et le score potentiel
    for _, ach in pairs(achievements_data) do
        potentialPoints = potentialPoints + (tonumber(ach.Points) or 0)
        if ach.Unlock == "True" then
            currentPoints = currentPoints + (tonumber(ach.Points) or 0)
        end
    end

    -- Définir la couleur de bordure pour currentPoints
    local currentPointsBorderColor
    if hardcoreMode == "True" then
        currentPointsBorderColor = "FF0000"  -- Bleu si hardcore
    else
        currentPointsBorderColor = "808080"  -- Gris sinon
    end

    -- Construire le texte avec des styles différents pour chaque partie
    local scoreText = string.format("PTS {\\3c&H%s&}%d{\\3c&H000000&}/%d", currentPointsBorderColor, currentPoints, potentialPoints)

    -- Fonction pour mettre à jour le texte du score
    local function show_score_text(scoreText)
        local text_properties = {
            align = 9,
            text = scoreText,
            color = "FFFFFF",  -- Couleur de base du texte (blanc)
            size = 50,
            font = "VT323",
            border_size = 4
        }

        if gfx_objects[scoreTextName] then
            set_object_properties(scoreTextName, text_properties)
        else
            create(scoreTextName, "text", text_properties, 50)
        end
    end
    
    show_score_text(scoreText)
end



-- Register the 'push-ra' message for processing
mp.register_script_message("push-ra", process_data)
