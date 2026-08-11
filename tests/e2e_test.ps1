param(
    [Parameter(Mandatory = $true)]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })]
    [string]$Wav,
    [string]$Label = "MIX",
    [string]$ExpectedPattern = ""
)

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$resultDir = Join-Path $env:TEMP "VoicePromptTests"
New-Item -ItemType Directory -Path $resultDir -Force | Out-Null
$resultFile = Join-Path $resultDir "paste_result.txt"
if (Test-Path $resultFile) { Remove-Item $resultFile -Force }

$form = New-Object System.Windows.Forms.Form
$form.Text = "VP-DICTATION-TARGET"
$form.Width = 720
$form.Height = 440
$form.StartPosition = "Manual"
$form.Left = 60
$form.Top = 60
$form.TopMost = $true

$box = New-Object System.Windows.Forms.TextBox
$box.Multiline = $true
$box.Dock = "Fill"
$box.ScrollBars = "Vertical"
$box.AcceptsReturn = $true
$box.AcceptsTab = $true
$box.Font = New-Object System.Drawing.Font("Consolas", 14)
$lastLogged = ""
$ignoreChanges = $false
$box.Add_TextChanged({
    if ($ignoreChanges) { return }
    $t = $box.Text
    if ($t -and $t -ne $lastLogged) {
        $lastLogged = $t
        "[$(Get-Date -Format 'HH:mm:ss.fff')] $t" | Add-Content -Path $resultFile -Encoding UTF8
    }
})
$form.Controls.Add($box)
$form.Add_Shown({
    $box.Focus()
})
$form.Show()
$form.Activate()
$box.Focus() | Out-Null
Start-Sleep -Milliseconds 600
[System.Windows.Forms.Application]::DoEvents()

Write-Output "Target window 'VP-DICTATION-TARGET' is up (in-process)."

$sig = @"
using System;
using System.Runtime.InteropServices;
public static class KeySim
{
    [DllImport("user32.dll")] public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    public static void HoldF1() { keybd_event(0x70, 0, 0, UIntPtr.Zero); }
    public static void ReleaseF1() { keybd_event(0x70, 0, 2, UIntPtr.Zero); }
}
"@
Add-Type -TypeDefinition $sig

[KeySim]::SetForegroundWindow($form.Handle) | Out-Null
Start-Sleep -Milliseconds 500
[System.Windows.Forms.Application]::DoEvents()

[System.Windows.Forms.SendKeys]::SendWait("^a")
[System.Windows.Forms.Application]::DoEvents()
Start-Sleep -Milliseconds 200
[System.Windows.Forms.SendKeys]::SendWait("{DELETE}")
[System.Windows.Forms.Application]::DoEvents()
Start-Sleep -Milliseconds 300

Write-Output "Hotkey DOWN (recording start)"
[KeySim]::HoldF1()
Start-Sleep -Milliseconds 400

$player = New-Object System.Media.SoundPlayer($Wav)
$player.Play()
$info = & ffprobe -v error -show_entries format=duration -of csv=p=0 $Wav 2>$null
$wavDuration = if ($info) { [double]$info } else { 8.0 }
Write-Output "Playing $Wav ($wavDuration s)"
$end = Get-Date
for ($i = 0; $i -lt [math]::Ceiling($wavDuration + 0.7); $i++) { Start-Sleep -Milliseconds 1000; [System.Windows.Forms.Application]::DoEvents() }

[System.Windows.Forms.Application]::DoEvents()
$ignoreChanges = $true
$box.Clear()
$lastLogged = ""
if (Test-Path $resultFile) { Remove-Item $resultFile -Force }
$ignoreChanges = $false

Write-Output "Hotkey UP (transcribe + paste)"
[KeySim]::ReleaseF1()
$t1 = Get-Date

$firstLine = $null
$pasteTimeoutSeconds = [math]::Min(60, [math]::Max(10, 10 + ($wavDuration * 0.20)))
$pollCount = [math]::Ceiling($pasteTimeoutSeconds / 0.25)
for ($i = 0; $i -lt $pollCount; $i++) {
    [System.Windows.Forms.Application]::DoEvents()
    if (Test-Path $resultFile) {
        $lines = @(Get-Content $resultFile)
        $firstLine = if ($ExpectedPattern) {
            $lines | Where-Object { $_ -match $ExpectedPattern } | Select-Object -First 1
        } else {
            $lines | Select-Object -First 1
        }
        if ($firstLine) { break }
    }
    Start-Sleep -Milliseconds 250
}
if ($firstLine) {
    $stamp = ($firstLine -split '\]')[0] -replace '\[', ''
    Write-Output "TEXT-LANDED at $stamp (release was $($t1.ToString('HH:mm:ss.fff')))"
    Write-Output "CONTENT: $firstLine"
    $exitCode = 0
} else {
    $unexpected = if (Test-Path $resultFile) { (Get-Content $resultFile -Raw).Trim() } else { "" }
    Write-Output "RESULT: EXPECTED TEXT DID NOT ARRIVE within ~$([math]::Round($pasteTimeoutSeconds, 1))s"
    if ($unexpected) { Write-Output "UNEXPECTED: $unexpected" }
    $exitCode = 1
}
$form.Close()
Start-Sleep -Milliseconds 300
exit $exitCode
