using System.ComponentModel.DataAnnotations;
namespace SmartTourGuide.API.Data.Entities
{
    public class UserLocationLog
    {
        [Key]
        public long Id { get; set; }
        public int UserId { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public DateTime Timestamp { get; set; }
    }
}
