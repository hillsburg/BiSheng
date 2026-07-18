using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using BiSheng.Editor.Controls.MarkdownEditor.Themes;

namespace BiSheng.Editor.Tests;

/// <summary>
/// 渲染引擎测试窗口
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // 默认加载综合文档
        OnTestFullDocument(sender, e);

        // 监听编辑器状态
        TestEditor.TextEditor.TextArea.Caret.PositionChanged += (s, args) => UpdateStatusBar();
        TestEditor.TextEditor.TextChanged += (s, args) => UpdateCharCount();
    }

    private void UpdateStatusBar()
    {
        LineInfo.Text = $"行: {TestEditor.TextEditor.TextArea.Caret.Line}";
        ColInfo.Text = $"列: {TestEditor.TextEditor.TextArea.Caret.Column}";
    }

    private void UpdateCharCount()
    {
        CharCount.Text = $"字符: {(TestEditor.Text?.Length ?? 0)}";
    }

    private void SetTestContent(string content, string testName)
    {
        var sw = Stopwatch.StartNew();
        TestEditor.Text = content;
        sw.Stop();
        RenderTime.Text = $"渲染: {sw.ElapsedMilliseconds}ms";
        StatusText.Text = $"已加载: {testName}";
        UpdateCharCount();
    }

    // ========== 测试用例 ==========

    private void OnTestBasicSyntax(object sender, RoutedEventArgs e)
    {
        var content = @"# 一级标题
## 二级标题
### 三级标题
#### 四级标题
##### 五级标题
###### 六级标题

这是一段普通文本，没有任何 Markdown 语法。

# 另一个标题

普通段落跟在标题后面。";

        SetTestContent(content, "基础语法 - 标题");
    }

    private void OnTestInlineStyles(object sender, RoutedEventArgs e)
    {
        var content = @"# 行内样式测试

## 加粗文本

这是 **加粗文本** 使用双星号。

这是 __加粗文本__ 使用双下划线。

## 斜体文本

这是 *斜体文本* 使用单星号。

这是 _斜体文本_ 使用单下划线。

## 行内代码

使用 `Console.WriteLine()` 输出调试信息。

变量名 `myVariable` 应该用代码样式。

## 链接

访问 [GitHub](https://github.com) 获取更多信息。

点击 [文档链接](https://docs.microsoft.com) 查看文档。

## 混合样式

这是一段包含 **加粗** 和 *斜体* 以及 `代码` 的文本。

**加粗中包含 *斜体*** 的测试。

## 复杂行内组合

这段文字有 **加粗** 然后 *斜体* 然后 `代码` 再回到普通文本。";

        SetTestContent(content, "行内样式测试");
    }

    private void OnTestLists(object sender, RoutedEventArgs e)
    {
        var content = @"# 列表测试

## 无序列表

- 第一项
- 第二项
- 第三项

## 使用星号的无序列表

* 苹果
* 香蕉
* 橙子

## 有序列表

1. 第一步：准备环境
2. 第二步：编写代码
3. 第三步：运行测试

## 嵌套内容

- 外层项目 A
  内层说明
- 外层项目 B
  更多说明
- 外层项目 C

## 混合列表

1. 有序第一项
2. 有序第二项
- 无序项目
- 另一个无序项目";

        SetTestContent(content, "列表测试");
    }

    private void OnTestCodeBlocks(object sender, RoutedEventArgs e)
    {
        var content = @"# 代码块测试

## C# 代码块

```csharp
using System;

public class Program
{
    public static void Main(string[] args)
    {
        Console.WriteLine(""Hello, BiSheng!"");
        Console.WriteLine($""Time: {DateTime.Now}"");
    }
}
```

## JavaScript 代码块

```javascript
function greet(name) {
    return `Hello, ${name}!`;
}

const result = greet('BiSheng');
console.log(result);
```

## Python 代码块

```python
def fibonacci(n):
    if n <= 1:
        return n
    return fibonacci(n-1) + fibonacci(n-2)

for i in range(10):
    print(fibonacci(i))
```

## 无语言标注的代码块

```
这是一个没有指定语言的代码块
只是纯文本
```

## 行内代码

使用 `var x = 10;` 声明变量。

函数 `CalculateSum(a, b)` 返回两数之和。";

        SetTestContent(content, "代码块测试");
    }

    private void OnTestBlockQuotes(object sender, RoutedEventArgs e)
    {
        var content = @"# 引用块测试

## 单行引用

> 这是一段引用文本。

## 多行引用

> 这是第一行引用。
> 这是第二行引用。
> 这是第三行引用。

## 引用中包含样式

> 引用中可以包含 **加粗** 和 *斜体* 以及 `代码`。

## 连续引用段落

> 第一段引用内容。

> 第二段引用内容。

## 普通文本与引用混合

这是一段普通文本。

> 这是一段引用。

这是另一段普通文本。";

        SetTestContent(content, "引用块测试");
    }

    private void OnTestImages(object sender, RoutedEventArgs e)
    {
        var content = @"# 图片测试

## 本地图片（如果路径存在会显示）

![示例图片](./sample.png)

## 网络图片

![WPF Logo](https://upload.wikimedia.org/wikipedia/commons/thumb/c/cd/WPF_Logo.svg/200px-WPF_Logo.svg.png)

## 无效图片路径（应显示占位文本）

![无效图片](/nonexistent/path/image.png)

## 图片与普通文本混合

下面是一张图片：

![GitHub](https://github.githubassets.com/images/modules/logos_page/GitHub-Mark.png)

上面是一张图片。";

        SetTestContent(content, "图片测试");
    }

    private void OnTestFullDocument(object sender, RoutedEventArgs e)
    {
        var content = @"# BiSheng Markdown 渲染引擎 - 综合测试文档

欢迎使用 **BiSheng** 渲染引擎测试工具。本文档涵盖了所有支持的 Markdown 语法特性。

## 1. 标题层级

### 三级标题示例

#### 四级标题示例

##### 五级标题示例

###### 六级标题示例

## 2. 文本格式

这是一段普通文本。

**加粗文本** 用于强调重要内容。

*斜体文本* 用于表示术语或轻微强调。

`inline code` 用于表示代码片段。

混合使用：**加粗** 和 *斜体* 和 `代码` 在同一段落中。

## 3. 列表

### 无序列表

- 项目一
- 项目二
- 项目三
- 项目四

### 有序列表

1. 准备工作
2. 设计架构
3. 编写代码
4. 运行测试

### 混合列表

- 前端技术
  使用 WPF 框架
- 后端技术
  使用 .NET 8
- 数据库
  可选多种方案

## 4. 引用

> 好的软件设计应该是简洁的、可维护的、可扩展的。
> —— 某位智者

> 引用中可以包含 **加粗** 和 `代码`。

## 5. 代码块

### C# 示例

```csharp
public class MarkdownEditor
{
    private readonly TextDocument _document;
    private readonly MarkdownRenderer _renderer;

    public void Render()
    {
        foreach (var line in _document.Lines)
        {
            _renderer.RenderLine(line);
        }
    }
}
```

### XAML 示例

```xml
<Window xmlns:md=""clr-namespace:BiSheng.Editor"">
    <md:MarkdownEditorControl x:Name=""Editor"" />
</Window>
```

## 6. 链接

访问 [GitHub](https://github.com) 获取源代码。

查看 [文档](https://docs.microsoft.com) 了解更多。

## 7. 分割线

---

## 8. 图片

![示例图片](https://via.placeholder.com/300x150)

---

*文档结束。移动光标到不同行查看即时渲染效果。*";

        SetTestContent(content, "综合文档");
    }

    private void OnTestDarkTheme(object sender, RoutedEventArgs e)
    {
        TestEditor.Theme = MarkdownTheme.Dark;
        StatusText.Text = "已切换: 深色主题";
    }

    private void OnTestLightTheme(object sender, RoutedEventArgs e)
    {
        TestEditor.Theme = MarkdownTheme.Light;
        StatusText.Text = "已切换: 浅色主题";
    }

    private void OnTestEmptyDocument(object sender, RoutedEventArgs e)
    {
        SetTestContent("", "空文档");
    }

    private void OnTestLongDocument(object sender, RoutedEventArgs e)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# 超长文档性能测试\n");
        sb.AppendLine("本文档用于测试渲染引擎在处理大量内容时的性能表现。\n");

        for (int i = 1; i <= 200; i++)
        {
            sb.AppendLine($"## 第 {i} 节");
            sb.AppendLine();

            if (i % 5 == 0)
            {
                sb.AppendLine("```csharp");
                sb.AppendLine($"// Code block #{i}");
                sb.AppendLine($"public void Method{i}() {{ }}");
                sb.AppendLine("```");
            }
            else if (i % 3 == 0)
            {
                sb.AppendLine($"> 这是第 {i} 节的引用内容。");
            }
            else
            {
                sb.AppendLine($"这是第 {i} 节的普通文本。包含 **加粗** 和 *斜体* 以及 `代码`。");
            }

            if (i % 10 == 0)
            {
                sb.AppendLine();
                sb.AppendLine("---");
            }

            sb.AppendLine();
        }

        SetTestContent(sb.ToString(), "超长文档 (200节)");
    }

    private void OnTestSpecialChars(object sender, RoutedEventArgs e)
    {
        var content = @"# 特殊字符测试

## 中文内容

这是一段包含中文的文本。**加粗中文** 和 *斜体中文* 测试。

中文标题下的内容：

### 中文三级标题

## 数学符号

公式：E = mc²

特殊符号：© ® ™ ℃ °F ± × ÷ ≠ ≈ ≤ ≥

## Unicode 字符

表情符号：😀 😎 🎉 🚀 💡

箭头：→ ← ↑ ↓ ⇒ ⇐

## 转义字符

这是 \*不是斜体\* 的文本。

这是 \*\*不是加粗\*\* 的文本。

## 空行测试



上面有两个空行。

## 极长行

这是一行非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常长的文本。

## 混排

Hello World 你好世界 12345 **bold** *italic* `code`

---

*特殊字符测试完成。*";

        SetTestContent(content, "特殊字符测试");
    }

    // ===== 字体和行间距控制测试 =====

    private void OnFontSizeUp(object sender, RoutedEventArgs e)
    {
        double current = TestEditor.EditorFontSize;
        TestEditor.EditorFontSize = Math.Min(32, current + 2);
        StatusText.Text = $"字体大小: {TestEditor.EditorFontSize}px";
    }

    private void OnFontSizeDown(object sender, RoutedEventArgs e)
    {
        double current = TestEditor.EditorFontSize;
        TestEditor.EditorFontSize = Math.Max(10, current - 2);
        StatusText.Text = $"字体大小: {TestEditor.EditorFontSize}px";
    }

    private void OnLineSpacingUp(object sender, RoutedEventArgs e)
    {
        double current = TestEditor.LineSpacing;
        TestEditor.LineSpacing = Math.Min(3.0, current + 0.2);
        StatusText.Text = $"行间距: {TestEditor.LineSpacing:F1}x";
    }

    private void OnLineSpacingDown(object sender, RoutedEventArgs e)
    {
        double current = TestEditor.LineSpacing;
        TestEditor.LineSpacing = Math.Max(1.0, current - 0.2);
        StatusText.Text = $"行间距: {TestEditor.LineSpacing:F1}x";
    }
}
