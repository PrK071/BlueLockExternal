-- Luau | Diagnostics/VisualE2E/Truth.server.luau | Studio Play only | Ground truth output only, never controller input.
assert(game:GetService("RunService"):IsStudio())
_G.VisualTruthEnabled=true
if not _G.VisualTruthStarted then
 _G.VisualTruthStarted=true
 task.spawn(function()
  local H=game:GetService("HttpService")
  while _G.VisualTruthEnabled and task.wait(.1) do
   local player=game.Players:GetPlayers()[1]
   local root=player and player.Character and player.Character:FindFirstChild("HumanoidRootPart")
   local ball=workspace:FindFirstChild("PhysicalBall")
   if root and ball then
    local p,b=root.Position,ball.Position
    local data={run=workspace:GetAttribute("VisualRun"),px=p.X,pz=p.Z,bx=b.X,bz=b.Z,py=p.Y,by=b.Y,clock=os.clock()}
    pcall(function() H:PostAsync("http://127.0.0.1:60137/truth-visual-e2e",H:JSONEncode(data),Enum.HttpContentType.ApplicationJson,false) end)
   end
  end
 end)
end
return "write-only evaluator truth sender active"

