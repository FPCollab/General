hook = {}

hooks = {}

GameHooks = {}
GameMode = {}

function hook.Call(name, ...)

	local ret = {}
	if GameHooks[name] then GameHooks[name](...) end
	if GameMode[name] then GameMode[name](...) end
	for k, v in pairs((hooks[name] or {})) do
	
		local re = v(...)
		if re and re.instance_of and DataPacket and re:instance_of(DataPacket) then
			re = re:ToTable()
		end
		
		if re then
			table.insert(ret, re)
		end
	
	end
	return ret

end

function hook.Add(h, u, f)

	if not hooks[h] then hooks[h] = {} end
	hooks[h][u] = f

end

function hook.Remove(h, u)

	hooks[h][u] = nil

end

function hook.Clear(h)

	hooks[h] = nil

end

function hook.ClearAll()

	for k, v in pairs(hooks) do
	
		hook.Clear(k)
	
	end

end

--hook.Call("", ) --name, args