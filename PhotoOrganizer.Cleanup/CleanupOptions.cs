namespace PhotoOrganizer.Cleanup;

public class CleanupOptions
{
    public bool CleanS3 { get; set; }
    public bool CleanDb { get; set; }
    public bool DryRun { get; set; }
    public bool AssumeYes { get; set; }
}