namespace CENS15_V2.Models
{
    public class Token
    {
        public Guid Id { get; set; }
        public string AccessToken { get; set; }

        public Auth Auth { get; set; }
    }
}
