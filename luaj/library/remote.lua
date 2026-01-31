local Socket = luajava.bindClass("java.net.Socket")
local InetSocketAddress = luajava.bindClass("java.net.InetSocketAddress")
local InputStreamReader = luajava.bindClass("java.io.InputStreamReader")
local BufferedReader = luajava.bindClass("java.io.BufferedReader")
local PrintWriter = luajava.bindClass("java.io.PrintWriter")

--client info
local playername = Player:getPlayer():getName():getString()
local version = Client:mcVersion()
local server = World:getCurrentServerAddress()

while true do Client:waitTick()

-- Create socket and connect
local socket = luajava.new(Socket)
local addr = luajava.new(InetSocketAddress, "::1", 8080)

if not socket then Chat:log("No socket") return nil end
if not addr then Chat:log("No address") return nil end

local success, err = pcall(function() socket:connect(addr) end)
if not success then goto reconnect end

--Get input stream
local isReader = luajava.new(InputStreamReader, socket:getInputStream(), "UTF-8")
local reader = luajava.new(BufferedReader, isReader)

-- Send identification
local writer = luajava.new(PrintWriter, socket:getOutputStream(), true)
local clientInfo = string.format("IDENTIFY|%s|%s|%s", playername, version, server or "singleplayer")
writer:println(clientInfo)

--Receive and execute commands
while true do

local success, command = pcall(function() return reader:readLine() end)

if command == nil then Chat:log("Connection Lost")
    break
        else
    local func, err = load(command)
    if func then
            local success, result = pcall(func)
            if not success then Chat:log("§cExecution Failed: \n".. tostring(result)) end
        end
    end
end

-- Close
writer:close()
reader:close()
socket:close()
    ::reconnect::
end