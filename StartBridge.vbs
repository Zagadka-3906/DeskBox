Option Explicit

Dim files, shell, executable
Set files = CreateObject("Scripting.FileSystemObject")
Set shell = CreateObject("WScript.Shell")
executable = files.BuildPath(files.GetParentFolderName(WScript.ScriptFullName), "DormElectricityBridge.exe")
WScript.Quit shell.Run(Chr(34) & executable & Chr(34), 0, True)
