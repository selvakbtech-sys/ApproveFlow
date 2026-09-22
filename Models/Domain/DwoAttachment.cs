namespace ApproveFlow.MVC.Models.Domain;

// Every file uploaded anywhere in the DWO module. Exactly one of PointId / SetId is set;
// Kind says which slot it fills (see DwoAttachmentKind). Storage follows the existing
// request-document convention: a GUID file name under AppSettings:FileStoragePath.
public class DwoAttachment
{
    [Key]
    public int AttachmentId { get; set; }
    public int? PointId { get; set; }
    public int? SetId { get; set; }
    [MaxLength(40)]
    public string Kind { get; set; } = DwoAttachmentKind.Point;
    public string FileName { get; set; } = "";
    public string StoredFileName { get; set; } = "";
    public long FileSizeBytes { get; set; }
    public string? ContentType { get; set; }
    public DateTimeOffset UploadedAt { get; set; }
    public int UploadedByUserId { get; set; }

    public virtual DwoPoint? Point { get; set; }
    public virtual DwoSet? Set { get; set; }
    public virtual User UploadedByUser { get; set; } = null!;

    [NotMapped]
    public string FormattedSize =>
        FileSizeBytes > 1_048_576 ? $"{FileSizeBytes / 1_048_576.0:F1} MB"
        : $"{FileSizeBytes / 1024} KB";
}
