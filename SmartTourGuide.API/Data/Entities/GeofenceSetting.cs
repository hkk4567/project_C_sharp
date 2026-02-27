using System.ComponentModel.DataAnnotations;
namespace SmartTourGuide.API.Data.Entities
{
    public class GeofenceSetting
    {
        [Key]
        public int Id { get; set; }

        // Foreign Key 1-1 với Poi
        public int PoiId { get; set; }
        public virtual Poi Poi { get; set; } = null!;
        // Bán kính kích hoạt (mét). Vd: Vào vùng 50m thì bắt đầu nói.
        public double TriggerRadiusInMeters { get; set; } = 50;

        // Slide 2: "Mức ưu tiên"
        public int Priority { get; set; } = 1;

        // Slide 2: "Cooldown" (giây). 
        // Nếu vừa nghe xong, thì bao lâu sau mới được kích hoạt lại để tránh spam.
        public int CooldownInSeconds { get; set; } = 300;
    }
}
