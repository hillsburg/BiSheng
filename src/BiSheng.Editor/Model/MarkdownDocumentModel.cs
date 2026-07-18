using System.Collections.Generic;
using BiSheng.Editor.Parsing;
using ICSharpCode.AvalonEdit.Document;

namespace BiSheng.Editor.Model
{
    /// <summary>
    /// 文档模型：维护每一行的状态信息
    /// </summary>
    public class MarkdownDocumentModel
    {
        private readonly TextDocument _document;
        private readonly Dictionary<int, LineState> _lineStates = new();
        private readonly MarkdownParser _parser = new();

        public MarkdownDocumentModel(TextDocument document)
        {
            _document = document;
            _document.Changed += OnDocumentChanged;
            RebuildAll();
        }

        public event Action? DocumentModelUpdated;

        /// <summary>
        /// 获取指定行（0-based）的状态
        /// </summary>
        public LineState GetLineState(int lineNumber)
        {
            if (_lineStates.TryGetValue(lineNumber, out var state))
                return state;

            var newState = new LineState { LineNumber = lineNumber };
            _lineStates[lineNumber] = newState;
            return newState;
        }

        /// <summary>
        /// 判断某行是否处于代码块内部
        /// </summary>
        public bool IsInsideCodeBlock(int lineNumber)
        {
            var state = GetLineState(lineNumber);
            return state.Type == LineType.CodeBlockContent
                || state.Type == LineType.CodeBlock
                || state.BlockStartLine >= 0;
        }

        /// <summary>
        /// 重建所有行状态
        /// </summary>
        public void RebuildAll()
        {
            _lineStates.Clear();

            int lineCount = _document.LineCount;
            bool inCodeBlock = false;
            int codeBlockStart = -1;

            for (int i = 0; i < lineCount; i++)
            {
                var line = _document.GetLineByNumber(i + 1);
                var text = _document.GetText(line.Offset, line.Length);
                var state = new LineState
                {
                    LineNumber = i,
                    RawText = text
                };

                // 检查代码块开闭
                if (text.TrimStart().StartsWith("```"))
                {
                    if (!inCodeBlock)
                    {
                        inCodeBlock = true;
                        codeBlockStart = i;
                        state.Type = LineType.CodeBlock;
                    }
                    else
                    {
                        inCodeBlock = false;
                        state.Type = LineType.CodeBlock;
                        state.BlockStartLine = codeBlockStart;
                        // 回填起始行的 BlockEndLine
                        if (_lineStates.TryGetValue(codeBlockStart, out var startState))
                            startState.BlockEndLine = i;
                        codeBlockStart = -1;
                    }
                }
                else if (inCodeBlock)
                {
                    state.Type = LineType.CodeBlockContent;
                    state.BlockStartLine = codeBlockStart;
                }
                else
                {
                    state.Type = LineMarkdownAnalyzer.AnalyzeLineType(text);
                    state.InlineMarkers = LineMarkdownAnalyzer.AnalyzeInlineElements(text);
                }

                _lineStates[i] = state;
            }

            DocumentModelUpdated?.Invoke();
        }

        private void OnDocumentChanged(object? sender, DocumentChangeEventArgs e)
        {
            // 简单策略：变更后重建所有状态
            // 对于大文档，后续可优化为增量更新
            RebuildAll();
        }
    }
}
