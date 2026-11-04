using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using SharpCompress.Common;
using SharpCompress.Writers.Zip;
using SaveStateConverter;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace SaveStateConverter.Tests;

public sealed class ConverterTests
{
    private static readonly string[] RequiredEntries =
    [
        "PCSX2 Internal Structures.dat", "eeMemory.bin", "iopMemory.bin", "eeHwRegs.bin",
        "iopHwRegs.bin", "Scratchpad.bin", "vu0Memory.bin", "vu1Memory.bin",
        "vu0MicroMem.bin", "vu1MicroMem.bin", "SPU2.bin", "PAD.bin", "GS.bin"
    ];

    [Fact]
    public void InspectDetectsFormatAndNulTerminatedBuildTagInZstandardArchive()
    {
        using var fixture = new Fixture();
        var output = Invoke(out var exitCode, "inspect", fixture.SourcePath);

        Assert.Equal(0, exitCode);
        Assert.Contains("Source Format: 0x9A54", output);
        Assert.Contains("Stable Format Match: 2.4.0", output);
        Assert.Contains("Source Build Metadata: v2.5.258", output);
        Assert.Contains("Conversion to 2.8.0-2.8.2: available (direct, validated)", output);
        Assert.Contains("Conversion to 2.6.0-2.6.3: available (direct, validated)", output);
    }

    [Fact]
    public void ConversionOfUnconvertibleLegacyStateFailsWithoutOutputOrSourceChange()
    {
        // Synthetic components are not a real Internal Structures stream: the converter must
        // reject it (or be unavailable) without leaving any output behind.
        using var fixture = new Fixture();
        var before = Hash(fixture.SourcePath);
        var outputPath = Path.Combine(fixture.DirectoryPath, "converted.p2s");

        var output = Invoke(out var exitCode, "convert", fixture.SourcePath, "--target", "2.8.2", "--output", outputPath);

        Assert.Equal(1, exitCode);
        Assert.Contains("Status: FAILED", output);
        Assert.False(File.Exists(outputPath));
        Assert.Empty(Directory.GetFiles(fixture.DirectoryPath, "*.tmp.p2s", SearchOption.AllDirectories));
        Assert.Equal(before, Hash(fixture.SourcePath));
    }

    [Fact]
    public void ReverseConversionIsRefusedWithReason()
    {
        using var fixture = new Fixture(0x9A59, "v2.8.2");
        var outputPath = Path.Combine(fixture.DirectoryPath, "older.p2s");

        var output = Invoke(out var exitCode, "convert", fixture.SourcePath, "--target", "2.4.0", "--output", outputPath);

        Assert.Equal(3, exitCode);
        Assert.Contains("Status: UNSUPPORTED", output);
        Assert.Contains("decode-ahead", output);
        Assert.False(File.Exists(outputPath));
    }

    [Fact]
    public void SameFormatIsCompatibleAndWritesNothing()
    {
        using var fixture = new Fixture(0x9A59, "v2.8.2");
        var outputPath = Path.Combine(fixture.DirectoryPath, "copy.p2s");

        var output = Invoke(out var exitCode, "convert", fixture.SourcePath, "--target", "2.8.2", "--output", outputPath);

        Assert.Equal(0, exitCode);
        Assert.Contains("Status: ALREADYCOMPATIBLE", output);
        Assert.False(File.Exists(outputPath));
    }

