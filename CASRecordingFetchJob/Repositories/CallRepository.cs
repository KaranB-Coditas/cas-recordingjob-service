using System.Data;

namespace CASRecordingFetchJob.Repositories
{
    public class CallRepository
    {
        private readonly IDbConnection _connection;

        public CallRepository(IDbConnection connection)
        {
            _connection = connection;
        }
    }
}
