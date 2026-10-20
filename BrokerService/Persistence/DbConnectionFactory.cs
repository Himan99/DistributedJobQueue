using Npgsql;

namespace DistributedJobQueue.Core.Persistence
{
    public class DbConnectionFactory
    {
        private readonly string _connectionString;

        public DbConnectionFactory(string connectionString)
        {
            _connectionString = connectionString;
        }

        public NpgsqlConnection CreateConnection() => new(_connectionString);
    }
}
