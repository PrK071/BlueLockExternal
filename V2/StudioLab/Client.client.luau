-- Luau | StudioLab/Client.client.luau | Roblox Studio only | Camera orientation and explicit lab controls.
local RunService = game:GetService("RunService")
if not RunService:IsStudio() then return end
local Players = game:GetService("Players")
local UserInputService = game:GetService("UserInputService")
local player = Players.LocalPlayer
local bridge = game:GetService("ReplicatedStorage"):WaitForChild("NavigationLabTelemetry")
local angle, elapsed = 0, 0
local active = true
UserInputService.WindowFocused:Connect(function() active = true end)
UserInputService.WindowFocusReleased:Connect(function() active = false end)
local gui = Instance.new("ScreenGui")
gui.Name, gui.ResetOnSpawn = "NavigationLab", false
gui.Parent = player:WaitForChild("PlayerGui")
local label = Instance.new("TextLabel")
label.Size, label.Position = UDim2.fromOffset(700, 55), UDim2.fromOffset(20, 10)
label.BackgroundTransparency, label.TextColor3 = 0.3, Color3.new(1, 1, 1)
label.TextSize = 18
label.Text = "LAB: Z gira câmera 90° | T troca alvo | Y reinicia posição | F12 para controle externo"
label.Parent = gui
UserInputService.InputBegan:Connect(function(input, processed)
    if processed then return end
    if input.KeyCode == Enum.KeyCode.Z then angle += math.pi / 2 end
    if input.KeyCode == Enum.KeyCode.T then bridge:FireServer({kind = "case"}) end
    if input.KeyCode == Enum.KeyCode.Y then bridge:FireServer({kind = "reset"}) end
end)
RunService.RenderStepped:Connect(function(dt)
    local character = player.Character
    local root = character and character:FindFirstChild("HumanoidRootPart")
    if not root then return end
    local camera = workspace.CurrentCamera
    camera.CameraType = Enum.CameraType.Scriptable
    camera.CFrame = CFrame.lookAt(root.Position + Vector3.new(42 * math.sin(angle), 30, 42 * math.cos(angle)), root.Position)
    elapsed += dt
    if elapsed >= 0.08 then
        elapsed = 0
        local right, forward = camera.CFrame.RightVector, camera.CFrame.LookVector
        bridge:FireServer({rx = right.X, rz = right.Z, fx = forward.X, fz = forward.Z, active = active})
    end
end)
