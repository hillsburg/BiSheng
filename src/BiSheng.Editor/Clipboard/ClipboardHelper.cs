using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BiSheng.Editor.Clipboard
{
    /// <summary>
    /// 剪贴板辅助工具：支持文本和图像的复制/粘贴
    /// </summary>
    public static class ClipboardHelper
    {
        /// <summary>
        /// 复制文本到剪贴板（同时提供纯文本和 HTML 格式）
        /// </summary>
        public static void CopyAsRichText(string markdownText, string? htmlText = null)
        {
            var dataObject = new DataObject();

            // 纯文本（Markdown 原文）
            dataObject.SetText(markdownText, TextDataFormat.UnicodeText);

            // HTML 格式（如果有）
            if (!string.IsNullOrEmpty(htmlText))
            {
                dataObject.SetText(htmlText, TextDataFormat.Html);
            }

            System.Windows.Clipboard.SetDataObject(dataObject, true);
        }

        /// <summary>
        /// 复制图片到剪贴板
        /// </summary>
        public static void CopyImage(string imagePath)
        {
            if (!File.Exists(imagePath)) return;

            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(imagePath, UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();

                var dataObject = new DataObject();
                dataObject.SetImage(bitmap);
                dataObject.SetText($"![image]({imagePath})", TextDataFormat.UnicodeText);
                System.Windows.Clipboard.SetDataObject(dataObject, true);
            }
            catch
            {
                // 图片加载失败，仅复制文本
                System.Windows.Clipboard.SetText($"![image]({imagePath})");
            }
        }

        /// <summary>
        /// 检查剪贴板中是否包含图片
        /// </summary>
        public static bool HasImageInClipboard()
        {
            return System.Windows.Clipboard.ContainsImage()
                || System.Windows.Clipboard.ContainsFileDropList();
        }

        /// <summary>
        /// 从剪贴板获取图片并保存到指定目录，返回文件路径
        /// 
        /// 绕过 WPF 的 Clipboard.GetImage()（对 DIB 格式 alpha 通道处理有 bug，导致图片全白），
        /// 直接读取剪贴板原始数据：优先 PNG 格式 → DIB 手动解析 → GetImage() 兑底
        /// </summary>
        /// <param name="saveDirectory">保存目录</param>
        /// <param name="fileName">可选的文件名（不含路径），如 "{uuid}.png"。为 null 则自动生成时间戳文件名</param>
        public static string? GetImageFromClipboard(string saveDirectory, string? fileName = null)
        {
            if (!Directory.Exists(saveDirectory))
                Directory.CreateDirectory(saveDirectory);

            fileName ??= $"image_{DateTime.Now:yyyyMMdd_HHmmss}.png";
            string filePath = Path.Combine(saveDirectory, fileName);

            try
            {
                var dataObj = System.Windows.Clipboard.GetDataObject();
                if (dataObj == null) return null;

                // 优先级 1：PNG 格式（无损，浏览器/截屏工具常用）
                if (dataObj.GetDataPresent("PNG", false))
                {
                    var pngStream = dataObj.GetData("PNG", false) as MemoryStream;
                    if (pngStream != null && pngStream.Length > 0)
                    {
                        pngStream.Position = 0;
                        using var fs = new FileStream(filePath, FileMode.Create);
                        pngStream.CopyTo(fs);
                        return filePath;
                    }
                }

                // 优先级 2：DIB 格式（Windows 截图 / 从应用程序复制的图片）
                // 手动解析 BITMAPINFOHEADER + 像素数据，绕过 WPF 的 alpha 通道 bug
                if (dataObj.GetDataPresent(DataFormats.Dib, false))
                {
                    var dibStream = dataObj.GetData(DataFormats.Dib, false) as MemoryStream;
                    if (dibStream != null && dibStream.Length > 0)
                    {
                        dibStream.Position = 0;
                        var bitmap = ConvertDibToBitmapSource(dibStream);
                        if (bitmap != null)
                        {
                            SaveBitmapToFile(bitmap, filePath);
                            return filePath;
                        }
                    }
                }

                // 优先级 3：Clipboard.GetImage()（其他图片格式）
                if (System.Windows.Clipboard.ContainsImage())
                {
                    var bitmapSource = System.Windows.Clipboard.GetImage();
                    if (bitmapSource != null && bitmapSource.PixelWidth > 0 && bitmapSource.PixelHeight > 0)
                    {
                        SaveBitmapToFile(bitmapSource, filePath);
                        return filePath;
                    }
                }

                // 优先级 4：文件列表（从文件管理器复制的图片）
                if (System.Windows.Clipboard.ContainsFileDropList())
                {
                    var files = System.Windows.Clipboard.GetFileDropList();
                    foreach (string file in files)
                    {
                        var ext = Path.GetExtension(file).ToLowerInvariant();
                        if (ext is ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp")
                        {
                            File.Copy(file, filePath, true);
                            return filePath;
                        }
                    }
                }
            }
            catch
            {
                // 获取图片失败
            }

            return null;
        }

        /// <summary>
        /// 将剪贴板 DIB 格式的 MemoryStream 转换为 BitmapSource
        /// 
        /// DIB 结构：BITMAPINFOHEADER (40 bytes) + 可选颜色表 + 像素数据（底部向上，BGRA/BGR）
        /// 
        /// WPF 的 Clipboard.GetImage() 对 32bpp DIB 的 alpha 通道处理有 bug，
        /// 把 premultiplied alpha 当作 opaque 处理，导致透明像素变成全白。
        /// 此方法手动解析 DIB，正确处理 alpha 通道。
        /// </summary>
        private static BitmapSource? ConvertDibToBitmapSource(MemoryStream dibStream)
        {
            using var reader = new BinaryReader(dibStream);

            // 读取 BITMAPINFOHEADER
            int headerSize = reader.ReadInt32();
            if (headerSize < 40) return null;

            int width = reader.ReadInt32();
            int height = reader.ReadInt32(); // 正数=底部向上, 负数=顶部向下
            reader.ReadInt16(); // planes
            int bitsPerPixel = reader.ReadInt16();
            int compression = reader.ReadInt32(); // 0=BI_RGB, 3=BI_BITFIELDS

            if (compression != 0 && compression != 3) return null;
            if (width <= 0 || height == 0) return null;
            if (bitsPerPixel != 24 && bitsPerPixel != 32) return null;

            // 跳过其余头部字段（已读 20 字节）
            int remainingHeader = headerSize - 20;
            if (remainingHeader > 0) reader.ReadBytes(remainingHeader);

            // 跳过颜色表（仅 8bpp 及以下）
            if (bitsPerPixel <= 8)
            {
                int colorCount = 1 << bitsPerPixel;
                reader.ReadBytes(colorCount * 4);
            }

            bool topDown = height < 0;
            int absHeight = Math.Abs(height);

            // 计算每行字节数（4 字节对齐）
            int stride = ((width * bitsPerPixel + 31) / 32) * 4;
            int totalBytes = stride * absHeight;

            // 确保流中有足够的像素数据
            long available = dibStream.Length - dibStream.Position;
            if (available < totalBytes) return null;

            byte[] pixelData = reader.ReadBytes(totalBytes);

            // 底部向上的 DIB：手动翻转行顺序
            // BitmapSource.Create 不支持负 stride，必须预处理
            if (!topDown)
            {
                byte[] flipped = new byte[totalBytes];
                for (int row = 0; row < absHeight; row++)
                {
                    int srcOffset = (absHeight - 1 - row) * stride;
                    int dstOffset = row * stride;
                    Array.Copy(pixelData, srcOffset, flipped, dstOffset, stride);
                }
                pixelData = flipped;
            }

            // 确定 PixelFormat
            var format = bitsPerPixel == 32 ? PixelFormats.Bgra32 : PixelFormats.Bgr24;

            return BitmapSource.Create(
                width, absHeight,
                96, 96,
                format,
                null,
                pixelData,
                stride);
        }

        private static void SaveBitmapToFile(BitmapSource source, string filePath)
        {
            // 直接编码 BitmapSource 为 PNG，避免 RenderTargetBitmap 中间层导致空白
            // 先转换为 WriteableBitmap 确保像素数据已完全解码
            var writeable = new WriteableBitmap(source);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(writeable));
            using var stream = new FileStream(filePath, FileMode.Create);
            encoder.Save(stream);
        }

        /// <summary>
        /// 压缩图片文件：通过降低分辨率重编码，使文件大小不超过目标大小
        /// 返回压缩后的文件路径（覆盖原文件）
        /// </summary>
        public static string CompressImage(string filePath, long maxBytes)
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(filePath, UriKind.Absolute);
                bitmap.EndInit();

                // 逐步降低分辨率直到文件大小达标
                int targetWidth = bitmap.PixelWidth;
                int targetHeight = bitmap.PixelHeight;
                byte[]? compressedBytes = null;

                for (double scale = 0.8; scale >= 0.1; scale -= 0.1)
                {
                    targetWidth = (int)(bitmap.PixelWidth * scale);
                    targetHeight = (int)(bitmap.PixelHeight * scale);

                    var rtb = new RenderTargetBitmap(
                        targetWidth, targetHeight,
                        bitmap.DpiX, bitmap.DpiY,
                        PixelFormats.Pbgra32);

                    var drawingVisual = new DrawingVisual();
                    using (var context = drawingVisual.RenderOpen())
                    {
                        context.DrawImage(bitmap, new Rect(0, 0, targetWidth, targetHeight));
                    }
                    rtb.Render(drawingVisual);

                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(rtb));
                    using var ms = new MemoryStream();
                    encoder.Save(ms);
                    compressedBytes = ms.ToArray();

                    if (compressedBytes.Length <= maxBytes)
                        break;
                }

                if (compressedBytes != null && compressedBytes.Length <= maxBytes)
                {
                    File.WriteAllBytes(filePath, compressedBytes);
                }
            }
            catch
            {
                // 压缩失败，保留原文件
            }

            return filePath;
        }

        /// <summary>
        /// 将 Markdown 内容转换为简单的 HTML（用于复制）
        /// </summary>
        public static string MarkdownToSimpleHtml(string markdown)
        {
            // 简单的 Markdown 到 HTML 转换（用于剪贴板）
            var html = System.Net.WebUtility.HtmlEncode(markdown);

            // 标题
            html = Regex.Replace(html,
                @"^######\s+(.+)$", "<h6>$1</h6>", RegexOptions.Multiline);
            html = Regex.Replace(html,
                @"^#####\s+(.+)$", "<h5>$1</h5>", RegexOptions.Multiline);
            html = Regex.Replace(html,
                @"^####\s+(.+)$", "<h4>$1</h4>", RegexOptions.Multiline);
            html = Regex.Replace(html,
                @"^###\s+(.+)$", "<h3>$1</h3>", RegexOptions.Multiline);
            html = Regex.Replace(html,
                @"^##\s+(.+)$", "<h2>$1</h2>", RegexOptions.Multiline);
            html = Regex.Replace(html,
                @"^#\s+(.+)$", "<h1>$1</h1>", RegexOptions.Multiline);

            // 加粗和斜体
            html = Regex.Replace(html,
                @"\*\*(.+?)\*\*", "<strong>$1</strong>");
            html = Regex.Replace(html,
                @"\*(.+?)\*", "<em>$1</em>");

            // 行内代码
            html = Regex.Replace(html,
                @"`([^`]+)`", "<code>$1</code>");

            // 换行
            html = html.Replace("\n", "<br/>\n");

            return $"<div>{html}</div>";
        }
    }
}
