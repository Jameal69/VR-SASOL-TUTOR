# SASL VR Tutor launcher - started by "Start SASL Tutor.bat" in the same folder.
# Opens the ML landmark streamer and the backend in their own windows.
# Needs (per PC): Python 3.12 with mediapipe + opencv-python, backend-api\venv
# with requirements installed, and backend-api\.env (not in Git - ask the team).

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$root = $PSScriptRoot   # the VR-SASOL-TUTOR folder

function Start-Window($title, $folder, $command) {
    Start-Process cmd.exe -ArgumentList "/k title $title & cd /d `"$root\$folder`" & $command"
}

function Start-BackendAndML {
    Start-Window "ML - landmark streamer" "ml-service"  "py -3.12 landmark_streamer.py --vr"
    Start-Window "Backend API"            "backend-api" "venv\Scripts\python.exe -m uvicorn app.main:app --reload"
}

# ---- popup ----
$form = New-Object System.Windows.Forms.Form
$form.Text = "SASL VR Tutor"
$form.Size = New-Object System.Drawing.Size(360, 210)
$form.StartPosition = "CenterScreen"
$form.FormBorderStyle = "FixedDialog"
$form.MaximizeBox = $false
$form.MinimizeBox = $false
$form.TopMost = $true

$label = New-Object System.Windows.Forms.Label
$label.Text = "How do you want to start?"
$label.Location = New-Object System.Drawing.Point(20, 18)
$label.AutoSize = $true
$form.Controls.Add($label)

$vr = New-Object System.Windows.Forms.Button
$vr.Text = "1) Run with VR"
$vr.Location = New-Object System.Drawing.Point(20, 50)
$vr.Size = New-Object System.Drawing.Size(300, 35)
$vr.DialogResult = [System.Windows.Forms.DialogResult]::Yes
$form.Controls.Add($vr)

$only = New-Object System.Windows.Forms.Button
$only.Text = "2) Only run backend and ML"
$only.Location = New-Object System.Drawing.Point(20, 95)
$only.Size = New-Object System.Drawing.Size(300, 35)
$only.DialogResult = [System.Windows.Forms.DialogResult]::No
$form.Controls.Add($only)

$form.AcceptButton = $vr
$choice = $form.ShowDialog()

switch ($choice) {
    "Yes" {
        Start-BackendAndML
        # TODO (VR extras, added later): e.g. open Meta Horizon Link / Unity here.
    }
    "No" {
        Start-BackendAndML
    }
    default { }   # closed with X: do nothing
}
