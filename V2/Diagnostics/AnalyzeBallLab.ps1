# PowerShell | Diagnostics/AnalyzeBallLab.ps1 | Windows PowerShell 5.1+ | Read saved physical trial evidence; no input injection.
param(
    [Parameter(Mandatory=$true)][string]$CycleLog,
    [Parameter(Mandatory=$true)][string]$AttemptsLog,
    [string]$OutputFolder = (Join-Path $PSScriptRoot '..\reports\ball-v22\physical'),
    [double]$GoalAimY = 3,
    [switch]$RequireCompleteSuite
)
$ErrorActionPreference='Stop'
function Percentile($values,[double]$fraction) {
    $sorted=@($values | Sort-Object)
    if($sorted.Count -eq 0){return $null}
    return $sorted[[Math]::Floor(($sorted.Count-1)*$fraction)]
}
function ElapsedTo($frames,[string]$stage,[double]$start) {
    $first=@($frames | Where-Object {$_.stage -eq $stage} | Select-Object -First 1)
    if($first.Count -eq 0){return $null}
    return [double]$first[0].time-$start
}
$frames=@(Get-Content -LiteralPath $CycleLog | Where-Object {$_} | ForEach-Object {$_ | ConvertFrom-Json})
$attempts=@(Get-Content -LiteralPath $AttemptsLog | Where-Object {$_} | ForEach-Object {$_ | ConvertFrom-Json})
$resources=@($frames | Where-Object {!$_.paused -and $_.private_mb -gt 0})
$trials=@(foreach($attempt in $attempts) {
    $trialFrames=@($frames | Where-Object {$_.trial -eq $attempt.trial})
    $start=if($trialFrames.Count){[double]$trialFrames[0].time}else{0}
    $crossing=@($trialFrames | Where-Object {$_.shot_id -eq $attempt.shot_id -and ($_.result -eq 'GOAL' -or $_.result -eq 'MISS')} | Select-Object -First 1)
    $crossError=$null
    if($crossing.Count -and $crossing[0].cross_z -lt $crossing[0].goal_z) {
        $dx=[double]$crossing[0].cross_x-[double]$crossing[0].goal_x
        $dy=[double]$crossing[0].cross_y-$GoalAimY
        $crossError=[Math]::Sqrt($dx*$dx+$dy*$dy)
    }
    $expected=if($attempt.scenario -eq 8){'SHOT_FAILED'}else{'GOAL_CONFIRMED'}
    $required=@('APPROACH_BALL','VERIFY_POSSESSION','CARRY_BALL','PREPARE_SHOT','SHOOT','VERIFY_GOAL')
    $missing=@($required | Where-Object {$trialFrames.stage -notcontains $_})
    $owned=@($trialFrames | Where-Object {$_.owner_id -eq 1 -and $_.constraint -and $_.possession -eq 'BALL_CONTROLLED'}).Count -ge 2
    $physicalShot=@($trialFrames | Where-Object {$_.shot_id -eq $attempt.shot_id -and $_.shot_id -gt 0}).Count -gt 0
    $resultEvidence=$crossing.Count -gt 0
    if($expected -eq 'GOAL_CONFIRMED' -and $crossing.Count) {
        $c=$crossing[0]
        $resultEvidence=$c.result -eq 'GOAL' -and [Math]::Abs($c.cross_x-$c.goal_x) -le 7.2 -and $c.cross_y -ge .8 -and $c.cross_y -le 7.2 -and $c.cross_z -le $c.goal_z-.8+.001 -and $c.goal_counter -gt $trialFrames[0].goal_counter
    }
    $fixtureEvidence=$true
    if($attempt.scenario -eq 6) {$fixtureEvidence=$attempt.recoveries -gt 0 -and @($trialFrames | Where-Object {$_.stage -eq 'RECOVERING' -and $_.possession -eq 'BALL_LOST'}).Count -gt 0}
    if($attempt.scenario -eq 9) {$fixtureEvidence=@($trialFrames | Where-Object {!$_.visible}).Count -gt 0}
    if($attempt.scenario -eq 10) {$fixtureEvidence=$attempt.recoveries -gt 0 -and $trialFrames.stage -contains 'RECOVERING'}
    [pscustomobject]@{
        trial=$attempt.trial;scenario=$attempt.scenario;shot_id=$attempt.shot_id
        outcome=$attempt.outcome;expected=$expected
        passed=($attempt.outcome -eq $expected -and $missing.Count -eq 0 -and $owned -and $physicalShot -and $resultEvidence -and $fixtureEvidence)
        missing_stages=($missing -join ';');ownership_evidence=$owned;physical_shot_evidence=$physicalShot;result_evidence=$resultEvidence;fixture_evidence=$fixtureEvidence
        duration_seconds=$attempt.duration;recoveries=$attempt.recoveries
        acquisition_seconds=(ElapsedTo $trialFrames 'CARRY_BALL' $start)
        prepare_seconds=(ElapsedTo $trialFrames 'PREPARE_SHOT' $start)
        shoot_seconds=(ElapsedTo $trialFrames 'VERIFY_GOAL' $start)
        trajectory_observation_rmse=$attempt.trajectory_rmse;trajectory_samples=$attempt.trajectory_samples
        goal_plane_aim_error_studs=$crossError;reason=$attempt.reason
    }
})
$passed=@($trials | Where-Object {$_.passed}).Count
$summary=[pscustomobject]@{
    schema='ball-lab-physical-analysis-v1';cycle_log=[IO.Path]::GetFullPath($CycleLog)
    attempts_log=[IO.Path]::GetFullPath($AttemptsLog);observed_frames=$frames.Count;completed_trials=$trials.Count
    passed=$passed;failed=($trials.Count-$passed)
    acceptance_percent=if($trials.Count){100.0*$passed/$trials.Count}else{$null}
    goals=@($trials | Where-Object {$_.outcome -eq 'GOAL_CONFIRMED'}).Count
    cpu_normalized_percent_p50=(Percentile @($resources | ForEach-Object {[double]$_.cpu_percent}) .5)
    cpu_normalized_percent_p95=(Percentile @($resources | ForEach-Object {[double]$_.cpu_percent}) .95)
    cpu_normalized_percent_max=($resources | Measure-Object cpu_percent -Maximum).Maximum
    private_mib_peak=($resources | Measure-Object private_mb -Maximum).Maximum
    working_mib_peak=($resources | Measure-Object working_mb -Maximum).Maximum
    observed_movement_speeds=@($resources | Where-Object {$_.PSObject.Properties.Name -contains 'movement_speed'} | ForEach-Object {[Math]::Round([double]$_.movement_speed,3)} | Sort-Object -Unique)
    requested_movement_speeds=@($resources | Where-Object {$_.PSObject.Properties.Name -contains 'requested_speed'} | ForEach-Object {$_.requested_speed} | Sort-Object -Unique)
    duration_seconds_p50=(Percentile @($trials | ForEach-Object {[double]$_.duration_seconds}) .5)
    notes='Process CPU normalized by logical processors; RAM is total panel process, excludes Studio. Attempt duration excludes explicit focus pauses; stage arrival times use wall timestamps. Shot telemetry is sparse and observation RMSE includes post-crossing collisions, so it is not an in-flight ballistic accuracy metric. Plane aim error compares the server crossing with the configured target height. A deliberate miss is expected in scenario 8.'
}
New-Item -ItemType Directory -Force -Path $OutputFolder | Out-Null
$trials | Export-Csv -LiteralPath (Join-Path $OutputFolder 'trials.csv') -NoTypeInformation -Encoding UTF8
$summary | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $OutputFolder 'summary.json') -Encoding UTF8
$summary | ConvertTo-Json -Depth 4
if($RequireCompleteSuite -and ($trials.Count -ne 10 -or @($trials.scenario | Sort-Object -Unique).Count -ne 10 -or $passed -ne 10)) {
    throw 'Physical acceptance requires all ten distinct scenarios and their corresponding lifecycle, ownership, shot, result and fixture evidence.'
}
