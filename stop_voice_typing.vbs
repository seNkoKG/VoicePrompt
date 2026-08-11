Set WshShell = CreateObject("WScript.Shell")
DaemonExe = WshShell.ExpandEnvironmentStrings("%USERPROFILE%\.voice-typing\venv\Scripts\faster-whisper-dictation.exe")
WshShell.Run """" & DaemonExe & """ stop", 0, True
