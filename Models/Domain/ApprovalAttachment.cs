namespace ApproveFlow.MVC.Models.Domain;

public class ApprovalAttachment
{
    [Key]
    public int AttachmentId { get; set; }
    public int RequestId { get; set; }
    public int StepLevel { get; set; }
    public string ActionType { get; set; } = "";
    public string FileName { get; set; } = "";
    public string StoredFileName { get; set; } = "";
    public long FileSizeBytes { get; set; }
    public string? ContentType { get; set; }
    public int UploadedByUserId { get; set; }
    public DateTimeOffset UploadedAt { get; set; }
    public virtual Request Request { get; set; } = null!;
    public virtual User UploadedByUser { get; set; } = null!;
    [NotMapped]
    public string FormattedSize =>
        FileSizeBytes > 1_048_576 ? $"{FileSizeBytes / 1_048_576.0:F1} MB"
        : $"{FileSizeBytes / 1024} KB";
}
