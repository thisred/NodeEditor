# Node Editor

一个基于 WPF 的可视化节点编辑器，支持通过连线编排逻辑流程并一键执行。

## 功能

- **可视化编辑**：拖拽创建节点，连线传递数据，框选/复制/粘贴/删除
- **节点系统**：4 种节点类型 — Event（事件入口）、Action（动作）、Task（任务）、Get（纯数据）
- **自动发现**：通过特性标注（`[Input]`/`[Output]`/`[ExecInput]`/`[ExecOutput]`）自动生成端口
- **执行引擎**：拓扑排序执行 Get 节点，沿 ExecOut 链执行 Action/Task
- **序列化**：JSON 导入/导出，支持保存和加载图
- **小地图**：右下角缩略图，点击跳转
- **日志面板**：底部可折叠、可拖拽调整大小的输出面板

## 项目结构

| 项目 | 说明 |
|------|------|
| `NodeEditor.Core` | 核心模型、特性、执行引擎、节点发现 |
| `NodeEditor.Serialization` | JSON 序列化/反序列化 |
| `NodeEditor.Samples` | 示例节点（数学运算、生命周期、变量、打印等） |
| `NodeEditor.Wpf` | WPF 编辑器界面 |

## 内置节点

- **生命周期**：OnStart、OnEnd
- **动作**：打印、设置变量
- **数据**：获取变量、字符串常量
- **数学**：加/减/乘/除运算

## 自定义节点

```csharp
[Node("我的节点", Category = "自定义", Color = "#4CAF50", Kind = NodeKind.Action)]
public class MyNode : NodeBase
{
    [ExecInput("In")]  public NodePort ExecIn { get; set; } = null!;
    [ExecOutput("Out")] public NodePort ExecOut { get; set; } = null!;
    [Input("值", typeof(double))] public NodePort Input { get; set; } = null!;
    [Output("结果", typeof(double))] public NodePort Output { get; set; } = null!;

    [NodeProperty("系数", Group = "参数", Order = 0)]
    public double Factor { get; set; } = 1.0;

    public override void Execute()
    {
        var v = Input.IsConnected ? (double)(Input.GetConnectedPort()?.Value ?? 0) : (double)(Input.Value ?? 0);
        Output.Value = v * Factor;
    }
}
```

## 运行

```bash
dotnet build NodeEditor.slnx
dotnet run --project NodeEditor.Wpf
```

## 技术栈

.NET 10 / WPF / MVVM / C#
