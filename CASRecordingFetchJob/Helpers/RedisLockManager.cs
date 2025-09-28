using StackExchange.Redis;
using System;
using System.Threading.Tasks;

namespace CASRecordingFetchJob.Helpers
{
    public interface IDistributedLockManager
    {
        Task<IDisposable?> AcquireLockAsync(string resourceKey, TimeSpan expiry);
    }
    public class RedisLockManager : IDistributedLockManager
    {
        private readonly IConnectionMultiplexer _redis;

        public RedisLockManager(IConnectionMultiplexer redis)
        {
            _redis = redis;
        }
        public async Task<IDisposable?> AcquireLockAsync(string resourceKey, TimeSpan expiry)
        {
            var db = _redis.GetDatabase();
            var lockKey = $"lock:{resourceKey}";
            var lockValue = Guid.NewGuid().ToString();

            bool acquired = await db.StringSetAsync(key: lockKey, value: lockValue, expiry: expiry, when: When.NotExists);

            if (!acquired)
                return null;

            return new RedisLock(db, lockKey, lockValue);
        }

        private class RedisLock : IDisposable
        {
            private readonly IDatabase _db;
            private readonly string _lockKey;
            private readonly string _lockValue;
            private bool _released;

            public RedisLock(IDatabase db, string lockKey, string lockValue)
            {
                _db = db;
                _lockKey = lockKey;
                _lockValue = lockValue;
            }

            public void Dispose()
            {
                if (_released) return;

                var script = @"
                if redis.call('get', KEYS[1]) == ARGV[1] then
                    return redis.call('del', KEYS[1])
                else
                    return 0
                end";

                _db.ScriptEvaluate(script, new RedisKey[] { _lockKey }, new RedisValue[] { _lockValue });
                _released = true;
            }
        }

    }

    
}
