# Generates the Xcode project with Unity and publishes it as a GitHub release, which starts the
# iOS workflow. Close the Unity Editor first: batch builds cannot run while this project is open.
param([string]$Account = 'lffelinx-web', [string]$Repo = 'lffelinx-web/horizonte', [switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path "$PSScriptRoot\..").Path
if (-not $SkipBuild) {
  $unity = 'C:\Program Files\Unity\Hub\Editor\6000.6.7f1\Editor\Unity.exe'
  $log = "$root\Logs\horizonte-ios-build.log"
  $p = Start-Process $unity -ArgumentList @('-batchmode', '-projectPath', "`"$root`"", '-buildTarget', 'iOS', '-executeMethod', 'Horizonte.EditorTools.ProjectSetup.BuildIosProject', '-logFile', "`"$log`"") -Wait -PassThru -NoNewWindow
  if ($p.ExitCode -ne 0) { Select-String -Path $log -Pattern 'error CS|Error building' | Select-Object -First 20 | ForEach-Object { $_.Line }; throw "Unity failed ($($p.ExitCode)). See $log" }
}
$archive = "$root\Builds\horizonte-xcode.tar.gz"
tar.exe -czf $archive -C "$root\Builds\iOS" .
if ($LASTEXITCODE -ne 0) { throw 'Could not create the archive.' }
Write-Output ("Archive: {0:N0} MB" -f ((Get-Item $archive).Length / 1MB))
$env:GH_TOKEN = (gh auth token --user $Account)
try {
  $tag = 'ios-' + (Get-Date -Format 'yyyyMMdd-HHmm')
  gh release create $tag $archive -R $Repo --title "iOS $tag" --notes 'Proyecto de Xcode generado por Unity para la compilación de iOS.' --prerelease
  if ($LASTEXITCODE -ne 0) { throw 'Could not create the release.' }
  # Each release holds a 200-400 MB Xcode project: keep only the three newest.
  $old = gh release list -R $Repo --limit 100 --json tagName,createdAt -q 'sort_by(.createdAt) | reverse | .[3:] | .[].tagName'
  foreach ($t in $old) {
    gh release delete $t -R $Repo --yes --cleanup-tag
    Write-Output "Deleted old release $t"
  }
} finally { Remove-Item Env:GH_TOKEN }
Write-Output "Published $tag"
