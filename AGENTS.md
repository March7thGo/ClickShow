# 项目约定

## 项目与资料

- ClickShow 是 Windows 10 22H2 / Windows 11 x64 的鼠标点击波纹工具，使用 C#、.NET 10、WinUI 3、Win32 和 Windows Composition。
- 功能与边界、运行和构建方法、验证范围见 `README.md`。修改行为前先核对相关说明。
- `src/` 是唯一应用项目，`tests/` 放逻辑测试，`installer/` 放 Inno Setup 脚本，`scripts/` 放构建和诊断脚本；生成物统一放在 `artifacts/`。

## 修改原则

- 只修改当前任务涉及的代码，保留工作区中无关的未提交改动。保持现有职责划分，不为假设中的需求增加接口层、框架或兜底逻辑。

## 构建与验证

- 每次修改后，先终止本机现有的 ClickShow 进程，重新构建并启动修改后的程序；失败时明确说明原因。
- 逻辑测试：`pwsh -File scripts/Test.ps1`；发布与安装包：`pwsh -File scripts/Build.ps1 -Edition Both`。仅需发布目录时加 `-SkipInstaller`。
- 运行诊断与压力测试分别使用 `scripts/Test.ps1 -Runtime`、`scripts/Test.ps1 -Stress`；单实例测试使用 `scripts/Test-Instance.ps1`。运行前退出已有 ClickShow 实例。
- 报告中区分编译、逻辑测试、运行诊断和实机验收；合成压力输入不能证明真实鼠标、多屏、UAC、安装升级或长期性能表现。
