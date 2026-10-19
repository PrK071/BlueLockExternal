-- Luau | StudioLab/Server.server.luau | Roblox Studio only | Operator-owned movement laboratory and localhost telemetry.
local RunService = game:GetService("RunService")
if not RunService:IsStudio() then return end
local Players = game:GetService("Players")
local HttpService = game:GetService("HttpService")
local ReplicatedStorage = game:GetService("ReplicatedStorage")
local endpoint = "__ENDPOINT__"
local bridge = Instance.new("RemoteEvent")
bridge.Name = "NavigationLabTelemetry"
bridge.Parent = ReplicatedStorage
local floor = Instance.new("Part")
floor.Name, floor.Anchored, floor.Size = "LabGround", true, Vector3.new(250, 1, 250)
floor.Position, floor.Color = Vector3.new(0, -0.5, 0), Color3.fromRGB(35, 140, 40)
floor.Parent = workspace
local target = Instance.new("Part")
target.Name, target.Shape, target.Anchored = "NavigationTarget", Enum.PartType.Ball, true
target.Size, target.CanCollide, target.Color = Vector3.new(2, 2, 2), false, Color3.new(1, 1, 1)
target.Parent = workspace
local cases = {Vector3.new(0, 1, -20), Vector3.new(0, 1, 20), Vector3.new(-20, 1, 0),
    Vector3.new(20, 1, 0), Vector3.new(16, 1, -16), Vector3.new(14, 1, -16), Vector3.new(0, 1, -20), Vector3.new(0, 1, -20)}
local caseIndex, caseAt, last, lastPlayer = 1, os.clock(), nil, nil
local seq, velocity = 0, Vector3.zero
local function reset(player)
    local character = player.Character
    if character then character:PivotTo(CFrame.new(0, 4, 0)) end
end
local function characterAdded(character)
    local root = character:WaitForChild("HumanoidRootPart", 10)
    if root then character:PivotTo(CFrame.new(0, 4, 0)) end
    local humanoid = character:FindFirstChildOfClass("Humanoid")
    if humanoid then humanoid.WalkSpeed = 16 end
end
local function playerAdded(player)
    player.CharacterAdded:Connect(characterAdded)
    if player.Character then task.spawn(characterAdded, player.Character) end
end
Players.PlayerAdded:Connect(playerAdded)
for _, player in Players:GetPlayers() do playerAdded(player) end
bridge.OnServerEvent:Connect(function(player, packet)
    if typeof(packet) ~= "table" then return end
    if packet.kind == "reset" then reset(player); return end
    if packet.kind == "case" then
        caseIndex = caseIndex % #cases + 1
        caseAt = os.clock()
        reset(player)
        return
    end
    if typeof(packet.rx) ~= "number" or typeof(packet.rz) ~= "number" or typeof(packet.fx) ~= "number" or typeof(packet.fz) ~= "number" then return end
    last, lastPlayer = packet, player
end)
RunService.Heartbeat:Connect(function()
    local elapsed = os.clock() - caseAt
    target.Position = cases[caseIndex]
    velocity = Vector3.zero
    if caseIndex == 6 then
        target.Position += Vector3.new(6 * math.sin(elapsed * 0.5), 0, 0)
        velocity = Vector3.new(3 * math.cos(elapsed * 0.5), 0, 0)
    end
    target.Transparency = caseIndex == 8 and elapsed % 6 > 2 and elapsed % 6 < 3 and 1 or 0
end)
task.spawn(function()
    local warned = false
    while task.wait(0.2) do
        local character = lastPlayer and lastPlayer.Character
        local root = character and character:FindFirstChild("HumanoidRootPart")
        local humanoid = character and character:FindFirstChildOfClass("Humanoid")
        if root and humanoid and last then
            seq += 1
            local packet = {studio = true, seq = seq, px = root.Position.X, pz = root.Position.Z,
                tx = target.Position.X, tz = target.Position.Z, vx = velocity.X, vz = velocity.Z,
                rx = last.rx, rz = last.rz, fx = last.fx, fz = last.fz, speed = humanoid.WalkSpeed,
                grounded = humanoid.FloorMaterial ~= Enum.Material.Air, visible = target.Transparency < 1,
                caseId = caseIndex, active = last.active == true}
            local ok, err = pcall(function()
                HttpService:PostAsync(endpoint, HttpService:JSONEncode(packet), Enum.HttpContentType.ApplicationJson, false)
            end)
            if not ok and not warned then warned = true; warn("NavigationLab telemetry: " .. tostring(err)) end
        end
    end
end)
