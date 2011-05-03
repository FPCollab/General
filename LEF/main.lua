function love.load() --LEF loading
	math.randomseed(os.time()) --Set up a random seed
	math.random() math.random() math.random() --toss the salad
	require("conf.lua") --load conf

	function include(dir) --Not much slower, allows for re-loading of files.
	
		return pcall(love.filesystem.load(dir)) --safety.
	
	end
	
	function requiredir(dir) --spacesaver 9000

		for k, v in pairs(love.filesystem.enumerate(dir)) do
			if love.filesystem.isFile(dir.."/"..v) then
				include(dir.."/"..v)
			end
		end

	end

	requiredir("/ext") --load them extensions
	require("mods/loader.lua") --load mods
	
	ents = {} --ents.
	fontsize = 12 --the basic font size
	sfont = love.graphics.newFont(fontsize) --basic font
	
	include("game/main.lua") --load the game
	
	hook.Call("Init") --call init
end

function love.update(dt) --update wrapper

	ParticleSys.Update() --update particle system wrapper
	
	hook.Call("Update", dt) --call update

end

function SetColor(c) --set color using my color class

	love.graphics.setColor(c.r, c.g, c.b, c.a)

end

function love.draw()

	hook.Call("Draw") --call draw

end

function love.mousepressed(x, y, b) --and more wrappers. whee

	hook.Call("MousePressed", x, y, b)

end

function love.mousereleased(x, y, b)

	hook.Call("MouseReleased", x, y, b)

end

function love.focus(f)

	hook.Call("Focus", f)

end