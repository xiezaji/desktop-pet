' Pet 桌宠 —— 静默启动（不闪控制台窗口）
' 双击本文件等价于 start-pet.cmd，但没有任何窗口闪一下
Dim fso, sh, base
Set fso = CreateObject("Scripting.FileSystemObject")
base = fso.GetParentFolderName(WScript.ScriptFullName)
If Not fso.FileExists(base & "\Pet.exe") Then
  MsgBox "找不到 Pet.exe" & vbCrLf & vbCrLf & _
         "请确认下载/解压完整，Pet.exe 应与本文件在同一个文件夹里。", _
         16, "Pet 桌宠"
  WScript.Quit 1
End If
Set sh = CreateObject("WScript.Shell")
sh.CurrentDirectory = base
sh.Run """" & base & "\Pet.exe""", 0, False
