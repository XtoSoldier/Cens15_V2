namespace CENS15_V2.Helper
{
    public class PasswordHasher
    {
        private readonly IConfiguration _config;
        private readonly int _cost;

        public PasswordHasher(IConfiguration config)
        {
            _config = config;
            _cost = _config.GetValue<int>("Security:BCryptWorkFactor");
        }

        public string Hash(string password)
            => BCrypt.Net.BCrypt.HashPassword(password, workFactor: _cost);

        public bool Verify(string password, string hash)
            => BCrypt.Net.BCrypt.Verify(password, hash);
    }
}
