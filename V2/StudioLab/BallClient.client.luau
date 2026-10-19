-- Luau | StudioLab/BallClient.client.luau | Roblox Studio only | Known camera, explicit trial controls and live laboratory HUD.
local RunService = game:GetService("RunService")
if not RunService:IsStudio() then return end
local Players = game:GetService("Players")
local UserInputService = game:GetService("UserInputService")
local player=Players.LocalPlayer
local bridge=game:GetService("ReplicatedStorage"):WaitForChild("BallLabBridge")
local angle,elapsed,active=0,0,true
local gui=Instance.new("ScreenGui")
gui.Name,gui.ResetOnSpawn,gui.Parent="BallControlLabHUD",false,player:WaitForChild("PlayerGui")
local label=Instance.new("TextLabel")
label.Size,label.Position=UDim2.fromOffset(790,70),UDim2.fromOffset(10,10)
label.BackgroundTransparency,label.TextColor3,label.TextSize=.25,Color3.new(1,1,1),17
label.Text,label.Parent="V2.2 LAB | Z câmera | T cenário | Y reiniciar | F12 emergência\nAquisição e chutes usam a mecânica própria deste laboratório.",gui
UserInputService.WindowFocused:Connect(function() active=true end)
UserInputService.WindowFocusReleased:Connect(function() active=false end)
UserInputService.InputBegan:Connect(function(input,processed)
    if processed then return end
    if input.KeyCode==Enum.KeyCode.Z then angle+=math.pi/2 end
    if input.KeyCode==Enum.KeyCode.T then bridge:FireServer({kind="case"}) end
    if input.KeyCode==Enum.KeyCode.Y then bridge:FireServer({kind="reset"}) end
    if input.KeyCode==Enum.KeyCode.F12 then bridge:FireServer({kind="stop"}) end
end)
bridge.OnClientEvent:Connect(function(packet)
    if typeof(packet)=="table" and packet.kind=="scenario" then
        angle=packet.yaw
        label.Text="V2.2 LAB | cenário "..packet.scenario.." | ensaio "..packet.trial.." | "..(packet.speed or 16).." studs/s\nZ câmera | T cenário | Y reiniciar | F12 emergência"
    end
end)
RunService.RenderStepped:Connect(function(dt)
    local character=player.Character
    local root=character and character:FindFirstChild("HumanoidRootPart")
    if not root then return end
    local camera=workspace.CurrentCamera
    camera.CameraType=Enum.CameraType.Scriptable
    camera.CFrame=CFrame.lookAt(root.Position+Vector3.new(32*math.sin(angle),24,32*math.cos(angle)),root.Position)
    elapsed+=dt
    if elapsed>=.08 then
        elapsed=0
        local r,f=camera.CFrame.RightVector,camera.CFrame.LookVector
        bridge:FireServer({rx=r.X,rz=r.Z,fx=f.X,fz=f.Z,active=active})
    end
end)
