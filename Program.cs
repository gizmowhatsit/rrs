namespace rrs;
// Rapid ReSync - jvincent 2025

internal abstract class Program
{
    private const string TRANSFER_STUB_EXTENSION = ".transfer";
    
    private static void Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.WriteLine("Usage: rrs <source-folder> <destination-folder>");
            Console.WriteLine(@"Example: rrs C:\Source C:\Backup");
            return;
        }

        string sourcePath = args[0];
        string destinationPath = args[1];

        try
        {
            if (!Directory.Exists(sourcePath))
            {
                Console.WriteLine($"Error: Source folder '{sourcePath}' does not exist.");
                return;
            }

            Console.WriteLine($"Starting sync from '{sourcePath}' to '{destinationPath}'");
            Console.WriteLine(new string('-', 60));

            // Detect any orphaned transfer stubs from previous interrupted runs
            var stubStats = DetectOrphanedStubs(destinationPath);
            if (stubStats.OrphanedStubsFound > 0)
            {
                Console.WriteLine($"Found {stubStats.OrphanedStubsFound} incomplete transfers from previous runs - will resume");
                Console.WriteLine(new string('-', 60));
            }

            var stats = SyncFolders(sourcePath, destinationPath);

            Console.WriteLine(new string('-', 60));
            Console.WriteLine("Sync completed successfully!");
            Console.WriteLine($"Files copied: {stats.FilesCopied}");
            Console.WriteLine($"Files skipped (already up-to-date): {stats.FilesSkipped}");
            Console.WriteLine($"Directories created: {stats.DirectoriesCreated}");
            Console.WriteLine($"Incomplete transfers resumed: {stats.IncompleteTransfersResumed}");

            if (stats.FilesWithDateTimeIssues.Count <= 0) return;
            
            Console.WriteLine($"Files skipped due to DateTime issues: {stats.FilesWithDateTimeIssues.Count}");
            Console.WriteLine("These files require manual review:");
            
            foreach (string file in stats.FilesWithDateTimeIssues)
            {
                Console.WriteLine($"  - {file}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error during sync: {ex.Message}");
            Environment.Exit(1);
        }
    }

    private static SyncStats SyncFolders(string sourcePath, string destinationPath)
    {
        var stats = new SyncStats();
        
        // Create destination directory if it doesn't exist
        if (!Directory.Exists(destinationPath))
        {
            Directory.CreateDirectory(destinationPath);
            stats.DirectoriesCreated++;
            Console.WriteLine($"Created directory: {destinationPath}");
        }

        // Process all files in current directory
        foreach (string sourceFile in Directory.GetFiles(sourcePath))
        {
            string fileName = Path.GetFileName(sourceFile);
            string destFile = Path.Combine(destinationPath, fileName);

            var copyResult = ProcessFile(sourceFile, destFile, stats);
            switch (copyResult)
            {
                case CopyResult.Copied:
                    stats.FilesCopied++;
                    Console.WriteLine($"Copied: {fileName}");
                    break;
                case CopyResult.Resumed:
                    stats.IncompleteTransfersResumed++;
                    Console.WriteLine($"Resumed incomplete transfer: {fileName}");
                    break;
                case CopyResult.Skipped:
                    stats.FilesSkipped++;
                    Console.WriteLine($"Skipped: {fileName} (already up-to-date)");
                    break;
                case CopyResult.SkippedDueToDateTimeIssue:
                    stats.FilesSkipped++;
                    Console.WriteLine($"Skipped: {fileName} (DateTime comparison failed)");
                    break;
            }
        }

        // Recursively process subdirectories
        foreach (string sourceDir in Directory.GetDirectories(sourcePath))
        {
            string dirName = Path.GetFileName(sourceDir);
            string destDir = Path.Combine(destinationPath, dirName);
            
            var subStats = SyncFolders(sourceDir, destDir);
            stats.Add(subStats);
        }

        return stats;
    }

    private static CopyResult ProcessFile(string sourceFile, string destFile, SyncStats stats)
    {
        string stubFile = GetStubFileName(destFile);
        bool hasOrphanedStub = File.Exists(stubFile);
        
        var shouldCopyResult = ShouldCopyFile(sourceFile, destFile, hasOrphanedStub, stats);
        
        if (shouldCopyResult.ShouldCopy)
        {
            try
            {
                return AtomicFileCopy(sourceFile, destFile, stubFile, hasOrphanedStub);
            }
            catch (Exception ex)
            {
                // Clean up stub file if copy fails
                if (File.Exists(stubFile))
                {
                    try
                    {
                        File.Delete(stubFile);
                    }
                    catch
                    {
                        // ignored
                    }
                }
                Console.WriteLine($"Warning: Failed to copy '{Path.GetFileName(sourceFile)}': {ex.Message}");
                return CopyResult.Failed;
            }
        }
        
        // Check if this was skipped due to a DateTime issue
        if (shouldCopyResult.WasDueTeDateTimeIssue)
        {
            return CopyResult.SkippedDueToDateTimeIssue;
        }
        
        return CopyResult.Skipped;
    }

    static CopyResult AtomicFileCopy(string sourceFile, string destFile, string stubFile, bool isResumedTransfer)
    {
        // Create transfer stub to indicate copy in progress
        File.WriteAllText(stubFile, $"Transfer started: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

        // Perform the actual file copy
        File.Copy(sourceFile, destFile, overwrite: true);
            
        // Copy successful - remove the stub
        File.Delete(stubFile);
            
        return isResumedTransfer ? CopyResult.Resumed : CopyResult.Copied;
    }

    private static ShouldCopyResult ShouldCopyFile(string sourceFile, string destFile, bool hasOrphanedStub, SyncStats stats)
    {
        // Always copy if there's an orphaned stub (indicates incomplete previous transfer)
        if (hasOrphanedStub)
            return new ShouldCopyResult(true, false);

        // Copy if destination file doesn't exist
        if (!File.Exists(destFile))
            return new ShouldCopyResult(true, false);

        // Try to compare file timestamps - copy if source file is newer than destination file
        try
        {
            DateTime sourceTime = File.GetLastWriteTime(sourceFile);
            DateTime destTime = File.GetLastWriteTime(destFile);
            
            // Validate that we got reasonable DateTime values
            if (sourceTime == DateTime.MinValue || destTime == DateTime.MinValue ||
                sourceTime > DateTime.Now.AddDays(1) || destTime > DateTime.Now.AddDays(1))
            {
                throw new InvalidOperationException("Invalid DateTime values detected");
            }
            
            return new ShouldCopyResult(sourceTime > destTime, false);
        }
        catch (Exception ex)
        {
            // Log the problematic file and skip it
            string fileName = Path.GetFileName(sourceFile);
            stats.FilesWithDateTimeIssues.Add($"{fileName} ({ex.GetType().Name}: {ex.Message})");
            
            // Skip the file when we can't determine timestamps - let user decide what to do
            return new ShouldCopyResult(false, true);
        }
    }

    private static StubDetectionStats DetectOrphanedStubs(string rootPath)
    {
        var stats = new StubDetectionStats();
        
        if (!Directory.Exists(rootPath))
            return stats;

        DetectOrphanedStubsRecursive(rootPath, stats);
        return stats;
    }

    private static void DetectOrphanedStubsRecursive(string directoryPath, StubDetectionStats stats)
    {
        try
        {
            // Find all stub files in current directory
            foreach (var _ in Directory.GetFiles(directoryPath, $"*{TRANSFER_STUB_EXTENSION}"))
            {
                // Count all stubs found - these represent potentially incomplete transfers
                // Don't delete them here - let the sync process handle them
                stats.OrphanedStubsFound++;
            }

            // Recursively process subdirectories
            foreach (string subDir in Directory.GetDirectories(directoryPath))
            {
                DetectOrphanedStubsRecursive(subDir, stats);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Error during stub detection in '{directoryPath}': {ex.Message}");
        }
    }

    private static string GetStubFileName(string originalFile)
    {
        return originalFile + TRANSFER_STUB_EXTENSION;
    }
}

internal enum CopyResult
{
    Copied,
    Resumed,
    Skipped,
    Failed,
    SkippedDueToDateTimeIssue
}

internal class SyncStats
{
    public int FilesCopied { get; set; }
    public int FilesSkipped { get; set; }
    public int DirectoriesCreated { get; set; }
    public int IncompleteTransfersResumed { get; set; }
    public List<string> FilesWithDateTimeIssues { get; } = [];

    public void Add(SyncStats other)
    {
        FilesCopied += other.FilesCopied;
        FilesSkipped += other.FilesSkipped;
        DirectoriesCreated += other.DirectoriesCreated;
        IncompleteTransfersResumed += other.IncompleteTransfersResumed;
        FilesWithDateTimeIssues.AddRange(other.FilesWithDateTimeIssues);
    }
}

internal class StubDetectionStats
{
    public int OrphanedStubsFound { get; set; }
}

internal class ShouldCopyResult(bool shouldCopy, bool wasDueTeDateTimeIssue)
{
    public bool ShouldCopy { get; } = shouldCopy;
    public bool WasDueTeDateTimeIssue { get; } = wasDueTeDateTimeIssue;
}