# SASL VR Tutor launcher - started by "Start SASL Tutor.bat" in the same folder.
# Option 1: backend + ML streamer, then Unity opens VRClassroom in Play mode (Oculus OpenXR).
# Option 2: backend + ML streamer only.
# Needs (per PC): Python 3.12 with mediapipe + opencv-python, backend-api\venv
# with requirements installed, backend-api\.env (not in Git - ask the team),
# the project's Unity version installed through Unity Hub, and Meta Horizon Link.

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$root         = $PSScriptRoot                                  # the VR-SASOL-TUTOR folder
$unityProject = Join-Path $root "frontend-unity\SASLTutorVR"

function Show-Message($text, $icon = "Information") {
    [System.Windows.Forms.MessageBox]::Show($text, "SASL VR Tutor", "OK", $icon) | Out-Null
}

function Start-Window($title, $folder, $command) {
    Start-Process cmd.exe -ArgumentList "/k title $title & cd /d `"$root\$folder`" & $command"
}

function Start-BackendAndML {
    Start-Window "ML - landmark streamer" "ml-service"  "py -3.12 landmark_streamer.py --vr"
    Start-Window "Backend API"            "backend-api" "venv\Scripts\python.exe -m uvicorn app.main:app --reload"
}

# Waits until the backend answers /api/health (up to ~60 s).
function Wait-Backend {
    for ($i = 0; $i -lt 60; $i++) {
        try {
            $r = Invoke-RestMethod "http://127.0.0.1:8000/api/health" -TimeoutSec 2
            if ($r.status -eq "ok") { return $true }
        } catch { }
        Start-Sleep -Seconds 1
    }
    return $false
}

# The Unity version this project uses, from ProjectSettings\ProjectVersion.txt.
function Get-UnityVersion {
    $line = Select-String -Path "$unityProject\ProjectSettings\ProjectVersion.txt" -Pattern '^m_EditorVersion:\s*(\S+)'
    if ($line) { return $line.Matches[0].Groups[1].Value }
    return $null
}

# Finds that Unity version where Unity Hub installs editors.
function Get-UnityExe($version) {
    $dirs = @("$env:ProgramFiles\Unity\Hub\Editor")
    $secondary = "$env:APPDATA\UnityHub\secondaryInstallPath.json"
    if (Test-Path $secondary) {
        try { $p = Get-Content $secondary -Raw | ConvertFrom-Json; if ($p) { $dirs += $p } } catch { }
    }
    foreach ($d in $dirs) {
        $exe = Join-Path $d "$version\Editor\Unity.exe"
        if (Test-Path $exe) { return $exe }
    }
    return $null
}

# Path of Meta's (Oculus) OpenXR runtime, from the OpenXR registry list.
function Get-OculusRuntimeJson {
    $key = "HKLM:\SOFTWARE\Khronos\OpenXR\1\AvailableRuntimes"
    if (Test-Path $key) {
        $found = (Get-Item $key).Property | Where-Object { $_ -match "oculus" -and (Test-Path $_) } | Select-Object -First 1
        if ($found) { return $found }
    }
    foreach ($p in @("$env:ProgramFiles\Meta Horizon\Support\oculus-runtime\oculus_openxr_64.json",
                     "$env:ProgramFiles\Oculus\Support\oculus-runtime\oculus_openxr_64.json")) {
        if (Test-Path $p) { return $p }
    }
    return $null
}

# True if a Unity editor already has this project open (it holds Temp\UnityLockfile).
function Test-UnityOpen {
    $lock = "$unityProject\Temp\UnityLockfile"
    if (-not (Test-Path $lock)) { return $false }
    try { $s = [System.IO.File]::Open($lock, "Open", "ReadWrite", "None"); $s.Close(); return $false }
    catch { return $true }
}

function Start-UnityVR {
    if (Test-UnityOpen) {
        Show-Message "Unity already has the project open.`n`nOpen Assets/Scenes/VRClassroom there and press Play yourself."
        return
    }
    $version = Get-UnityVersion
    $exe = Get-UnityExe $version
    if (-not $exe) {
        Show-Message "Couldn't find Unity $version.`nInstall it through Unity Hub, then try again." "Warning"
        return
    }

    # Same as Unity's "Play Mode OpenXR Runtime = Oculus OpenXR": point OpenXR at Meta's runtime
    # for the Unity process we start.
    $runtime = Get-OculusRuntimeJson
    if ($runtime) { $env:XR_RUNTIME_JSON = $runtime }
    else { Show-Message "Couldn't find the Oculus OpenXR runtime. Is Meta Horizon Link installed?`nUnity will use the system default runtime." "Warning" }

    if (-not (Wait-Backend)) {
        Show-Message "The backend didn't answer within a minute - check the 'Backend API' window.`nStarting Unity anyway." "Warning"
    }

    # SASLLauncher.StartVR (Assets/Editor/SASLLauncher.cs) opens VRClassroom and presses Play.
    Start-Process $exe -ArgumentList "-projectPath `"$unityProject`" -executeMethod SASLLauncher.StartVR"
}

# ---- popup ----
$form = New-Object System.Windows.Forms.Form
$form.Text = "SASL VR Tutor"
$form.Size = New-Object System.Drawing.Size(360, 230)
$form.StartPosition = "CenterScreen"
$form.FormBorderStyle = "FixedDialog"
$form.MaximizeBox = $false
$form.MinimizeBox = $false
$form.TopMost = $true

$label = New-Object System.Windows.Forms.Label
$label.Text = "How do you want to start?`n(For VR: headset on and Link running first.)"
$label.Location = New-Object System.Drawing.Point(20, 15)
$label.Size = New-Object System.Drawing.Size(310, 35)
$form.Controls.Add($label)

$vr = New-Object System.Windows.Forms.Button
$vr.Text = "1) Run with VR"
$vr.Location = New-Object System.Drawing.Point(20, 60)
$vr.Size = New-Object System.Drawing.Size(300, 35)
$vr.DialogResult = [System.Windows.Forms.DialogResult]::Yes
$form.Controls.Add($vr)

$only = New-Object System.Windows.Forms.Button
$only.Text = "2) Only run backend and ML"
$only.Location = New-Object System.Drawing.Point(20, 105)
$only.Size = New-Object System.Drawing.Size(300, 35)
$only.DialogResult = [System.Windows.Forms.DialogResult]::No
$form.Controls.Add($only)

$form.AcceptButton = $vr
$choice = $form.ShowDialog()

switch ($choice) {
    "Yes" { Start-BackendAndML; Start-UnityVR }
    "No"  { Start-BackendAndML }
    default { }   # closed with X: do nothing
}
