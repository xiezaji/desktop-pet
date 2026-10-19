' Pet 桌宠 —— 静默启动（不闪控制台窗口）
' 双击本文件等价于 start-pet.cmd，但没有任何窗口闪一下
Dim fso, sh, base
Set fso = CreateObject("Scripting.FileSystemObject")
base = fso.GetParentFolderName(WScript.ScriptFullName)
Set sh = CreateObject("WScript.Shell")
sh.CurrentDirectory = base
sh.Run """" & base & "\Pet.exe""", 0, False
