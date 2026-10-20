namespace DistributedJobQueue.Core.Domain
{
    [Serializable]
    public class JobPersistenceException : Exception
    {
        public JobPersistenceException()
        {
        }

        public JobPersistenceException(string? message) : base(message)
        {
        }

        public JobPersistenceException(string? message, Exception? innerException) : base(message, innerException)
        {
        }
    }
}