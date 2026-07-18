using PuppeteerSharp;

static string? TryResolveSystemChromiumPath()
{
    var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
    var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
    string[] candidates =
    [
        Path.Combine(programFilesX86, @"Microsoft\Edge\Application\msedge.exe"),
        Path.Combine(programFiles, @"Microsoft\Edge\Application\msedge.exe"),
        Path.Combine(programFiles, @"Google\Chrome\Application\chrome.exe"),
        Path.Combine(programFilesX86, @"Google\Chrome\Application\chrome.exe"),
    ];
    foreach (var c in candidates)
        if (File.Exists(c)) return c;
    return null;
}

var exePath = TryResolveSystemChromiumPath();
if (exePath == null) { Console.WriteLine("No system browser"); return 1; }
Console.WriteLine("Using: " + exePath);

await using var browser = await Puppeteer.LaunchAsync(new LaunchOptions
{
    Headless = true,
    ExecutablePath = exePath,
    Args = new[] { "--no-sandbox", "--disable-dev-shm-usage", "--disable-gpu" },
});
await using var page = await browser.NewPageAsync();
await page.SetContentAsync("<html><body><h1>Test PDF</h1><p>中文测试</p></body></html>", new NavigationOptions
{
    WaitUntil = new[] { WaitUntilNavigation.Load },
    Timeout = 60000,
});
var bytes = await page.PdfDataAsync(new PdfOptions { Format = PuppeteerSharp.Media.PaperFormat.A4 });
Console.WriteLine("PDF bytes: " + bytes.Length);
var outPath = Path.Combine(Path.GetTempPath(), "bisheng-edge-test.pdf");
await File.WriteAllBytesAsync(outPath, bytes);
Console.WriteLine("Wrote: " + outPath);
return 0;
