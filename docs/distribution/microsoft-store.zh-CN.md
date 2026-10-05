# Agent Controller Microsoft Store 打包

待发布版本：1.2.2 / MSIX 1.2.2.0。规范核对日期：2026-10-04。构建准备不代表已提交或通过商店审核。

## 产物

- `scripts/package-release.ps1 -Compact` 生成 Windows x64 精简 ZIP，依赖 .NET 10 Desktop Runtime；上限 30 MiB。
- `scripts/package-store.ps1` 生成自包含 x64 MSIX、SHA-256 和构建来源记录。使用 Partner Center 产品标识页给出的包名、发布者和显示名，不猜测身份。MSIX 未签名，交由商店签名，不作为可直接旁加载的已签名安装包。
- 版本默认读取 `app/AgentController.csproj` 的 `FileVersion`，商店版本末段必须为零。同一发布候选修正不重复升级版本。

```powershell
./scripts/package-store.ps1 -IdentityName '<Package/Identity/Name>' `
  -Publisher '<Package/Identity/Publisher>' `
  -PublisherDisplayName '<Package/Properties/PublisherDisplayName>'
./scripts/render-store-assets.ps1
```

需要项目指定的 .NET SDK 与包含 MakeAppx/MakePri 的 Windows SDK。商店脚本使用新的输出目录，避免覆盖已有提交产物；构建中间文件与最终 payload 分开。现有精简包脚本会替换同版本的本地 ZIP。

## 素材

`scripts/StoreAssets` 离线读取当前主窗口 XAML、主题与真实视图，以固定示例数据生成英文及简体中文各三张 1920×1080 PNG。不会构造主窗口的应用依赖，不读取真实账号、会话和偏好，不连接手柄或执行输入操作。离线构造窗口不调用 `Show`。这不是交互验收。

`packaging/store/listing.json` 保存商品描述、功能点和图片说明；隐私政策位于同目录的 `privacy-policy.txt`。复用现有品牌图标，商店包中的 `Assets/AppTile300.png` 可作为 300×300 商店图标。图片、上传记录、测试结果与审核备注留在 Git 忽略的 `dist/store/`。

两种语言的商品说明均提供 GitHub 源码、Issues 反馈入口及 LICENSE 链接；属性中的网站和支持 URL 使用同一项目地址。许可文案以仓库 LICENSE 为准，当前为 GNU General Public License v3.0 only（GPL-3.0-only）。允许商业使用；分发受 GPL 约束的作品时须履行提供对应源码等许可证义务，第三方素材沿用各自许可。

素材编码使用 WPF 的同步 PNG 编码器，仅在后台线程编码有限数量的内存图像；文件读取和 PNG 写入使用异步 API。打包脚本中的同步文件操作属于离线构建流程，不在应用 UI 线程执行。

## 验证与提交

运行现有测试、Release 编译和 `git diff --check`；保留失败记录，不把打包成功等同于测试通过。`verify-release.ps1 -IncludeCompact` 校验 ZIP 内容和校验和；MakeAppx 验证 MSIX 清单与包结构。需要分别确认商店支持的设备系列、两种语言的商品说明、截图、隐私政策、年龄分级及定价。

自动化验证不覆盖真实应用交互、全新机器安装、打包身份下的开机启动或硬件兼容性。当前应用开机启动实现仍使用注册表 Run 项，商店清单声明的 startupTask 尚未与该设置连接；商店安装后的启动项可由 Windows 设置管理，应用内此项不能视为已验证。

参考：[提交要求](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/create-app-submission)、[MSIX 上传](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/upload-app-packages)、[截图与图片](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/screenshots-and-images)。
