﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Build.Construction;
using Microsoft.Build.Evaluation;

namespace Microsoft.VisualStudio.ProjectSystem
{
    /// <summary>
    ///     Provides an implementation of <see cref="IProjectAccessor"/> that delegates onto
    ///     the <see cref="IProjectLockService"/>.
    /// </summary>
    [Export(typeof(IProjectAccessor))]
    internal class ProjectAccessor : IProjectAccessor
    {
        // NOTE: It is very deliberate that we ConfigureAwait(true) in this class to switch 
        // back to the thread type ("threadpool") where XXXLockAsync switched us too.

        private readonly IProjectLockService _projectLockService;

        [ImportingConstructor]
        public ProjectAccessor(IProjectLockService projectLockService)
        {
            _projectLockService = projectLockService;
        }

        public async Task EnterWriteLockAsync(Func<ProjectCollection, CancellationToken, Task> action, CancellationToken cancellationToken = default)
        {
            Requires.NotNull(action, nameof(action));

            using (ProjectWriteLockReleaser access = await _projectLockService.WriteLockAsync(cancellationToken))
            {
                // Only async to let the caller call one of the other project accessor methods
                await action(access.ProjectCollection, cancellationToken);

                // Avoid blocking thread on Dispose
                await access.ReleaseAsync();
            }
        }

        public async Task<TResult> OpenProjectForReadAsync<TResult>(ConfiguredProject project, Func<Project, TResult> action, CancellationToken cancellationToken = default)
        {
            Requires.NotNull(project, nameof(project));
            Requires.NotNull(project, nameof(action));

            using (ProjectLockReleaser access = await _projectLockService.ReadLockAsync(cancellationToken))
            {
                Project evaluatedProject = await access.GetProjectAsync(project, cancellationToken);

                // Deliberately not async to reduce the type of
                // code you can run while holding the lock.
                return action(evaluatedProject);
            }
        }

        public async Task<TResult> OpenProjectXmlForReadAsync<TResult>(UnconfiguredProject project, Func<ProjectRootElement, TResult> action, CancellationToken cancellationToken = default)
        {
            Requires.NotNull(project, nameof(project));
            Requires.NotNull(project, nameof(action));

            using (ProjectLockReleaser access = await _projectLockService.ReadLockAsync(cancellationToken))
            {
                ProjectRootElement rootElement = await access.GetProjectXmlAsync(project.FullPath, cancellationToken);

                // Deliberately not async to reduce the type of
                // code you can run while holding the lock.
                return action(rootElement);
            }
        }

        public async Task OpenProjectXmlForUpgradeableReadAsync(UnconfiguredProject project, Func<ProjectRootElement, CancellationToken, Task> action, CancellationToken cancellationToken = default)
        {
            Requires.NotNull(project, nameof(project));
            Requires.NotNull(project, nameof(action));

            using (ProjectLockReleaser access = await _projectLockService.UpgradeableReadLockAsync(cancellationToken))
            {
                ProjectRootElement rootElement = await access.GetProjectXmlAsync(project.FullPath, cancellationToken);

                // Only async to let the caller upgrade to a 
                // write lock via OpenProjectXmlForWriteAsync
                await action(rootElement, cancellationToken);
            }
        }

        public async Task OpenProjectXmlForWriteAsync(UnconfiguredProject project, Action<ProjectRootElement> action, CancellationToken cancellationToken = default)
        {
            Requires.NotNull(project, nameof(project));
            Requires.NotNull(project, nameof(action));

            using (ProjectWriteLockReleaser access = await _projectLockService.WriteLockAsync(cancellationToken))
            {
                await access.CheckoutAsync(project.FullPath);

                ProjectRootElement rootElement = await access.GetProjectXmlAsync(project.FullPath, cancellationToken);

                // Deliberately not async to reduce the type of
                // code you can run while holding the lock.
                action(rootElement);

                // Avoid blocking thread on Dispose
                await access.ReleaseAsync();
            }
        }

        public async Task OpenProjectForWriteAsync(ConfiguredProject project, Action<Project> action, CancellationToken cancellationToken = default)
        {
            Requires.NotNull(project, nameof(project));
            Requires.NotNull(project, nameof(action));

            using (ProjectWriteLockReleaser access = await _projectLockService.WriteLockAsync(cancellationToken))
            {
                await access.CheckoutAsync(project.UnconfiguredProject.FullPath);

                Project evaluatedProject = await access.GetProjectAsync(project, cancellationToken);

                // Deliberately not async to reduce the type of
                // code you can run while holding the lock.
                action(evaluatedProject);

                // Avoid blocking thread on Dispose
                await access.ReleaseAsync();
            }
        }
    }
}
