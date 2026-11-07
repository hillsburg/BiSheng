using System.Collections.Generic;
using BiSheng.Editor.Parsing;
using ICSharpCode.AvalonEdit.Document;

namespace BiSheng.Editor.Model
{
    /// <summary>
    /// 文档模型：维护每一行的状态信息
    ///
    /// 增量更新策略（避免长笔记每次按键整篇跑正则）：
    /// - 每行的正则分析结果只取决于该行文本，编辑后只重新分析被改动的行；
    /// - 代码块配对是全局的（一个 ``` 会改变其后所有行的类型），但只依赖缓存的栅栏标记，
    ///   每次变更做一遍不跑正则的线性扫描即可，成本远低于整篇重新分析；
    /// - 扫描时发现编辑区之外的行类型变化（如新增/删除栅栏），通过 <see cref="LineTypesChanged"/>
    ///   通知视图重绘这些行——AvalonEdit 自身只会重绘被编辑的行。
    /// </summary>
    public class MarkdownDocumentModel
    {
        private readonly TextDocument _document;

        /// <summary>按 0-based 行号索引的行状态，始终与文档行数一致</summary>
        private readonly List<LineState> _lines = new();

        public MarkdownDocumentModel(TextDocument document)
        {
            _document = document;
            _document.Changed += OnDocumentChanged;
            RebuildAll();
        }

        public event Action? DocumentModelUpdated;

        /// <summary>
        /// 编辑区之外有行的类型发生变化（0-based 首行、末行，闭区间），视图需要重绘这些行
        /// </summary>
        public event Action<int, int>? LineTypesChanged;

        /// <summary>
        /// 获取指定行（0-based）的状态；越界时返回默认状态（不写入模型）
        /// </summary>
        public LineState GetLineState(int lineNumber)
        {
            if (lineNumber >= 0 && lineNumber < _lines.Count)
            {
                return _lines[lineNumber];
            }

            return new LineState { LineNumber = lineNumber };
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
            _lines.Clear();

            int lineCount = _document.LineCount;
            for (int i = 0; i < lineCount; i++)
            {
                _lines.Add(CreateLineState(i));
            }

            // 全量重建时视图本身会整体刷新，无需上报类型变化
            ResolveBlocks(editedFirst: 0, editedLast: lineCount - 1);
            DocumentModelUpdated?.Invoke();
        }

        private void OnDocumentChanged(object? sender, DocumentChangeEventArgs e)
        {
            if (!TryApplyIncremental(e, out int editedFirst, out int editedLast))
            {
                RebuildAll();
                return;
            }

            var (changedFirst, changedLast) = ResolveBlocks(editedFirst, editedLast);
            DocumentModelUpdated?.Invoke();

            if (changedFirst >= 0)
            {
                LineTypesChanged?.Invoke(changedFirst, changedLast);
            }
        }

        /// <summary>
        /// 只替换受本次变更影响的行。
        /// 变更之前的行（前缀）和之后的行（尾部）文本都不变，因此：
        /// 新受影响行数 − 旧受影响行数 = 新总行数 − 旧总行数。
        /// 推算结果与实际行数对不上时返回 false，由调用方回退全量重建。
        /// </summary>
        private bool TryApplyIncremental(DocumentChangeEventArgs e, out int editedFirst, out int editedLast)
        {
            editedFirst = editedLast = -1;
            int oldTotal = _lines.Count;
            int newTotal = _document.LineCount;
            if (oldTotal == 0)
            {
                return false;
            }

            int changeEnd = Math.Min(e.Offset + e.InsertionLength, _document.TextLength);
            int first = _document.GetLineByOffset(Math.Min(e.Offset, _document.TextLength)).LineNumber - 1;
            int last = _document.GetLineByOffset(changeEnd).LineNumber - 1;

            int newAffected = last - first + 1;
            int oldAffected = newAffected - (newTotal - oldTotal);
            if (oldAffected < 1 || first + oldAffected > oldTotal)
            {
                return false;
            }

            var replacement = new List<LineState>(newAffected);
            for (int i = first; i <= last; i++)
            {
                replacement.Add(CreateLineState(i));
            }

            _lines.RemoveRange(first, oldAffected);
            _lines.InsertRange(first, replacement);

            if (_lines.Count != newTotal)
            {
                return false;
            }

            editedFirst = first;
            editedLast = last;
            return true;
        }

        /// <summary>为指定行建立状态：只做栅栏判断，正则分析延后到确认不在代码块内时</summary>
        private LineState CreateLineState(int lineIndex)
        {
            var line = _document.GetLineByNumber(lineIndex + 1);
            var text = _document.GetText(line.Offset, line.Length);
            return new LineState
            {
                LineNumber = lineIndex,
                RawText = text,
                IsFence = text.TrimStart().StartsWith("```", StringComparison.Ordinal)
            };
        }

        /// <summary>
        /// 线性扫描配对代码块并确定每行最终类型。
        /// 返回编辑区之外类型发生变化的行范围（闭区间）；无变化时返回 (-1, -1)。
        /// </summary>
        private (int First, int Last) ResolveBlocks(int editedFirst, int editedLast)
        {
            int changedFirst = -1;
            int changedLast = -1;
            bool inCodeBlock = false;
            int codeBlockStart = -1;

            for (int i = 0; i < _lines.Count; i++)
            {
                var state = _lines[i];
                var previousType = state.Type;

                state.LineNumber = i;
                state.BlockStartLine = -1;
                state.BlockEndLine = -1;

                if (state.IsFence)
                {
                    state.Type = LineType.CodeBlock;
                    ClearMarkers(state);
                    if (!inCodeBlock)
                    {
                        inCodeBlock = true;
                        codeBlockStart = i;
                    }
                    else
                    {
                        inCodeBlock = false;
                        state.BlockStartLine = codeBlockStart;
                        // 回填起始行的 BlockEndLine
                        _lines[codeBlockStart].BlockEndLine = i;
                        codeBlockStart = -1;
                    }
                }
                else if (inCodeBlock)
                {
                    state.Type = LineType.CodeBlockContent;
                    state.BlockStartLine = codeBlockStart;
                    ClearMarkers(state);
                }
                else
                {
                    EnsureAnalyzed(state);
                    state.Type = state.AnalyzedType;
                    state.InlineMarkers = state.AnalyzedMarkers!;
                }

                bool outsideEdit = i < editedFirst || i > editedLast;
                if (outsideEdit && state.Type != previousType)
                {
                    if (changedFirst < 0)
                    {
                        changedFirst = i;
                    }

                    changedLast = i;
                }
            }

            return (changedFirst, changedLast);
        }

        /// <summary>按普通行做一次正则分析并缓存（行文本不变就不再重复分析）</summary>
        private static void EnsureAnalyzed(LineState state)
        {
            if (state.IsAnalyzed)
            {
                return;
            }

            state.AnalyzedType = LineMarkdownAnalyzer.AnalyzeLineType(state.RawText);
            state.AnalyzedMarkers = LineMarkdownAnalyzer.AnalyzeInlineElements(state.RawText);
            state.IsAnalyzed = true;
        }

        /// <summary>代码块内的行不应用行内样式；复用已有空列表，避免每次扫描都分配</summary>
        private static void ClearMarkers(LineState state)
        {
            if (state.InlineMarkers.Count != 0 || ReferenceEquals(state.InlineMarkers, state.AnalyzedMarkers))
            {
                state.InlineMarkers = new List<InlineMarker>();
            }
        }
    }
}
