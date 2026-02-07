' Run systray monitor in background (no console window).
' Double-click this file or run from Task Scheduler / Startup.

Set WshShell = CreateObject("WScript.Shell")
scriptDir = Replace(WScript.ScriptFullName, "\systray_monitor.vbs", "")
WshShell.CurrentDirectory = scriptDir

' Use pythonw so no console appears; fallback to python if pythonw missing
pythonw = "pythonw"
pythonExe = "python"

' Try pythonw first (no window)
On Error Resume Next
WshShell.Run pythonw & " """ & scriptDir & "\systray_monitor.py""", 0, False
If Err.Number <> 0 Then
  Err.Clear
  WshShell.Run pythonExe & " """ & scriptDir & "\systray_monitor.py""", 0, False
End If
