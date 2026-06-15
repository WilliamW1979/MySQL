namespace MySQL
{
    internal class MySqlConnectionStringBuilder
    {
        public string Server { get; set; } = string.Empty;
        public uint Port { get; set; }
        public string Database { get; set; } = string.Empty;
        public string UserID { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public bool Pooling { get; set; }
        public uint MinimumPoolSize { get; set; }
        public uint MaximumPoolSize { get; set; }
        public uint ConnectionTimeout { get; set; }
        public bool AllowZeroDateTime { get; set; }
        public bool ConvertZeroDateTime { get; set; }
        public object SslMode { get; set; } = null!;
    }
}