    [Fact]
    public void CatalogFilesAreEmbeddedForTheSingleFileRelease()
    {
        // The single-file executable has nothing next to it and reads these embedded copies.
        foreach (var name in new[] { "stable-formats.json", "routes.json", "game-titles.tsv" })
            Assert.Equal(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, name)), ShippedData.ReadEmbedded(name));
        Assert.True(GameTitles.Count > 10_000);
    }

    [Fact]
    public void CatalogAdvertisesOnlyListedRoutes()
    {
        var catalog = Catalog.Load();
        Assert.Equal(RouteStatus.Validated, catalog.FindRoute(0x9A54, 0x9A59)!.Status);
        Assert.Equal(RouteStatus.Validated, catalog.FindRoute(0x9A55, 0x9A59)!.Status);
        Assert.NotNull(catalog.FindRoute(0x9A54, 0x9A55));
        Assert.Equal(ConversionStatus.Unsupported, ConversionEngine.Assess(0x9A55, 0x9A54, catalog).Status);
        Assert.Equal(ConversionStatus.Unsupported, ConversionEngine.Assess(0x9A59, 0x9A54, catalog).Status);
        Assert.Equal(ConversionStatus.Unsupported, ConversionEngine.Assess(0x9A56, 0x9A59, catalog).Status); // development-only format
        Assert.Equal(ConversionStatus.Compatible, ConversionEngine.Assess(0x9A59, 0x9A59, catalog).Status);
    }

    [Fact]
    public void CatalogResolvesEveryReleaseSharingAFormat()
    {
        var catalog = Catalog.Load();
        foreach (var version in new[] { "2.0.0", "2.0.1", "2.0.2", "2.0.3" })
            Assert.Equal((ushort)0x9A4F, catalog.FindRelease(version)!.Format);
        Assert.Equal("2.0.0-2.0.3", catalog.FindFormat(0x9A4F)!.Label);
        Assert.Equal((ushort)0x9A50, catalog.FindRelease("2.2.0")!.Format);
        Assert.Equal("2.2.0", catalog.FindFormat(0x9A50)!.Label);
        foreach (var version in new[] { "2.6.0", "2.6.1", "2.6.2", "2.6.3" })
            Assert.Equal((ushort)0x9A55, catalog.FindRelease(version)!.Format);
        foreach (var version in new[] { "2.8.0", "2.8.1", "2.8.2" })
            Assert.Equal((ushort)0x9A59, catalog.FindRelease(version)!.Format);
        Assert.Equal("2.6.0-2.6.3", catalog.FindFormat(0x9A55)!.Label);
        Assert.Equal("2.8.0-2.8.2", catalog.FindFormat(0x9A59)!.Label);
        Assert.Null(catalog.FindRelease("2.1.0"));
        Assert.Null(catalog.FindRelease("2.9.0")); // development series

        // Older formats convert forward to every newer stable release, never backward.
        foreach (var target in new ushort[] { 0x9A54, 0x9A55, 0x9A59 })
        {
            Assert.Equal(RouteStatus.Validated, catalog.FindRoute(0x9A4F, target)!.Status);
            Assert.Equal(RouteStatus.Validated, catalog.FindRoute(0x9A50, target)!.Status);
        }
        Assert.Equal(RouteStatus.Validated, catalog.FindRoute(0x9A4F, 0x9A50)!.Status);
        Assert.Equal(ConversionStatus.Unsupported, ConversionEngine.Assess(0x9A50, 0x9A4F, catalog).Status);
        Assert.Equal(ConversionStatus.Unsupported, ConversionEngine.Assess(0x9A54, 0x9A50, catalog).Status);
        Assert.Equal(ConversionStatus.Unsupported, ConversionEngine.Assess(0x9A59, 0x9A4F, catalog).Status);
    }

    [Fact]
    public void InspectNamesTheReleaseRangeOfAnOlderFormat()
    {
        using var fixture = new Fixture(0x9A4F, "v2.0.3");
        var output = Invoke(out var exitCode, "inspect", fixture.SourcePath);

        Assert.Equal(0, exitCode);
        Assert.Contains("Source Format: 0x9A4F", output);
        Assert.Contains("Stable Format Match: 2.0.0-2.0.3", output);
        foreach (var target in new[] { "2.2.0", "2.4.0", "2.6.0-2.6.3", "2.8.0-2.8.2" })
            Assert.Contains($"Conversion to {target}: available (direct, validated)", output);
    }

    [Fact]
    public void ConvertAcceptsAnyReleaseOfASharedFormatAsTarget()
    {
        using var fixture = new Fixture(0x9A4F, "v2.0.1");
        var outputPath = Path.Combine(fixture.DirectoryPath, "same.p2s");

        var output = Invoke(out var exitCode, "convert", fixture.SourcePath, "--target", "2.0.0", "--output", outputPath);

        Assert.Equal(0, exitCode);
        Assert.Contains("Status: ALREADYCOMPATIBLE", output);
        Assert.False(File.Exists(outputPath));
    }

    [Fact]
    public void BatchConversionReportsEveryFileAndNeverOverwrites()
    {
        using var legacy = new Fixture();
        using var native = new Fixture(0x9A59, "v2.8.2");
        var input = Path.Combine(legacy.DirectoryPath, "batch-in");
        var outputDirectory = Path.Combine(legacy.DirectoryPath, "batch-out");
        Directory.CreateDirectory(input);
        Directory.CreateDirectory(outputDirectory);
        File.Copy(legacy.SourcePath, Path.Combine(input, "SLUS-00000 (00000000).01.p2s"));
        File.Copy(native.SourcePath, Path.Combine(input, "SLUS-00000 (00000000).02.p2s"));
        File.Copy(legacy.SourcePath, Path.Combine(input, "SLUS-00000 (00000000).03.p2s"));
        File.WriteAllText(Path.Combine(outputDirectory, "SLUS-00000 (00000000).03.p2s"), "keep");

        var service = new ConversionService(Catalog.Load(), NativeConverter.Locate());
        var results = service.ConvertDirectory(input, "2.8.2", outputDirectory, recursive: false);

        Assert.Equal(3, results.Count);
        Assert.Equal(ConversionOutcome.Failed, results[0].Outcome);
        Assert.Equal(ConversionOutcome.AlreadyCompatible, results[1].Outcome);
        Assert.Equal(ConversionOutcome.Skipped, results[2].Outcome);
        Assert.Equal("keep", File.ReadAllText(Path.Combine(outputDirectory, "SLUS-00000 (00000000).03.p2s")));
        Assert.Single(Directory.GetFiles(outputDirectory));
    }

    [Fact]
    public void InPlaceConversionReplacesOriginalsOnlyAfterVerifiedBackup()
    {
        using var work = new Fixture();
        var states = work.Folder("sstates");
        var backupRoot = Path.Combine(work.DirectoryPath, "backups");
        var slot = Path.Combine(states, "SLUS-00000 (00000000).01.p2s");
        var slotBackup = slot + ".backup"; // PCSX2's copy of an overwritten slot is a state too
        var native = Path.Combine(states, "SLUS-00000 (00000000).02.p2s");
        WriteState(slot, 0x9A54, "v2.4.0", 0x11);
        WriteState(slotBackup, 0x9A54, "v2.4.0", 0x22);
        WriteState(native, 0x9A59, "v2.8.2", 0x33);
        var before = new[] { slot, slotBackup, native }.ToDictionary(x => x, Hash);

        var run = new ConversionService(Catalog.Load(), new FakeConverter()).ConvertDirectoryInPlace(states, "2.8.2", recursive: false, backupRoot);

        Assert.Equal([ConversionOutcome.Converted, ConversionOutcome.Converted, ConversionOutcome.AlreadyCompatible],
            run.Results.Select(x => x.Outcome).ToArray());
        Assert.Equal((ushort)0x9A59, SaveStateInspector.Inspect(slot).Format);
        Assert.Equal((ushort)0x9A59, SaveStateInspector.Inspect(slotBackup).Format);
        Assert.Equal(before[native], Hash(native));
        var set = BackupSet.Open(run.BackupFolder!);
        Assert.Equal(2, set.Manifest.Entries.Count);
        foreach (var entry in set.Manifest.Entries)
        {
            Assert.Equal(before[entry.Original], entry.OriginalSha256);
            Assert.Equal(before[entry.Original], Hash(Path.Combine(set.Folder!, entry.Backup)));
            Assert.Equal(Hash(entry.Original), entry.ConvertedSha256);
        }
        Assert.Equal(3, Directory.GetFiles(states).Length); // no temporary files left behind
    }

    [Fact]
    public void InPlaceConversionKeepsOriginalAndCreatesNoBackupWhenConversionFails()
    {
        using var work = new Fixture();
        var states = work.Folder("sstates");
        var backupRoot = Path.Combine(work.DirectoryPath, "backups");
        var slot = Path.Combine(states, "SLUS-00000 (00000000).01.p2s");
        WriteState(slot, 0x9A54, "v2.4.0");
        var before = Hash(slot);

        var run = new ConversionService(Catalog.Load(), new FakeConverter(succeed: false)).ConvertDirectoryInPlace(states, "2.8.2", false, backupRoot);

        Assert.Equal(ConversionOutcome.Failed, Assert.Single(run.Results).Outcome);
        Assert.Equal(before, Hash(slot));
        Assert.Null(run.BackupFolder);
        Assert.False(Directory.Exists(backupRoot));
        Assert.Single(Directory.GetFiles(states));
    }

    [Fact]
    public void InPlaceConversionKeepsReadOnlyOriginalAndDropsItsBackup()
    {
        using var work = new Fixture();
        var states = work.Folder("sstates");
        var backupRoot = Path.Combine(work.DirectoryPath, "backups");
        var slot = Path.Combine(states, "SLUS-00000 (00000000).01.p2s");
        WriteState(slot, 0x9A54, "v2.4.0");
        File.SetAttributes(slot, FileAttributes.ReadOnly);
        var before = Hash(slot);

        var run = new ConversionService(Catalog.Load(), new FakeConverter()).ConvertDirectoryInPlace(states, "2.8.2", false, backupRoot);

        var result = Assert.Single(run.Results);
        Assert.Equal(ConversionOutcome.Failed, result.Outcome);
        Assert.Contains("original left unchanged", result.Message);
        Assert.Equal(before, Hash(slot));
        Assert.Null(run.BackupFolder); // the set made for it is removed again
        Assert.Empty(BackupSet.List(backupRoot));
        Assert.Single(Directory.GetFiles(states));
    }

    [Fact]
    public void RestorePutsOriginalsBackButNeverOverwritesNewerSaves()
    {
        using var work = new Fixture();
        var states = work.Folder("sstates");
        var backupRoot = Path.Combine(work.DirectoryPath, "backups");
        var slotA = Path.Combine(states, "SLUS-00000 (00000000).01.p2s");
        var slotB = Path.Combine(states, "SLUS-00000 (00000000).02.p2s");
        WriteState(slotA, 0x9A54, "v2.4.0", 0x11);
        WriteState(slotB, 0x9A54, "v2.4.0", 0x22);
        var (hashA, hashB) = (Hash(slotA), Hash(slotB));
        var run = new ConversionService(Catalog.Load(), new FakeConverter()).ConvertDirectoryInPlace(states, "2.8.2", false, backupRoot);
        WriteState(slotB, 0x9A59, "v2.8.2", 0x44); // a newer save made after the conversion
        var newerB = Hash(slotB);
        var set = BackupSet.Open(run.BackupFolder!);
        RestoreResult For(IEnumerable<RestoreResult> results, string path) =>
            results.Single(x => string.Equals(x.Original, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase));

        var restored = set.Restore();

        Assert.Equal(RestoreOutcome.Restored, For(restored, slotA).Outcome);
        Assert.Equal(RestoreOutcome.Skipped, For(restored, slotB).Outcome);
        Assert.Equal(hashA, Hash(slotA));
        Assert.Equal(newerB, Hash(slotB));
        Assert.Equal(RestoreOutcome.AlreadyOriginal, For(set.Restore(), slotA).Outcome);

        // Both originals can still be recovered next to the newer save, never overwriting anything.
        var elsewhere = Path.Combine(work.DirectoryPath, "recovered");
        Assert.All(set.Restore(elsewhere), x => Assert.Equal(RestoreOutcome.Restored, x.Outcome));
        Assert.Equal(hashA, Hash(Path.Combine(elsewhere, Path.GetFileName(slotA))));
        Assert.Equal(hashB, Hash(Path.Combine(elsewhere, Path.GetFileName(slotB))));
        Assert.All(set.Restore(elsewhere), x => Assert.Equal(RestoreOutcome.Skipped, x.Outcome));
    }

    [Fact]
    public void InPlaceConversionRefusesBackupsInsideTheSaveStateFolder()
    {
        using var work = new Fixture();
        var states = work.Folder("sstates");
        var service = new ConversionService(Catalog.Load(), new FakeConverter());

        Assert.Throws<InvalidOperationException>(() => service.ConvertDirectoryInPlace(states, "2.8.2", false, states));
        Assert.Throws<InvalidOperationException>(() => service.ConvertDirectoryInPlace(states, "2.8.2", false, Path.Combine(states, "backups")));
    }

    [Fact]
    public void CommandLineNeedsExactlyOneDestinationAndRestoresBackupSets()
    {
        using var work = new Fixture();
        Invoke(out var neither, "convert", work.SourcePath, "--target", "2.8.2");
        Invoke(out var both, "convert", work.SourcePath, "--target", "2.8.2", "--output", Path.Combine(work.DirectoryPath, "x.p2s"), "--in-place");
        Assert.Equal(2, neither);
        Assert.Equal(2, both);

        var states = work.Folder("sstates");
        var backupRoot = Path.Combine(work.DirectoryPath, "backups");
        var slot = Path.Combine(states, "SLUS-00000 (00000000).01.p2s");
        WriteState(slot, 0x9A54, "v2.4.0");
        var original = Hash(slot);
        var run = new ConversionService(Catalog.Load(), new FakeConverter()).ConvertDirectoryInPlace(states, "2.8.2", false, backupRoot);

        var listing = Invoke(out var listed, "backups", "--backup-dir", backupRoot);
        Assert.Equal(0, listed);
        Assert.Contains(run.BackupFolder!, listing);
        Assert.Contains("1 save state(s)", listing);

        var restore = Invoke(out var restored, "restore", run.BackupFolder!);
        Assert.Equal(0, restored);
        Assert.Contains("RESTORED", restore);
        Assert.Equal(original, Hash(slot));
    }

    [Fact]
    public void GameTitlesAndSaveStateNamesAreRecognized()
    {
        Assert.Equal("Tony Hawk's Pro Skater 3", GameTitles.Find("SLUS-20013"));
        Assert.Equal("Monster Jam - Urban Assault", GameTitles.Find("slus-21807"));
        Assert.Null(GameTitles.Find("ABCD-00000"));
        Assert.Null(GameTitles.Find(null));

        var backup = SaveStateName.Parse(Path.Combine("C:", "sstates", "SLUS-20680 (FD7EEE96).10.p2s.backup"))!;
        Assert.Equal(("SLUS-20680", "FD7EEE96", "10", true), (backup.Serial, backup.Crc, backup.Slot, backup.IsBackup));
        Assert.Equal("Slot 10 (PCSX2 backup)", backup.SlotText);
        Assert.Equal("Slot 1", SaveStateName.Parse("SCUS-97199 (CE4933D0).01.p2s")!.SlotText);
        Assert.Equal("Resume", SaveStateName.Parse("SLUS-21807 (55bc9c93).resume.p2s")!.SlotText);
        Assert.Null(SaveStateName.Parse("notes.txt"));
        Assert.Null(SaveStateName.Parse("SLUS-20013.01.p2s"));
    }

    [Fact]
    public void SearchMatchesEveryWordInAnyFieldIgnoringCase()
    {
        string[] pacMan = ["Pac-Man World 2", "SLUS-20224  ·  Slot 1", "SLUS-20224 (E7EA3288).01.p2s"];
        string[] tonyHawk = ["Tony Hawk's Pro Skater 3", "SLUS-20013  ·  Slot 2", "SLUS-20013 (F77E2FB5).02.p2s"];

        Assert.True(TextSearch.Matches("", pacMan));
        Assert.True(TextSearch.Matches("   ", pacMan));
        Assert.True(TextSearch.Matches("pac man", pacMan));
        Assert.True(TextSearch.Matches("PAC-MAN", pacMan));
        Assert.True(TextSearch.Matches("20224", pacMan));
        Assert.True(TextSearch.Matches("tony slot 2", tonyHawk));
        Assert.True(TextSearch.Matches("f77e2fb5", tonyHawk));
        Assert.False(TextSearch.Matches("tony slot 1", tonyHawk)); // "1" is not a number in slot 2 or in SLUS-20013
        Assert.True(TextSearch.Matches("2022", pacMan));          // longer numbers: start of the disc ID
        Assert.False(TextSearch.Matches("0224", pacMan));
        Assert.True(TextSearch.Matches("bob", ["Nickelodeon SpongeBob SquarePants"]));
        Assert.False(TextSearch.Matches("pac hawk", pacMan));
        Assert.False(TextSearch.Matches("spongebob", pacMan));
    }

    [Fact]
    public void ScanIncludesSlotBackupsAndIgnoresTemporaryFiles()
    {
        using var work = new Fixture();
        var states = work.Folder("sstates");
        WriteState(Path.Combine(states, "SLUS-00000 (00000000).01.p2s"), 0x9A54, "v2.4.0");
        WriteState(Path.Combine(states, "SLUS-00000 (00000000).01.p2s.backup"), 0x9A54, "v2.4.0");
        WriteState(Path.Combine(states, ".SLUS-00000 (00000000).01.p2s.0123.tmp.p2s"), 0x9A59, "v2.8.2");

        var output = Invoke(out var exitCode, "scan", states);

        Assert.Equal(0, exitCode);
        Assert.Contains(".01.p2s.backup", output);
        Assert.DoesNotContain(".tmp.p2s", output);
        Assert.Equal(2, ConversionService.EnumerateStates(states, recursive: false).Count);
    }

    [NativeFixtureFact]
    public void GenuineLegacyStatesConvertInPlaceAndRestoreExactly()
    {
        using var work = new Fixture();
        var states = work.Folder("sstates");
        var originals = new Dictionary<string, string>();
        foreach (var (source, index) in NativeFixtureFactAttribute.Fixtures().Select((x, i) => (x, i)))
        {
            var copy = Path.Combine(states, $"{index:D2} {Path.GetFileName(source)}");
            File.Copy(source, copy);
            File.SetAttributes(copy, FileAttributes.Normal);
            originals[copy] = Hash(copy);
        }

        var run = new ConversionService(Catalog.Load(), NativeConverter.Locate())
            .ConvertDirectoryInPlace(states, "2.8.2", false, Path.Combine(work.DirectoryPath, "backups"));

        Assert.All(run.Results, x => Assert.True(x.Outcome is ConversionOutcome.Converted or ConversionOutcome.AlreadyCompatible, x.Message));
        foreach (var file in originals.Keys)
            Assert.Equal((ushort)0x9A59, SaveStateInspector.Inspect(file).Format);
        Assert.All(BackupSet.Open(run.BackupFolder!).Restore(), x => Assert.Equal(RestoreOutcome.Restored, x.Outcome));
        foreach (var (file, hash) in originals)
            Assert.Equal(hash, Hash(file));
    }

    [NativeFixtureFact]
    public void GenuineLegacyStatesConvertAlongEveryCatalogRoute()
    {
        var catalog = Catalog.Load();
        var service = new ConversionService(catalog, NativeConverter.Locate());
        var checkedRoutes = 0;
        foreach (var source in NativeFixtureFactAttribute.Fixtures())
        {
            var format = SaveStateInspector.Inspect(source).Format!.Value;
            var before = Hash(source);
            foreach (var route in catalog.Routes.Where(x => x.Source == format))
            {
                using var work = new Fixture();
                var target = catalog.Formats.First(x => x.Format == route.Target);
                var outputPath = Path.Combine(work.DirectoryPath, Path.GetFileName(source));

                var result = service.Convert(source, target.Version, outputPath);

                Assert.True(result.Outcome == ConversionOutcome.Converted, $"{Path.GetFileName(source)} -> {target.Version}: {result.Message}");
                var produced = SaveStateInspector.Inspect(outputPath, catalog);
                Assert.True(produced.IsValid, produced.Error);
                Assert.Equal(route.Target, produced.Format);
                Assert.Contains(result.Report, line => line.Contains("complete: consumed") && line.Contains("source bytes"));
                checkedRoutes++;
            }
            Assert.Equal(before, Hash(source));
        }
        Assert.True(checkedRoutes > 0, "no catalog route applies to the configured fixtures");
    }

    [Fact]
    public void ConversionNeverOverwritesExistingOutput()
    {
        using var fixture = new Fixture();
        var outputPath = Path.Combine(fixture.DirectoryPath, "existing.p2s");
        File.WriteAllText(outputPath, "leave this file alone");

        var output = Invoke(out var exitCode, "convert", fixture.SourcePath, "--target", "2.8.2", "--output", outputPath);

        Assert.Equal(1, exitCode);
        Assert.Contains("refusing to overwrite", output);
        Assert.Equal("leave this file alone", File.ReadAllText(outputPath));
    }

    [Fact]
    public void CorruptArchiveIsRejected()
    {
        using var fixture = new Fixture();
        var corruptPath = Path.Combine(fixture.DirectoryPath, "corrupt.p2s");
        File.WriteAllBytes(corruptPath, [0x50, 0x4B, 0x03, 0x04, 0x00, 0x01]);

        var output = Invoke(out var exitCode, "inspect", corruptPath);

        Assert.Equal(1, exitCode);
        Assert.Contains("Inspection: FAILED", output);
    }

    private static string Invoke(out int exitCode, params string[] args)
    {
        var original = Console.Out;
        var originalError = Console.Error;
        using var writer = new StringWriter();
        try
        {
            Console.SetOut(writer);
            Console.SetError(writer);
            exitCode = Program.Main(args);
            return writer.ToString();
        }
        finally
        {
            Console.SetOut(original);
            Console.SetError(originalError);
        }
    }

    private static byte[] CreateVersionEntry(ushort format, string build)
    {
        var bytes = new byte[36];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)format << 16);
        Encoding.UTF8.GetBytes(build).CopyTo(bytes, 4);
        // Intentionally leave zero padding after the terminating NUL.
        return bytes;
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    /// <summary>Writes a structurally valid archive with placeholder components (not a convertible state).</summary>
    private static void WriteState(string path, ushort format, string build, byte fill = 0x11)
    {
        if (File.Exists(path)) File.Delete(path);
        using var output = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        using var zip = new ZipWriter(output, new ZipWriterOptions(CompressionType.ZStandard) { LeaveStreamOpen = true });
        WriteEntry(zip, "PCSX2 Savestate Version.id", CreateVersionEntry(format, build), CompressionType.ZStandard);
        foreach (var entry in RequiredEntries)
            WriteEntry(zip, entry, [fill, (byte)(fill + 0x11), (byte)(fill + 0x22), (byte)(fill + 0x33)], CompressionType.ZStandard);
        WriteEntry(zip, "Screenshot.png", [0x89, 0x50, 0x4E, 0x47], CompressionType.None);
    }

    private static void WriteEntry(ZipWriter zip, string name, byte[] bytes, CompressionType compression)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        zip.Write(name, stream, new ZipWriterEntryOptions
        {
            CompressionType = compression,
            CompressionLevel = compression == CompressionType.ZStandard ? 3 : 0
        });
    }

    /// <summary>Stands in for the native converter: writes a placeholder archive in the target format.</summary>
    private sealed class FakeConverter(bool succeed = true) : IStateConverter
    {
        public NativeResult Convert(string input, string output, ushort targetFormat)
        {
            if (!succeed)
                return new(false, 1, null, null, null, null, "synthetic conversion failure", ["synthetic report"]);
            WriteState(output, targetFormat, "fake-converter", 0x5A);
            return new(true, 0, null, null, targetFormat.ToString("X4"), output, null, ["complete: consumed 4 of 4 source bytes (synthetic)"]);
        }
    }

    private sealed class Fixture : IDisposable
    {
        public string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), $"saveconvert-test-{Guid.NewGuid():N}");
        public string SourcePath { get; }

        public Fixture(ushort format = 0x9A54, string build = "v2.5.258")
        {
            Directory.CreateDirectory(DirectoryPath);
            SourcePath = Path.Combine(DirectoryPath, "source.p2s");
            WriteState(SourcePath, format, build);
        }

        public string Folder(string name) => Directory.CreateDirectory(Path.Combine(DirectoryPath, name)).FullName;

        public void Dispose()
        {
            if (!Directory.Exists(DirectoryPath)) return;
            foreach (var file in Directory.EnumerateFiles(DirectoryPath, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(DirectoryPath, recursive: true);
        }
    }
}

/// <summary>
/// Runs only when the native converter is available and SAVECONVERT_TEST_FIXTURE lists real save
/// states from official releases (';'-separated).
/// </summary>
public sealed class NativeFixtureFactAttribute : FactAttribute
{
    public const string FixtureVariable = "SAVECONVERT_TEST_FIXTURE";

    public static string[] Fixtures() =>
        (Environment.GetEnvironmentVariable(FixtureVariable) ?? "")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public NativeFixtureFactAttribute()
    {
        if (NativeConverter.Locate() is null)
            Skip = "Native converter not found (set SAVECONVERT_NATIVE).";
        else if (Fixtures() is not { Length: > 0 } paths || !paths.All(File.Exists))
            Skip = $"Set {FixtureVariable} to genuine legacy save states (';'-separated).";
    }
}
