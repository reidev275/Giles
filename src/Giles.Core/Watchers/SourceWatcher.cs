using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Timers;
using Giles.Core.AppDomains;
using Giles.Core.Configuration;
using Giles.Core.IO;
using Giles.Core.Runners;
using Giles.Core.Utility;

namespace Giles.Core.Watchers
{
    public class SourceWatcher : IDisposable
    {
        readonly IBuildRunner buildRunner;
        readonly Timer buildTimer;
        readonly IFileSystem fileSystem;
        readonly IFileWatcherFactory fileWatcherFactory;
        readonly GilesConfig config;

        public List<FileSystemWatcher> FileWatchers { get; set; }

        public SourceWatcher(IBuildRunner buildRunner, IFileSystem fileSystem,
                             IFileWatcherFactory fileWatcherFactory, GilesConfig config)
        {
            FileWatchers = new List<FileSystemWatcher>();
            this.fileSystem = fileSystem;
            this.buildRunner = buildRunner;
            this.fileWatcherFactory = fileWatcherFactory;
            this.config = config;
            buildTimer = new Timer { AutoReset = false, Enabled = false, Interval = config.BuildDelay };
            config.PropertyChanged += config_PropertyChanged;
            buildTimer.Elapsed += (sender, e) => RunNow();
        }

        void config_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName != "BuildDelay") return;
            
            var buildDelay = ((GilesConfig) sender).BuildDelay;

            buildTimer.Interval = buildDelay;
        }

        public void Dispose()
        {
            FileWatchers.ToList().ForEach(x => x.Dispose());
        }

        public void Watch(string solutionPath, string filter)
        {
            var solutionFolder = fileSystem.GetDirectoryName(solutionPath);
            var fileSystemWatcher = fileWatcherFactory.Build(solutionFolder, filter, ChangeAction, null,
                                                                           ErrorAction);
            fileSystemWatcher.EnableRaisingEvents = true;
            fileSystemWatcher.NotifyFilter = NotifyFilters.LastWrite;
            fileSystemWatcher.IncludeSubdirectories = true;

            FileWatchers.Add(fileSystemWatcher);
        }

        public void ErrorAction(object sender, ErrorEventArgs e)
        {
            throw new NotImplementedException();
        }

        public void ChangeAction(object sender, FileSystemEventArgs e)
        {
            if (buildTimer.Enabled)
                ResetBuildTimer();
            else
                buildTimer.Enabled = true;
        }

        void ResetBuildTimer()
        {
            buildTimer.Enabled = false;
            buildTimer.Enabled = true;
        }

        public Func<GilesConfig, GilesTestListener> GetListener = config => new GilesTestListener(config);

        public void RunNow()
        {
            if (!buildRunner.Run())
                return;

            var listener = GetListener.Invoke(config);

            var manager = new GilesAppDomainManager();

            var runResults = new List<SessionResults>();

            config.TestAssemblies.Each(assm => runResults.AddRange(manager.Run(assm)));

            runResults.Each(result =>
                               {
                                   result.Messages.Each(m => listener.WriteLine(m, "Output"));
                                   result.TestResults.Each(listener.AddTestSummary);
                               });

            listener.DisplayResults();

            LastRunResults.GilesTestListener = listener;
        }
    }
}