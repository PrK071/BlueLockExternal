local p=game.Players:GetPlayers()[1];local root=p.Character.HumanoidRootPart;local ball=workspace.PhysicalBall
if _G.VisualMotion then _G.VisualMotion:Disconnect();_G.VisualMotion=nil end
p.Character:PivotTo(CFrame.new(0,3,0));root.AssemblyLinearVelocity=Vector3.zero;root.AssemblyAngularVelocity=Vector3.zero
ball.Position=Vector3.new(BX,.8,BZ);ball.AssemblyLinearVelocity=Vector3.zero;ball.AssemblyAngularVelocity=Vector3.zero
workspace:SetAttribute("VisualRun","RUN");workspace:SetAttribute("VisualKind","KIND");workspace:SetAttribute("VisualYaw",YAW)
local start=os.clock()
if "KIND"=="moving" or "KIND"=="abrupt" then
 _G.VisualMotion=game:GetService("RunService").Heartbeat:Connect(function()
  local t=os.clock()-start;local v=ball.AssemblyLinearVelocity
  local speed="KIND"=="abrupt" and (math.floor(t)%2==0 and 2 or -2) or 1.2*math.cos(t)
  ball.AssemblyLinearVelocity=Vector3.new(speed,v.Y,0)
 end)
end
task.wait(.3)
return {run=workspace:GetAttribute("VisualRun"),ball=tostring(ball.Position),player=tostring(root.Position)}
