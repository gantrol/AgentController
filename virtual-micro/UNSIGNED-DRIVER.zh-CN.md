# Codex Micro 未签名驱动包

[English](./UNSIGNED-DRIVER.md)

只想安装现成驱动，请使用[三步安装说明](../docs/CodexMicroSimulator-安装教程.zh-CN.md)。
本文仅保留构建、二次签名和证书细节。

Release 资产 `CodexMicroVhfUm-v1.0.0-win-x64-UNSIGNED-DEVELOPER.zip` 是开发者产物，
不是正式安装包。它提供预编译的 x64 UMDF2/VHF 二进制，方便审计者在不编译 C/C++
的情况下检查并二次签名。

包内不含证书或私钥。下载归档中的 DLL、catalog 和原生辅助程序均有意保持未签名。
UMDF DLL 和原生辅助程序均静态链接 C/C++ 运行库，因此该开发者包不另外依赖 Visual
C++ Redistributable。

## 需要什么

对预编译包进行本机二次签名和安装需要：

- Windows 10/11 x64，以及管理员 PowerShell；
- Windows SDK `10.0.26100.0` 中的 SignTool；
- 固定 WDK NuGet 包 `Microsoft.Windows.WDK.x64` `10.0.26100.6584` 中的 Inf2Cat；
- 如果本机尚未缓存 WDK NuGet 包，只需使用 Visual Studio/Build Tools 的 MSBuild
  完成还原；这条路线不需要重新编译 C/C++。

如果要重新构建二进制，请安装 Visual Studio/Build Tools 2022、**使用 C++ 的桌面
开发**、MSVC v143 x64/x86 工具、x64/x86 Spectre 缓解库、Windows SDK
`10.0.26100.0` 和 MSBuild。只有模拟器应用和测试需要 .NET 9 SDK，单独构建 UMDF2
驱动不需要它。

## 只还原签名工具，不编译

在解压目录中定位 MSBuild，并还原固定版本的 WDK NuGet 包：

```powershell
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$msbuild = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild `
  -find 'MSBuild\**\Bin\amd64\MSBuild.exe' | Select-Object -First 1

if (-not $msbuild) { throw '未找到 Visual Studio MSBuild。' }

& $msbuild '.\driver\CodexMicroVhfUm\CodexMicroVhfUm.vcxproj' `
  /restore /t:Restore /p:Configuration=Release /p:Platform=x64
```

## 最简单的本机二次签名路径

在解压后的 `virtual-micro` 目录打开管理员 PowerShell，运行：

```powershell
.\Install-CodexMicroDriver.ps1
```

脚本会生成不可导出的本机测试代码签名证书
`CN=Codex Micro Simulator Driver`，在该电脑上信任其公钥证书，为
`CodexMicroVhfUm.dll` 嵌入签名，删除并使用 Inf2Cat 重新生成 catalog，再签名新的
catalog，安装 `Root\CodexMicroHidUm`，最后验证设备健康状态。

必须在签 DLL 之后重新生成 catalog，因为 catalog 中记录的 DLL 哈希已经变化；顺序
倒置会得到无效的驱动包。

## 使用已有开发证书

下面是等价的手动顺序，假设证书和私钥已经位于 `Cert:\LocalMachine\My`。请替换
`<THUMBPRINT>`，必要时调整工具路径。如果证书位于当前用户证书库，请去掉 `/sm`。

```powershell
$package = (Resolve-Path '.\driver\CodexMicroVhfUm\x64\Release').Path
$signTool = 'C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64\signtool.exe'
$inf2Cat = Join-Path $env:USERPROFILE `
  '.nuget\packages\microsoft.windows.wdk.x64\10.0.26100.6584\c\bin\10.0.26100.0\x86\Inf2Cat.exe'
$thumbprint = '<THUMBPRINT>'

& $signTool sign /v /fd SHA256 /sha1 $thumbprint /sm `
  "$package\CodexMicroVhfUm.dll"

Remove-Item -LiteralPath "$package\CodexMicroVhfUm.cat" -Force
& $inf2Cat "/driver:$package" /os:10_X64

& $signTool sign /v /fd SHA256 /sha1 $thumbprint /sm `
  "$package\CodexMicroVhfUm.cat"

& $signTool verify /v /pa "$package\CodexMicroVhfUm.cat"
& $signTool verify /v /pa /c "$package\CodexMicroVhfUm.cat" `
  "$package\CodexMicroVhfUm.dll"
```

让目标电脑信任本地签发证书是另一项管理员操作。本地或企业开发签名不能代替微软的
生产驱动签名流程。不要共享私钥，也不要把本机生成的测试证书作为公开发行身份。
