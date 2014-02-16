﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.TestWindow.Extensibility;

namespace DanTup.TestAdapters
{
	/// <summary>
	/// Base TestContainerDiscoverer that scans projects for certain file extensions assumed to be tests and
	/// watches them for changes, notifying VS when the tests may have been updated.
	/// </summary>
	public abstract class TestContainerDiscoverer : ITestContainerDiscoverer, IDisposable, IVsSolutionEvents
	{
		public abstract Uri ExecutorUri { get; }
		protected abstract string TestContainerFileExtension { get; }
		protected abstract string[] WatchedFilePatterns { get; }

		readonly IVsSolution solutionService;
		readonly MultiFileSystemWatcher watchers = new MultiFileSystemWatcher();
		MultiFileSystemWatcher watcher;
		TestContainer[] cachedTestContainers;

		protected TestContainerDiscoverer(IServiceProvider serviceProvider)
		{
			this.solutionService = (IVsSolution)serviceProvider.GetService(typeof(SVsSolution));
			uint pdwCookie;
			this.solutionService.AdviseSolutionEvents(this, out pdwCookie);
		}

		public IEnumerable<ITestContainer> TestContainers
		{
			get
			{
				if (cachedTestContainers == null)
					UpdateTestContainers();

				return cachedTestContainers;
			}
		}

		/// <summary>
		/// Updates the (cached) list of test containers if it's not already populated.
		/// </summary>
		void UpdateTestContainers()
		{
			lock (this) // HACK: Make all this better
			{
				if (cachedTestContainers != null)
					return;

				cachedTestContainers = solutionService
					.GetProjects()
					.SelectMany(p => p.GetProjectItems())
					.Where(File.Exists)
					.Where(f => f.EndsWith(this.TestContainerFileExtension))
					.Select(f => new TestContainer(this, f))
					.ToArray();

				SetupWatchers();
			}
		}

		/// <summary>
		/// Sets up filesystem watches to watch directories for each solution for changes.
		/// </summary>
		private void SetupWatchers()
		{
			if (watcher != null)
				watcher.Dispose();

			watcher = new MultiFileSystemWatcher();
			watcher.FileChanged += TestContainerUpdated;

			// Get all directories that had test containers
			var dirs = cachedTestContainers
				.Select(tc => Path.GetDirectoryName(tc.Source))
				.Distinct();

			foreach (var dir in dirs)
				foreach (var filePattern in this.WatchedFilePatterns)
					watcher.AddWatcher(dir, filePattern);
		}

		private void TestContainerUpdated(object sender, EventArgs e)
		{
			cachedTestContainers = null;

			var evt = TestContainersUpdated;
			if (evt != null)
				evt(this, EventArgs.Empty);
		}

		public event EventHandler TestContainersUpdated;

		#region IDisposable Cruft

		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		protected virtual void Dispose(bool disposing)
		{
			if (disposing)
			{
				if (watcher != null)
				{
					watcher.Dispose();
					watcher = null;
				}
			}
		}

		#endregion

		#region IVsSolutionEvents Cruft

		int IVsSolutionEvents.OnAfterCloseSolution(object pUnkReserved)
		{
			return VSConstants.S_OK;
		}

		int IVsSolutionEvents.OnAfterLoadProject(IVsHierarchy pStubHierarchy, IVsHierarchy pRealHierarchy)
		{
			return VSConstants.S_OK;
		}

		int IVsSolutionEvents.OnAfterOpenProject(IVsHierarchy pHierarchy, int fAdded)
		{
			TestContainerUpdated(this, EventArgs.Empty);
			return VSConstants.S_OK;
		}

		int IVsSolutionEvents.OnAfterOpenSolution(object pUnkReserved, int fNewSolution)
		{
			TestContainerUpdated(this, EventArgs.Empty);
			return VSConstants.S_OK;
		}

		int IVsSolutionEvents.OnBeforeCloseProject(IVsHierarchy pHierarchy, int fRemoved)
		{
			return VSConstants.S_OK;
		}

		int IVsSolutionEvents.OnBeforeCloseSolution(object pUnkReserved)
		{
			return VSConstants.S_OK;
		}

		int IVsSolutionEvents.OnBeforeUnloadProject(IVsHierarchy pRealHierarchy, IVsHierarchy pStubHierarchy)
		{
			return VSConstants.S_OK;
		}

		int IVsSolutionEvents.OnQueryCloseProject(IVsHierarchy pHierarchy, int fRemoving, ref int pfCancel)
		{
			return VSConstants.S_OK;
		}

		int IVsSolutionEvents.OnQueryCloseSolution(object pUnkReserved, ref int pfCancel)
		{
			return VSConstants.S_OK;
		}

		int IVsSolutionEvents.OnQueryUnloadProject(IVsHierarchy pRealHierarchy, ref int pfCancel)
		{
			return VSConstants.S_OK;
		}

		#endregion
	}
}
