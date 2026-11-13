# 双击修复器

Windows 平台的键盘 / 鼠标双击故障修复工具。通过全局输入钩子识别微动或轴体老化产生的抖动碎片，把一次物理按压合并成一次干净事件。

## 功能

- 鼠标左 / 右 / 中 / 侧键与任意键盘按键独立配置
- 每个按键可单独启用、单独设置扣留时长（0~120ms）
- 深度模式：扣留 UP 事件，修复拖动中断与单击变双击
- 轻量模式（扣留 0ms）：只丢弃重复 DOWN，零额外延迟
- 总开关、开机自启、增强模式（管理员权限）、关闭后驻留托盘
- 配置文件热加载，外部编辑后立即生效

## 构建与运行

```powershell
dotnet build DoubleClickHealer.sln
dotnet run --project src/DoubleClickHealer.App
```

单元测试：

```powershell
dotnet test tests/DoubleClickHealer.Core.Tests
```

发布自包含单文件：

```powershell
dotnet publish src/DoubleClickHealer.App/DoubleClickHealer.App.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:PublishReadyToRun=true -o artifacts/publish
```

## 配置文件

`%AppData%\DoubleClickHealer\config.json`

```json
{
  "version": 1,
  "enabled": true,
  "runAtStartup": false,
  "startMinimized": true,
  "minimizeToTray": true,
  "elevatedMode": false,
  "targets": [
    { "kind": "Mouse", "code": 1, "enabled": true, "holdMs": 40 },
    { "kind": "Keyboard", "code": 65, "enabled": true, "holdMs": 0 }
  ]
}
```

- 鼠标 `code`：1 左键、2 右键、4 中键、8/16 侧键
- 键盘 `code`：虚拟键码（A = 65），左右修饰键为 0xA0~0xA5
- `holdMs = 0` 表示轻量模式

## 架构

```
src/DoubleClickHealer.Core
  Engine/DebounceState    单键防抖状态机（纯逻辑，可单测）
  Engine/HealerEngine     规则热替换与编排
  Engine/ReleaseTimer     高精度可等待定时器，空闲零 CPU
  Input/InputHook         WH_MOUSE_LL / WH_KEYBOARD_LL + 看门狗
  Input/InputInjector     串行注入队列（SendInput）
  Config/ConfigStore      JSON 原子写入 + 热加载
  KeyRecorder             按键录制钩子

src/DoubleClickHealer.App
  WPF 主界面、托盘、单实例、开机自启、增强模式

tools/DoubleClickHealer.PoC         真实设备手动验证
tools/DoubleClickHealer.Simulator   时序回归（注入带测试签名的事件）
```

### 关键约束

- 钩子回调只做时间戳判定，绝不阻塞，绝不调用 `SendInput`
- 所有注入走独立单线程队列，避免钩子重入与 300ms 超时
- 注入事件带 `dwExtraInfo` 魔数，钩子据此放行，避免自噬
- 抖动判定结合「UP→DOWN 间隔」与「上次按压时长」，避免误吞真实快速双击

## 已知限制

- 低级钩子对以管理员运行的程序无效（UIPI）；开启增强模式可覆盖
- 使用 RawInput / DirectInput 的游戏可能绕过
- UAC 安全桌面与登录界面不生效
