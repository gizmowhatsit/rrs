namespace rrs;
// rapid resync - jvincent 2025

internal abstract class Program
{
    private static void Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.WriteLine("Usage: rrs <source-folder> <destination-folder>");
            Console.WriteLine("Example: rrs C:\\Source C:\\Backup");
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

            var stats = SyncFolders(sourcePath, destinationPath);

            Console.WriteLine(new string('-', 60));
            Console.WriteLine("Sync completed successfully!");
            Console.WriteLine($"Files copied: {stats.FilesCopied}");
            Console.WriteLine($"Files skipped (already up-to-date): {stats.FilesSkipped}");
            Console.WriteLine($"Directories created: {stats.DirectoriesCreated}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error during sync: {ex.Message}");
            Environment.Exit(1);
        }
    }

    static SyncStats SyncFolders(string sourcePath, string destinationPath)
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

            if (ShouldCopyFile(sourceFile, destFile))
            {
                try
                {
                    File.Copy(sourceFile, destFile, overwrite: true);
                    stats.FilesCopied++;
                    Console.WriteLine($"Copied: {fileName}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Warning: Failed to copy '{fileName}': {ex.Message}");
                }
            }
            else
            {
                stats.FilesSkipped++;
                Console.WriteLine($"Skipped: {fileName} (already up-to-date)");
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

    static bool ShouldCopyFile(string sourceFile, string destFile)
    {
        // Copy if destination file doesn't exist
        if (!File.Exists(destFile))
            return true;

        // Copy if source file is newer than destination file
        DateTime sourceTime = File.GetLastWriteTime(sourceFile);
        DateTime destTime = File.GetLastWriteTime(destFile);
        
        return sourceTime > destTime;
    }
}

class SyncStats
{
    public int FilesCopied { get; set; }
    public int FilesSkipped { get; set; }
    public int DirectoriesCreated { get; set; }

    public void Add(SyncStats other)
    {
        FilesCopied += other.FilesCopied;
        FilesSkipped += other.FilesSkipped;
        DirectoriesCreated += other.DirectoriesCreated;
    }
}
