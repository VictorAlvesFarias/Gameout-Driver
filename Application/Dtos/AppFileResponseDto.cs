namespace Application.Dtos
{
    public class AppFileResponseDto
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Path { get; set; }
        public bool VersionControl { get; set; }
        public bool Observer { get; set; }
        public DateTime CreateDate { get; set; }
        public DateTime UpdateDate { get; set; }
        public bool Synced { get; set; }
        public string UserId { get; set; }
        public bool AutoValidateSync { get; set; }
    }
}
