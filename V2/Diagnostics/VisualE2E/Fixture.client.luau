-- Luau | Diagnostics/VisualE2E/Fixture.client.luau | Studio Play only | Visible calibration markers, actor marker, camera and controlled perception faults.
assert(game:GetService("RunService"):IsStudio())
local player=game.Players.LocalPlayer
local original=player.PlayerScripts:FindFirstChild("BallControlLabClient")
if original then original.Enabled=false end
local old=workspace:FindFirstChild("VisualE2E");if old then old:Destroy() end
local lab=Instance.new("Folder");lab.Name="VisualE2E";lab.Parent=workspace
local colors={Color3.fromRGB(255,0,0),Color3.fromRGB(255,255,0),Color3.fromRGB(0,255,255),Color3.fromRGB(0,0,255)}
local coords={Vector3.new(-20,.04,-20),Vector3.new(20,.04,-20),Vector3.new(20,.04,20),Vector3.new(-20,.04,20)}
for i,pos in coords do local a=Instance.new("Part");a.Name="Fiducial"..i;a.Anchored=true;a.CanCollide=false;a.Material=Enum.Material.Neon;a.Color=colors[i];a.Size=Vector3.new(2,.05,2);a.Position=pos;a.Parent=lab end
local marker=Instance.new("Part");marker.Name="PlayerMarker";marker.Anchored=true;marker.CanCollide=false;marker.Material=Enum.Material.Neon;marker.Color=Color3.fromRGB(255,0,255);marker.Shape=Enum.PartType.Cylinder;marker.Size=Vector3.new(.04,4,4);marker.Parent=lab
local blackout=Instance.new("ScreenGui");blackout.Name="VisualFault";blackout.IgnoreGuiInset=true;blackout.ResetOnSpawn=false;blackout.Parent=player.PlayerGui
local panel=Instance.new("Frame");panel.Size=UDim2.fromScale(1,1);panel.BackgroundColor3=Color3.new(0,0,0);panel.Visible=false;panel.Parent=blackout
local run,lastRun,started=nil,nil,os.clock()
game:GetService("RunService"):UnbindFromRenderStep("VisualE2ECamera")
game:GetService("RunService"):BindToRenderStep("VisualE2ECamera",Enum.RenderPriority.Camera.Value+10,function()
 local cam=workspace.CurrentCamera;local yaw=workspace:GetAttribute("VisualYaw") or 0
 cam.CameraType=Enum.CameraType.Scriptable;cam.CFrame=CFrame.lookAt(Vector3.new(0,60,0),Vector3.zero,Vector3.new(math.sin(yaw),0,-math.cos(yaw)))
 local root=player.Character and player.Character:FindFirstChild("HumanoidRootPart")
 if root then marker.CFrame=CFrame.new(root.Position.X,.04,root.Position.Z)*CFrame.Angles(0,0,math.pi/2) end
 run=workspace:GetAttribute("VisualRun");if run~=lastRun then lastRun=run;started=os.clock() end
 local age=os.clock()-started;local kind=workspace:GetAttribute("VisualKind") or ""
 local ball=workspace:FindFirstChild("PhysicalBall")
 local fault=age>1 and age<2.5
 if ball then
 ball.LocalTransparencyModifier=kind=="occlusion" and fault and 1 or 0
 ball.Color=kind=="perception" and fault and Color3.new(0,0,0) or Color3.new(1,1,1)
 end
 panel.Visible=kind=="tracking-loss" and fault
end)
local hud=player.PlayerGui:FindFirstChild("BallControlLabHUD");if hud then hud.Enabled=false end
return "visual fixture active"

