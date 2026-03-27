using System.ComponentModel.DataAnnotations;

namespace SmartTourGuide.API.Data.Entities;

public class OwnerNotification
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int OwnerId { get; set; }

    public int? PoiId { get; set; }

    [Required]
    [MaxLength(100)]
    public string AdminUsername { get; set; } = string.Empty;

    [Required]
    [MaxLength(150)]
    public string Title { get; set; } = string.Empty;

    [Required]
    [MaxLength(500)]
    public string Message { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public bool IsRead { get; set; } = false;
}
