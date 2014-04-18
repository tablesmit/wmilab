﻿namespace System.Management
{
    using System.Collections.Generic;
    using System.Text.RegularExpressions;

    /// <summary>
    /// Acts as a broker for managing a single WQL query and subsequent executions.
    /// </summary>
    public class ManagementQueryBroker
    {
        #region Constructors

        /// <summary>
        /// Creates a new instance of System.Management.ManagementQueryBroker.
        /// </summary>
        /// <param name="query">The WQL Query to be executed.</param>
        /// <param name="namespacePath">Namespace path of the ManagementPath within which to execute a query.</param>
        /// <param name="connectionOptions">Specifies all settings required to make a WMI connection.</param>
        public ManagementQueryBroker(String query, ManagementScope scope)
        {
            this.query = query;
            this.scope = scope;
            this.queryType = query.GetWqlQueryType();
            this.results = new List<ManagementBaseObject>();

            this.queryObserver = new ManagementOperationObserver();
            this.queryObserver.ObjectReady += new ObjectReadyEventHandler(queryObserver_ObjectReady);
            this.queryObserver.Completed += new CompletedEventHandler(queryObserver_Completed);

            // Extract class name from query
            MatchCollection matches = Regex.Matches(query, @"(select.*from\s+|references\s+of\s+{|associators\s+of\s+{)([A-Za-z0-9_]+)", RegexOptions.IgnoreCase);
            if (matches.Count != 1 || !matches[0].Groups[2].Success)
                throw new ArgumentException("Could not determine class name from query.");

            var className = matches[0].Groups[2];
            var classPath = new ManagementPath(String.Format("\\\\{0}\\{1}:{2}", scope.Path.Server, scope.Path.NamespacePath, className));

            // Get class descriptor to assist with queries
            this.resultClass = new ManagementClass(this.scope, classPath, new ObjectGetOptions());
        }

        #endregion

        #region Fields

        private String query;

        private WqlQueryType queryType;

        private ManagementScope scope;

        private ManagementObjectSearcher querySearcher;

        private ManagementOperationObserver queryObserver;

        private ManagementEventWatcher queryWatcher;

        private Int32 resultCount;

        private Boolean inProgress;

        private List<ManagementBaseObject> results;

        private ManagementClass resultClass;

        #endregion

        #region Properties

        /// <summary>
        /// Gets the WqlQueryType of the WQL Query string.
        /// </summary>
        public WqlQueryType QueryType
        {
            get { return this.queryType; }
        }

        /// <summary>
        /// Gets a value reflecting whether query execution is in progress.
        /// </summary>
        public Boolean InProgress
        {
            get { return this.inProgress; }
        }

        /// <summary>
        /// Gets the number of ManagementObject results returned so far.
        /// </summary>
        public Int32 ResultCount
        {
            get { return this.resultCount; }
        }

        /// <summary>
        /// Gets the list of currently returned query results.
        /// </summary>
        public List<ManagementBaseObject> Results
        {
            get { return this.results; }
        }

        public ManagementClass ResultClass
        {
            get { return this.resultClass; }
        }

        #endregion

        #region Methods

        /// <summary>
        /// Executes the WQL query asynchronously.
        /// </summary>
        public void ExecuteAsync()
        {
            this.OnStarted(this, EventArgs.Empty);

            try
            {
                if (this.ResultClass.IsEvent())
                {
                    // Start an event watcher
                    this.queryWatcher = new ManagementEventWatcher(this.scope, new EventQuery(this.query));
                    this.queryWatcher.EventArrived += new EventArrivedEventHandler(queryWatcher_EventArrived);
                    this.queryWatcher.Stopped += new StoppedEventHandler(queryWatcher_Stopped);
                    this.queryWatcher.Start();
                }

                else
                {
                    // Start a standard async query
                    this.querySearcher = new ManagementObjectSearcher(this.scope, new ObjectQuery(query));
                    this.querySearcher.Get(this.queryObserver);
                }
            }

            catch (ManagementException)
            {
                this.inProgress = false;
            }
        }

        /// <summary>
        /// Cancels asynchronous execution of the WQL Query.
        /// </summary>
        public void Cancel()
        {
            this.queryObserver.Cancel();
            this.querySearcher.Dispose();

            if (this.queryWatcher != null)
            {
                this.queryWatcher.Stop();
                this.queryWatcher.Dispose();
                this.queryWatcher = null;
            }

            // ? this.OnQueryCompleted(this, EventArgs.Empty);
        }

        protected virtual void OnStarted(object sender, EventArgs e)
        {
            this.resultCount = 0;
            this.results.Clear();

            this.inProgress = true;

            if (this.Started != null)
                this.Started(sender, e);
        }

        protected virtual void OnQueryCompleted(object sender, BrokerCompletedEventArgs e)
        {
            this.inProgress = false;

            if (null != this.Completed)
                this.Completed(sender, e);
        }

        protected virtual void OnObjectReady(object sender, BrokerObjectReadyEventArgs e)
        {
            this.resultCount++;
            this.results.Add(e.NewObject);
            
            if (null != this.ObjectReady)
                this.ObjectReady(sender, e);
        }

        #endregion

        #region Event Handlers

        void queryObserver_Completed(object sender, CompletedEventArgs e)
        {
            var args = new BrokerCompletedEventArgs(e.Status == ManagementStatus.NoError, e.StatusObject, e.Status);
            this.OnQueryCompleted(sender, args);
        }

        void queryObserver_ObjectReady(object sender, ObjectReadyEventArgs e)
        {
            var args = new BrokerObjectReadyEventArgs(e.NewObject, this.ResultCount + 1);
            this.OnObjectReady(sender, args);
        }

        void queryWatcher_Stopped(object sender, StoppedEventArgs e)
        {
            var args = new BrokerCompletedEventArgs(e.Status == ManagementStatus.NoError, null, e.Status);
            this.OnQueryCompleted(this, args);
        }

        void queryWatcher_EventArrived(object sender, EventArrivedEventArgs e)
        {
            var args = new BrokerObjectReadyEventArgs(e.NewEvent, this.resultCount + 1);
            this.OnObjectReady(this, args);
        }

        #endregion

        #region Events

        /// <summary>
        /// Occurs when an operation has started.
        /// </summary>
        public event EventHandler Started;

        /// <summary>
        /// Occurs when a new object is available.
        /// </summary>
        public event BrokerObjectReadyEventHandler ObjectReady;

        /// <summary>
        /// Occurs when an operation has completed.
        /// </summary>
        public event BrokerCompletedEventHandler Completed;

        #endregion
    }

    public delegate void BrokerObjectReadyEventHandler(object sender, BrokerObjectReadyEventArgs e);

    public delegate void BrokerCompletedEventHandler(object sender, BrokerCompletedEventArgs e);

    public class BrokerObjectReadyEventArgs : EventArgs
    {
        internal BrokerObjectReadyEventArgs(ManagementBaseObject newObject, Int32 index)
        {
            this.NewObject = newObject;
            this.Index = index;
        }

        public ManagementBaseObject NewObject { get; private set; }

        public Int32 Index { get; private set; }
    }

    public class BrokerCompletedEventArgs : EventArgs
    {
        internal BrokerCompletedEventArgs(Boolean success, ManagementBaseObject statusObject, ManagementStatus status)
        {
            this.Success = success;
            this.StatusObject = statusObject;
            this.Status = status;
        }

        public Boolean Success { get; private set; }

        public ManagementBaseObject StatusObject { get; private set; }

        public ManagementStatus Status { get; private set; }
    }
}
