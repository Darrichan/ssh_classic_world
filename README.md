# ssh_classic_world

SSH 无损双机控制器的当前源码快照。

## 内容

- `src/`：控制器、临时 Agent、路线与识别相关 C# 源码
- `build.ps1`：使用 Windows .NET Framework x64 编译器构建控制器与验证程序
- `bootstrap.ps1`：远端临时 Agent 启动脚本

本仓库不包含运行配置、凭据、路线数据、日志、截图、模型、可执行文件及历史备份。

## 构建

在 Windows PowerShell 中运行：

```powershell
.\build.ps1 -Root $PWD.Path
```
