using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;
using Xunit;

namespace EndHud.Vision.Tests;

/// <summary>Collected garbage while Windows operations are awaited: alone, so the collections it forces slow no other test.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class WindowsAwaitCollection
{
    public const string Name = "Windows operations under garbage collection";
}

/// <summary>
/// Every reader awaits Windows operations (opening a file, decoding a picture, reading text). The
/// Windows projection that .NET SDK 8.0.100 brings (C#/WinRT 2.0.4) let the garbage collector free an
/// operation while its result was being taken, in optimized (Release) code: the await answered null,
/// threw "Value does not fall within the expected range", or crashed the process with an access
/// violation. Full Release test runs showed it as NullReferenceExceptions in the OCR and image readers
/// when several tests ran at once (more garbage, more collections); never alone, and not seen in Debug.
/// The projection is pinned in Directory.Build.props; this test notices if that is undone. Measured: on
/// the old projection a Release run failed 3 of 3 times within 15 s (5 s was not enough), the pinned
/// one passed 3 of 3. In a Debug build it passed on either (unoptimized code keeps the operation alive longer).
/// </summary>
[Collection(WindowsAwaitCollection.Name)]
public class WindowsAwaitTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Fact]
    public async Task Awaited_operations_answer_while_the_garbage_collector_runs()
    {
        string picture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "inn", "chef-picker.jpg");
        var stop = DateTime.UtcNow.AddSeconds(15);
        int files = 0, readings = 0;
        var blank = new byte[200 * 60 * 4];
        Array.Fill(blank, (byte)255);

        var work = new List<Task>();
        for (int i = 0; i < 4; i++)
            work.Add(Task.Run(async () =>
            {
                while (DateTime.UtcNow < stop)
                {
                    Assert.NotNull(await StorageFile.GetFileFromPathAsync(picture));
                    Interlocked.Increment(ref files);
                }
            }));
        for (int i = 0; i < 2; i++)
            work.Add(Task.Run(async () =>
            {
                // One engine per reader: an engine reads one picture at a time.
                var engine = OcrEngine.TryCreateFromLanguage(new Language("en-US"))
                             ?? OcrEngine.TryCreateFromUserProfileLanguages()
                             ?? throw new InvalidOperationException("No Windows OCR language is installed.");
                while (DateTime.UtcNow < stop)
                {
                    using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(blank.AsBuffer(), BitmapPixelFormat.Bgra8, 200, 60, BitmapAlphaMode.Premultiplied);
                    Assert.NotNull(await engine.RecognizeAsync(bitmap));
                    Interlocked.Increment(ref readings);
                }
            }));
        work.Add(Task.Run(() =>
        {
            while (DateTime.UtcNow < stop)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }));
        await Task.WhenAll(work);
        output.WriteLine($"files={files} readings={readings} gen2={GC.CollectionCount(2)}");

        // Enough operations for the old projection to have failed: it never lasted 15 s of this.
        Assert.True(files > 500, $"only {files} files opened");
        Assert.True(readings > 500, $"only {readings} pictures read");
    }
}
