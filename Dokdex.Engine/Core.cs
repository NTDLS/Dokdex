﻿using Dokdex.Engine.Documents;
using Dokdex.Engine.Health;
using Dokdex.Engine.IO;
using Dokdex.Engine.Locking;
using Dokdex.Engine.Logging;
using Dokdex.Engine.Schemas;
using Dokdex.Engine.Transactions;
using Dokdex.Library;
using System.Diagnostics;
using System.Reflection;
using Dokdex.Engine.Sessions;
using Dokdex.Engine.Caching;
using Dokdex.Engine.Indexes;
using Dokdex.Engine.Query;

namespace Dokdex.Engine
{
    public class Core
    {
        public SchemaManager Schemas;
        public IOManager IO;
        public LockManager Locking;
        public DocumentManager Documents;
        public TransactionManager Transactions;
        public Settings settings;
        public LogManager Log;
        public HealthManager Health;
        public SessionManager Sessions;
        public CacheManager Cache;
        public PersistIndexManager Indexes;
        public QueryManager Query;

        public Core(Settings settings)
        {
            this.settings = settings;

            Log = new LogManager(this);

            Assembly assembly = Assembly.GetExecutingAssembly();
            FileVersionInfo fileVersionInfo = FileVersionInfo.GetVersionInfo(assembly.Location);
            Log.Write(string.Format("{0} v{1} PID:{2}",
                fileVersionInfo.ProductName,
                fileVersionInfo.ProductVersion,
                Process.GetCurrentProcess().Id));

            Log.Write("Initializing cache manager.");
            Cache = new CacheManager(this);

            Log.Write("Initializing IO manager.");
            IO = new IOManager(this);

            Log.Write("Initializing health manager.");
            Health = new HealthManager(this);

            Log.Write("Initializing index manager.");
            Indexes = new PersistIndexManager(this);

            Log.Write("Initializing session manager.");
            Sessions = new SessionManager(this);

            Log.Write("Initializing lock manager.");
            Locking = new LockManager(this);

            Log.Write("Initializing transaction manager.");
            Transactions = new TransactionManager(this);

            Log.Write("Initializing namespace manager.");
            Schemas = new SchemaManager(this);

            Log.Write("Initializing document manager.");
            Documents = new DocumentManager(this);

            Log.Write("Initializing query manager.");
            Query = new QueryManager(this);

            Log.Write("Initilization complete.");
        }

        public void Start()
        {
            Log.Write("Starting server.");

            Transactions.Recover();
        }

        public void Shutdown()
        {
            Log.Write("Shutting down server.");

            Health.Close();
            Log.Close();
        }
    }
}
